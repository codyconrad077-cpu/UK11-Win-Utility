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
            using var sensors = new Sensors(); var reading = sensors.Read();
            if (reading.GpuLoad is < 0 or > 100 || reading.CpuTemperature is < -30 or > 150 || reading.GpuTemperature is < -30 or > 150) throw new Exception("Implausible sensor data");
            if (string.IsNullOrWhiteSpace(reading.Status)) throw new Exception("Sensor capability status missing");
            Console.WriteLine("PASS real sensor read or explicit unavailability: " + reading.Status);
            return new { scriptsParsed = actions.Length, invalidRequestsRejected = rejected, administratorBoundaries = actions.Count(Scripts.RequiresAdministrator), readOnly, cpuTemperatureAvailable = reading.CpuTemperature is not null, gpuTemperatureAvailable = reading.GpuTemperature is not null, mutationAcceptance = "App removal, DNS/service changes and installations were not performed on this runner. Test on a disposable Windows client before relying on recovery." };
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task PowerShell(string script, string sourcePath)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        info.Environment["APEX_TEST_SCRIPTS"] = sourcePath;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception("PowerShell workflow check: " + await stdout + " " + await stderr);
    }
}
