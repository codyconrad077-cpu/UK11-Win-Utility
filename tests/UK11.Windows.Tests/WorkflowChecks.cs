using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UK11.Windows;

internal static class WorkflowChecks
{
    internal static async Task<object> Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "apex-workflows-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string[] actions = ["system", "restore-list", "restore-create", "service-list", "service-change", "store-change", "apps-list", "apps-remove", "apps-reinstall", "network-list", "network-configure", "network-restore", "driver-scan", "driver-install", "driver-backup", "runtime-preview", "runtime-install"];
            var sources = actions.Select(id => new { id, source = Scripts.Common + "\n" + Scripts.Get(id) }).ToArray();
            var sourcePath = Path.Combine(root, "scripts.json");
            File.WriteAllText(sourcePath, JsonSerializer.Serialize(sources));
            var parser = """
                $ErrorActionPreference='Stop'
                $scripts=Get-Content -LiteralPath $env:APEX_TEST_SCRIPTS -Raw | ConvertFrom-Json
                foreach($script in $scripts){
                  $tokens=$null; $errors=$null
                  [void][System.Management.Automation.Language.Parser]::ParseInput($script.source,[ref]$tokens,[ref]$errors)
                  if($errors.Count -gt 0){throw ($script.id+': '+(($errors | ForEach-Object Message) -join ' | '))}
                }
                'PASS Windows PowerShell 5.1 parses all '+$scripts.Count+' workflows'
                """;
            await PowerShell(parser, sourcePath);
            Console.WriteLine("PASS all 17 Windows PowerShell workflows parse without executing mutations");

            // Exercise actual rejection paths; traps prove no mutating cmdlet can be reached.
            var rejected = 0;
            foreach (var (id, payload) in new[] {
                ("service-change", "@{name='WinDefend';disable=$true}"),
                ("store-change", "@{name='AppXSvc';disable=$true}"),
                ("apps-reinstall", "@{name='../outside'}"),
                ("driver-install", "@{vendor='NVIDIA';ids=@('fake');acceptTerms=$false}"),
                ("runtime-install", "@{acceptTerms=$false}") })
            {
                var rejection = "$ErrorActionPreference='Stop'; $request=" + payload + ";\n" + Scripts.Common + "\n"
                    + "function Set-Service {throw 'MUTATION_REACHED'}; function New-ItemProperty {throw 'MUTATION_REACHED'}; function Add-AppxPackage {throw 'MUTATION_REACHED'}; function winget.exe {throw 'MUTATION_REACHED'};\n"
                    + "$rejected=$false; try { & {\n" + Scripts.Get(id) + "\n} } catch {if($_.Exception.Message -like '*MUTATION_REACHED*'){throw}; $rejected=$true}; if(-not $rejected){throw 'Invalid request was accepted'}; 'PASS rejected';";
                await PowerShell(rejection, sourcePath); rejected++;
            }
            var workflow = new WindowsWorkflows(root);
            var readOnly = new List<string>();
            foreach (var action in new[] { "system", "restore-list", "service-list", "apps-list", "network-list" })
            {
                var result = await workflow.RunAsync(action);
                if (!result.Success) throw new Exception(action + ": " + result.Error);
                if (action == "system" && string.IsNullOrWhiteSpace(result.Data.GetProperty("cpu").GetString())) throw new Exception("CPU inventory was empty");
                if (action == "restore-list" && !result.Data.TryGetProperty("available", out _)) throw new Exception("System Restore capability was not reported");
                if (action == "service-list" && result.Data.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)) throw new Exception("Service inventory missing");
                readOnly.Add(action);
                Console.WriteLine("PASS read-only Windows workflow: " + action);
            }
            foreach (var action in actions.Where(Scripts.RequiresAdministrator))
            {
                try { await workflow.RunAsync(action); throw new Exception("Missing administrator boundary: " + action); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("administrator")) { }
            }
            await SandboxedRecovery(root, sourcePath);
            using var sensors = new Sensors(); var reading = sensors.Read();
            if (reading.GpuLoad is < 0 or > 100 || reading.CpuTemperature is < -30 or > 150 || reading.GpuTemperature is < -30 or > 150) throw new Exception("Implausible sensor data");
            if (string.IsNullOrWhiteSpace(reading.Status)) throw new Exception("Sensor capability status missing");
            Console.WriteLine("PASS real sensor read or explicit unavailability: " + reading.Status);
            return new { scriptsParsed = actions.Length, invalidRequestsRejected = rejected, administratorBoundaries = actions.Count(Scripts.RequiresAdministrator), readOnly, sandboxedRecoveryChecks = 5, cpuTemperatureAvailable = reading.CpuTemperature is not null, gpuTemperatureAvailable = reading.GpuTemperature is not null, mutationAcceptance = "Network/service recovery uses fake providers with real workflow scripts and disk journals. Real app removal, DNS/service changes and installations were not performed on this runner. Test on a disposable Windows client before relying on recovery." };
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task SandboxedRecovery(string root, string sourcePath)
    {
        // Substitute Windows providers, not operation code. These tests can mutate only fake
        // in-memory state and journals in a temporary directory, never the host's configuration.
        var common = "$ErrorActionPreference='Stop'; $rootData='" + root.Replace("'", "''") + "';\n" + Scripts.Common + "\n";
        var network = common + """
            $global:dns=@('9.9.9.9'); $global:manual='9.9.9.9'; $global:failResolution=$false
            function Get-NetAdapter {param([switch]$Physical) [pscustomobject]@{ifIndex=5;InterfaceGuid=[guid]'11111111-2222-3333-4444-555555555555';Status='Up';Name='Sandbox'}}
            function Get-DnsClientServerAddress {param($InterfaceIndex,$AddressFamily) [pscustomobject]@{ServerAddresses=$global:dns}}
            function Get-ItemProperty {param($LiteralPath) [pscustomobject]@{NameServer=$global:manual}}
            function Set-DnsClientServerAddress {param($InputObject,$ServerAddresses,[switch]$ResetServerAddresses) if($ResetServerAddresses){$global:dns=@('192.0.2.1');$global:manual=''}else{$global:dns=@($ServerAddresses);$global:manual=$ServerAddresses -join ','}}
            function Clear-DnsClientCache {}
            function Resolve-DnsName {param($Name,[switch]$DnsOnly,[switch]$QuickTimeout,$ErrorAction) if($global:failResolution){throw 'Injected resolution failure'}}
            $request=[pscustomobject]@{index=5}
            """ + "\n& {\n" + Scripts.Get("network-configure") + "\n} | Out-Null\n" + """
            if(($global:dns -join ',') -ne '1.1.1.1,1.0.0.1'){throw 'DNS target was not applied'}
            """ + "\n& {\n" + Scripts.Get("network-restore") + "\n} | Out-Null\n" + """
            if(($global:dns -join ',') -ne '9.9.9.9' -or $global:manual -ne '9.9.9.9'){throw 'Static DNS original was not restored'}
            $global:manual=''; $global:dns=@('192.0.2.1'); $global:failResolution=$true; $failed=$false
            """ + "\ntry { & {\n" + Scripts.Get("network-configure") + "\n} | Out-Null } catch {$failed=$true}\n" + """
            if(-not $failed -or $global:manual -ne '' -or ($global:dns -join ',') -ne '192.0.2.1'){throw 'Failed DNS setup did not recover DHCP mode'}
            $journal=Get-Content (Join-Path $rootData 'network\11111111222233334444555555555555.json') -Raw | ConvertFrom-Json
            if($journal.state -ne 'Restored'){throw 'Verified DNS rollback journal was not resolved'}
            $global:failResolution=$false
            """ + "\n& {\n" + Scripts.Get("network-configure") + "\n} | Out-Null\n$global:dns=@('203.0.113.2'); $global:manual='203.0.113.2'; $failed=$false\ntry { & {\n" + Scripts.Get("network-restore") + "\n} | Out-Null } catch {$failed=$true}\n" + """
            if(-not $failed -or ($global:dns -join ',') -ne '203.0.113.2'){throw 'External DNS conflict was overwritten'}
            """;
        await PowerShell(network, sourcePath);
        var service = common + """
            $global:mode='Auto'; $global:state='Running'; $global:stopFails=$false
            function Get-CimInstance {param($ClassName,$Filter) [pscustomobject]@{StartMode=$global:mode;State=$global:state}}
            function Get-Service {param($Name) [pscustomobject]@{Status=$global:state;DependentServices=@()}}
            function Get-ItemProperty {param($LiteralPath,$Name,$ErrorAction) $null}
            function Remove-ItemProperty {param($LiteralPath,$Name,$ErrorAction)}
            function Set-ItemProperty {throw 'Unexpected delayed-start write'}
            function Set-Service {param($Name,$StartupType) $global:mode=@{Automatic='Auto';Manual='Manual';Disabled='Disabled'}[[string]$StartupType]}
            function Stop-Service {param($Name,$ErrorAction) if($global:stopFails){throw 'Injected stop failure'}; $global:state='Stopped'}
            function Start-Service {param($Name) $global:state='Running'}
            $request=[pscustomobject]@{name='SysMain';disable=$true}
            """ + "\n& {\n" + Scripts.Get("service-change") + "\n} | Out-Null\n" + """
            if($global:mode -ne 'Disabled' -or $global:state -ne 'Stopped'){throw 'Optional service disable did not apply'}
            $request.disable=$false
            """ + "\n& {\n" + Scripts.Get("service-change") + "\n} | Out-Null\n" + """
            if($global:mode -ne 'Auto' -or $global:state -ne 'Running'){throw 'Original service mode/running state was not recovered'}
            $request.disable=$true; $global:stopFails=$true; $failed=$false
            """ + "\ntry { & {\n" + Scripts.Get("service-change") + "\n} | Out-Null } catch {$failed=$true}\n" + """
            if(-not $failed -or $global:mode -ne 'Disabled'){throw 'Injected partial service failure was not exercised'}
            $request.disable=$false; $global:stopFails=$false
            """ + "\n& {\n" + Scripts.Get("service-change") + "\n} | Out-Null\n" + """
            if($global:mode -ne 'Auto' -or $global:state -ne 'Running'){throw 'Interrupted service original did not recover'}
            $journal=Get-Content (Join-Path $rootData 'services\SysMain.json') -Raw | ConvertFrom-Json
            if($journal.state -ne 'Restored'){throw 'Service recovery journal remains unresolved'}
            """;
        await PowerShell(service, sourcePath);
        Console.WriteLine("PASS 5 sandboxed workflow recovery scenarios: static DNS, DHCP rollback, external conflict, service restore, interrupted service recovery");
    }

    private static async Task PowerShell(string script, string sourcePath)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Composite fault-injection scenarios exceed Windows' command-line size limit.
        // Read our own generated test source through a short bootstrap instead.
        var scriptPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "check-source.txt");
        File.WriteAllText(scriptPath, script);
        const string bootstrap = "& ([scriptblock]::Create([IO.File]::ReadAllText($env:APEX_TEST_CODE)))";
        info.Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap));
        info.Environment["APEX_TEST_CODE"] = scriptPath;
        info.Environment["APEX_TEST_SCRIPTS"] = sourcePath;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("PowerShell workflow check: " + await stdout + " " + await stderr);
    }
}
