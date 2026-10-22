using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace UK11.Windows;

public sealed record WorkflowResult(bool Success, string? Error, JsonElement Data);

/// <summary>Fixed, reviewable Windows workflows. Request data is JSON, never interpolated as PowerShell code.</summary>
public sealed class WindowsWorkflows(string dataDirectory)
{
    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
    public async Task<WorkflowResult> RunAsync(string action, object? request = null, bool administrator = false)
    {
        var operation = Scripts.Get(action); // Throws before spawning for unknown actions.
        if (Scripts.RequiresAdministrator(action) && !administrator) throw new InvalidOperationException("This operation requires an approved administrator launch.");
        var jobs = Path.Combine(dataDirectory, "jobs"); Directory.CreateDirectory(jobs);
        var output = Path.Combine(jobs, Guid.NewGuid().ToString("N") + ".json");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request ?? new { })));
        var script = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';\n"
            + "$rootData=" + Quote(dataDirectory) + "; $output=" + Quote(output) + ";\n"
            + "$request=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')) | ConvertFrom-Json);\n"
            + Scripts.Common + "\ntry { $value = & {\n" + operation + "\n}; $answer=@{success=$true;error=$null;data=$value}; $code=0 } catch { $answer=@{success=$false;error=$_.Exception.Message;data=$null}; $code=1 };\n"
            + "$answer | ConvertTo-Json -Depth 16 -Compress | Set-Content -LiteralPath $output -Encoding UTF8; exit $code;";
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = administrator,
            CreateNoWindow = !administrator,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (administrator) info.Verb = "runas";
        info.Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(info) ?? throw new IOException("Windows could not start the workflow.");
        await process.WaitForExitAsync();
        if (!File.Exists(output)) throw new IOException("The Windows workflow ended without a result. Administrator access may have been declined.");
        var result = JsonSerializer.Deserialize<WorkflowResult>(await File.ReadAllTextAsync(output), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty workflow result.");
        if (process.ExitCode != 0 && result.Success) throw new IOException("Workflow exit code contradicted its result.");
        return result;
    }
}

public static class Scripts
{
    public static readonly string[] ServiceNames = ["SysMain", "WSearch", "MapsBroker", "Fax", "RetailDemo", "WMPNetworkSvc", "XblGameSave", "XboxNetApiSvc", "Spooler"];
    public static bool RequiresAdministrator(string id) => id is "restore-create" or "service-change" or "store-change" or "network-configure" or "network-restore" or "driver-install" or "driver-backup";
    public static string Get(string id) => id switch
    {
        "system" => SystemInventory,
        "restore-list" => RestoreList,
        "restore-create" => RestoreCreate,
        "service-list" => Services,
        "service-change" => ServiceChange,
        "store-change" => StoreChange,
        "apps-list" => Apps,
        "apps-remove" => AppsRemove,
        "apps-reinstall" => AppsReinstall,
        "network-list" => Network,
        "network-configure" => NetworkConfigure,
        "network-restore" => NetworkRestore,
        "driver-scan" => DriverScan,
        "driver-install" => DriverInstall,
        "driver-backup" => DriverBackup,
        "runtime-preview" => RuntimePreview,
        "runtime-install" => RuntimeInstall,
        _ => throw new ArgumentException("Unknown Windows operation: " + id)
    };
    public const string Common = """
    function Save-Atomic($path,$value) {
      [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
      $text = $value | ConvertTo-Json -Depth 12
      $temp=$path+'.tmp'; $bytes=[Text.Encoding]::UTF8.GetBytes($text)
      $stream=[IO.File]::Open($temp,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
      try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
      if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temp,$path,$null) } else { [IO.File]::Move($temp,$path) }
    }
    function Read-Backup($path) { if(Test-Path -LiteralPath $path){Get-Content -LiteralPath $path -Raw | ConvertFrom-Json} }
    function Log-Operation($text) { Add-Content -LiteralPath (Join-Path $rootData 'operations.log') -Value ((Get-Date).ToUniversalTime().ToString('o')+' '+$text) }
    $serviceNames=@('SysMain','WSearch','MapsBroker','Fax','RetailDemo','WMPNetworkSvc','XblGameSave','XboxNetApiSvc','Spooler')
    $autoApps=@('Clipchamp.Clipchamp','Microsoft.MicrosoftSolitaireCollection','Microsoft.BingNews','Microsoft.BingWeather','Microsoft.MicrosoftOfficeHub','Microsoft.SkypeApp','Microsoft.Microsoft3DViewer','Microsoft.People')
    $runtimeIds=@('Microsoft.VCRedist.2015+.x64','Microsoft.VCRedist.2015+.x86','Microsoft.DotNet.DesktopRuntime.8','Microsoft.DotNet.DesktopRuntime.10','Microsoft.DirectX')
    function Get-RemovableApps {
      @(Get-AppxPackage | Where-Object { -not $_.NonRemovable -and -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.Name -notmatch '^(Microsoft\.(WindowsStore|StorePurchaseApp|DesktopAppInstaller|Windows\.(StartMenuExperienceHost|ShellExperienceHost|SecHealthUI))|MicrosoftWindows\.(Client|CrossDevice)|Microsoft\.WindowsAppRuntime)' })
    }
    function Driver-Updates {
      $session=New-Object -ComObject Microsoft.Update.Session
      $session.CreateUpdateSearcher().Search("IsInstalled=0 and Type='Driver' and IsHidden=0").Updates
    }
    function Store-Policy {
      $edition=[string](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
      $key=Get-Item 'HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore' -ErrorAction SilentlyContinue
      $present=$key -and $key.GetValueNames() -contains 'RemoveWindowsStore'
      $typeOK=-not $present -or $key.GetValueKind('RemoveWindowsStore') -eq [Microsoft.Win32.RegistryValueKind]::DWord
      $supported=$edition -match '^(Enterprise|Education)' -and $typeOK
      $value=0; if($present -and $typeOK){$value=[int]$key.GetValue('RemoveWindowsStore')}
      [pscustomobject]@{supported=$supported;present=[bool]$present;value=$value;edition=$edition}
    }
    """;
    private const string SystemInventory = """
    $bios=Get-CimInstance Win32_BIOS; $board=Get-CimInstance Win32_BaseBoard
    $secure='Unavailable'; try { $secure=[string](Confirm-SecureBootUEFI -ErrorAction Stop) } catch {}
    [pscustomobject]@{cpu=(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name) -join ', ';gpu=@(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID);board=($board.Manufacturer+' '+$board.Product);revision=$board.Version;biosVendor=$bios.Manufacturer;biosVersion=$bios.SMBIOSBIOSVersion;biosDate=[string]$bios.ReleaseDate;secureBoot=$secure;scewin='No verified OEM-specific write profile is installed. Firmware access remains read-only.'}
    """;
    private const string RestoreList = """
    try { $points=@(Get-CimInstance -Namespace root/default -ClassName SystemRestore -ErrorAction Stop | Select-Object SequenceNumber,Description,CreationTime); [pscustomobject]@{available=$true;points=$points;message='Windows restore points. Opening recovery lets you review before restoring.'} }
    catch { [pscustomobject]@{available=$false;points=@();message=('System Restore is unavailable or disabled: '+$_.Exception.Message)} }
    """;
    private const string RestoreCreate = """
    $before=@(Get-CimInstance -Namespace root/default -ClassName SystemRestore | Select-Object -ExpandProperty SequenceNumber)
    Checkpoint-Computer -Description 'Apex Tweaks - before changes' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
    $new=@(Get-CimInstance -Namespace root/default -ClassName SystemRestore | Where-Object { $_.SequenceNumber -notin $before })
    if($new.Count -eq 0){throw 'Windows did not create a new restore point. Check System Protection, free space and the Windows restore-point frequency limit.'}
    Log-Operation 'System restore point created and verified'
    $new | Select-Object SequenceNumber,Description,CreationTime
    """;
    private const string Services = """
    @($serviceNames | ForEach-Object {
      $name=$_; $s=Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
      if($s){$backup=Read-Backup (Join-Path $rootData ('services\'+$name+'.json')); [pscustomobject]@{name=$name;title=$s.DisplayName;mode=$s.StartMode;status=$s.State;owned=($null -ne $backup -and $backup.state -ne 'Restored');original=$backup.originalMode}}
    })
    $store=Store-Policy; $backup=Read-Backup (Join-Path $rootData 'services\MicrosoftStore.json')
    [pscustomobject]@{name='MicrosoftStore';title='Microsoft Store (Enterprise/Education)';mode=$(if($store.value -eq 1){'Disabled'}else{'Enabled'});status=$store.edition;supported=$store.supported;owned=($null -ne $backup -and $backup.state -ne 'Restored')}
    """;
    private const string StoreChange = """
    if($request.name -cne 'MicrosoftStore'){throw 'Unexpected policy target.'}
    $policy=Store-Policy; if(-not $policy.supported){throw 'This Store policy is supported only on Enterprise/Education with a compatible registry value.'}
    $path=Join-Path $rootData 'services\MicrosoftStore.json'; $b=Read-Backup $path
    $key='HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore'
    if($request.disable -eq $true){
      if($policy.value -eq 1){return [pscustomobject]@{message='Store is already blocked. No original was invented.'}}
      if($b -and $b.state -ne 'Restored'){throw 'Restore the existing Store-policy original first.'}
      $b=[pscustomobject]@{name='MicrosoftStore';originalPresent=$policy.present;originalValue=$policy.value;state='Prepared'}
      Save-Atomic $path $b; Log-Operation 'Store policy prepare'
      New-Item -Path $key -Force | Out-Null
      New-ItemProperty -LiteralPath $key -Name RemoveWindowsStore -Value 1 -PropertyType DWord -Force | Out-Null
      if((Store-Policy).value -ne 1){throw 'Store policy did not verify; original retained.'}
      $b.state='Applied'; Save-Atomic $path $b
    } else {
      if(-not $b -or $b.name -cne 'MicrosoftStore'){throw 'No Store-policy original exists.'}
      if($policy.value -ne 1 -and ($policy.present -ne $b.originalPresent -or $policy.value -ne $b.originalValue)){throw 'An external Store policy conflicts with restore.'}
      if($b.originalPresent){New-ItemProperty -LiteralPath $key -Name RemoveWindowsStore -Value ([int]$b.originalValue) -PropertyType DWord -Force | Out-Null}
      else {Remove-ItemProperty -LiteralPath $key -Name RemoveWindowsStore -ErrorAction SilentlyContinue}
      $actual=Store-Policy
      if($actual.present -ne $b.originalPresent -or ($b.originalPresent -and $actual.value -ne $b.originalValue)){throw 'Store policy restore did not verify.'}
      $b.state='Restored'; Save-Atomic $path $b
    }
    Log-Operation 'Store policy verified; Store infrastructure preserved'
    [pscustomobject]@{message='Store access policy verified. Sign out/restart if policy has not refreshed.'}
    """;
    private const string ServiceChange = """
    $name=[string]$request.name
    if($name -cnotin $serviceNames){throw 'This service is outside the reviewed allowlist.'}
    $path=Join-Path $rootData ('services\'+$name+'.json'); $backup=Read-Backup $path
    $s=Get-CimInstance Win32_Service -Filter "Name='$name'"
    if(-not $s){throw 'Service is not installed.'}
    if($request.disable -eq $true){
      if($s.StartMode -eq 'Disabled'){return [pscustomobject]@{message='Already disabled; no original has been invented.'}}
      if($backup -and $backup.state -ne 'Restored'){throw 'An unresolved service original exists. Restore it first.'}
      if(@((Get-Service -Name $name).DependentServices | Where-Object Status -eq Running).Count -gt 0){throw 'A running service depends on this service; disabling was refused.'}
      $key='HKLM:\SYSTEM\CurrentControlSet\Services\'+$name
      $delayed=Get-ItemProperty -LiteralPath $key -Name DelayedAutoStart -ErrorAction SilentlyContinue
      $backup=[pscustomobject]@{name=$name;originalMode=$s.StartMode;wasRunning=($s.State -eq 'Running');delayedPresent=($null -ne $delayed);delayedValue=$delayed.DelayedAutoStart;state='Prepared'}
      Save-Atomic $path $backup; Log-Operation ('Service prepare disable '+$name)
      Set-Service -Name $name -StartupType Disabled
      if($s.State -eq 'Running'){Stop-Service -Name $name -ErrorAction Stop}
      $actual=Get-CimInstance Win32_Service -Filter "Name='$name'"
      if($actual.StartMode -ne 'Disabled' -or $actual.State -ne 'Stopped'){throw 'Service change did not verify; original retained.'}
      $backup.state='Applied'; Save-Atomic $path $backup; Log-Operation ('Service disable verified '+$name)
    } else {
      if(-not $backup -or $backup.name -cne $name -or $backup.originalMode -notin @('Auto','Manual','Disabled')){throw 'No valid service original exists.'}
      if($s.StartMode -ne 'Disabled' -and $s.StartMode -ne $backup.originalMode){throw 'An external startup-mode change conflicts with restore.'}
      $mode=@{Auto='Automatic';Manual='Manual';Disabled='Disabled'}[$backup.originalMode]
      Set-Service -Name $name -StartupType $mode
      $key='HKLM:\SYSTEM\CurrentControlSet\Services\'+$name
      if($backup.delayedPresent){Set-ItemProperty -LiteralPath $key -Name DelayedAutoStart -Value ([int]$backup.delayedValue)} else {Remove-ItemProperty -LiteralPath $key -Name DelayedAutoStart -ErrorAction SilentlyContinue}
      if($backup.wasRunning){Start-Service -Name $name}
      elseif((Get-Service -Name $name).Status -eq 'Running'){
        if(@((Get-Service -Name $name).DependentServices | Where-Object Status -eq Running).Count -gt 0){throw 'Running dependents prevent restoring the original stopped state; backup retained.'}
        Stop-Service -Name $name -ErrorAction Stop
      }
      $actual=Get-CimInstance Win32_Service -Filter "Name='$name'"
      $delayed=Get-ItemProperty -LiteralPath $key -Name DelayedAutoStart -ErrorAction SilentlyContinue
      if($actual.StartMode -ne $backup.originalMode -or ($backup.wasRunning -and $actual.State -ne 'Running') -or (-not $backup.wasRunning -and $actual.State -ne 'Stopped') -or ($backup.delayedPresent -and ($null -eq $delayed -or $delayed.DelayedAutoStart -ne $backup.delayedValue)) -or (-not $backup.delayedPresent -and $null -ne $delayed)){throw 'Service restoration did not verify; backup retained.'}
      $backup.state='Restored'; Save-Atomic $path $backup; Log-Operation ('Service restore verified '+$name)
    }
    [pscustomobject]@{message='Service state verified.';name=$name}
    """;
    private const string Apps = """
    @(Get-RemovableApps | ForEach-Object { [pscustomobject]@{name=$_.Name;fullName=$_.PackageFullName;publisher=$_.Publisher;microsoft=($_.Publisher -like '*Microsoft*');auto=($_.Name -cin $autoApps)} })
    """;
    private const string AppsRemove = """
    $requested=@($request.packages); $apps=@(Get-RemovableApps | Where-Object { $_.PackageFullName -cin $requested })
    if($apps.Count -ne $requested.Count){throw 'The package list changed or contains protected/unavailable packages. Rescan first.'}
    if($request.automatic -eq $true -and @($apps | Where-Object { $_.Name -cnotin $autoApps }).Count -gt 0){throw 'Auto debloat is restricted to the reviewed optional-app list.'}
    $removed=@(); $failed=@()
    foreach($p in $apps){
      try {
        if($p.Name -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,190}$'){throw 'Unexpected package identifier.'}
        Save-Atomic (Join-Path $rootData ('apps\'+$p.Name+'.json')) ([pscustomobject]@{name=$p.Name;fullName=$p.PackageFullName;publisher=$p.Publisher;manifest=(Join-Path $p.InstallLocation 'AppxManifest.xml');note='Inventory backup. Reinstall requires retained files or the original store source.'})
        Log-Operation ('Remove package prepare '+$p.PackageFullName)
        Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop
        if(@(Get-AppxPackage | Where-Object PackageFullName -ceq $p.PackageFullName).Count -ne 0){throw 'Package is still installed.'}
        $removed += $p.Name; Log-Operation ('Remove package verified '+$p.Name)
      } catch { $failed += ($p.Name+': '+$_.Exception.Message) }
    }
    [pscustomobject]@{removed=$removed;failed=$failed;message='Package removal checked. Inventory backups are not a guaranteed reinstall source.'}
    """;
    private const string AppsReinstall = """
    $name=[string]$request.name
    if($name -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]{0,190}$'){throw 'Unexpected package identifier.'}
    $b=Read-Backup (Join-Path $rootData ('apps\'+$name+'.json'))
    if(-not $b -or $b.name -cne $name -or -not (Test-Path -LiteralPath $b.manifest)){throw 'Original package files are no longer present. Reinstall from Microsoft Store or the original publisher.'}
    Add-AppxPackage -Register $b.manifest -DisableDevelopmentMode -ErrorAction Stop
    if(@(Get-AppxPackage -Name $name).Count -eq 0){throw 'Package registration did not verify.'}
    Log-Operation ('Package registration restored '+$name)
    [pscustomobject]@{message='Original package registration verified.'}
    """;
    private const string Network = """
    @(Get-NetAdapter -Physical | ForEach-Object { $adapter=$_; $dns=Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4; [pscustomobject]@{index=$adapter.ifIndex;name=$adapter.Name;description=$adapter.InterfaceDescription;status=[string]$adapter.Status;speed=[string]$adapter.LinkSpeed;dns=@($dns.ServerAddresses)} })
    """;
    private const string NetworkConfigure = """
    $index=[int]$request.index
    $adapter=Get-NetAdapter -Physical | Where-Object ifIndex -eq $index
    if(-not $adapter -or $adapter.Status -ne 'Up'){throw 'Choose an active physical network adapter.'}
    $adapterGuid=[guid]$adapter.InterfaceGuid; $key='HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\'+$adapterGuid.ToString('B')
    $dns=Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4
    $path=Join-Path $rootData ('network\'+([guid]$adapter.InterfaceGuid).ToString('N')+'.json'); $existing=Read-Backup $path
    if($existing -and $existing.state -ne 'Restored'){throw 'A DNS original already exists. Restore it before applying again.'}
    $manual=[string](Get-ItemProperty -LiteralPath $key).NameServer
    $backup=[pscustomobject]@{adapter=([guid]$adapter.InterfaceGuid).ToString();original=@($dns.ServerAddresses);automatic=[string]::IsNullOrWhiteSpace($manual);applied=@('1.1.1.1','1.0.0.1');state='Prepared'}
    Save-Atomic $path $backup; Log-Operation ('DNS prepare '+$adapter.Name)
    try {
      Set-DnsClientServerAddress -InputObject $dns -ServerAddresses $backup.applied
      $actual=@((Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4).ServerAddresses)
      if(($actual -join ',') -ne ($backup.applied -join ',')){throw 'DNS values did not verify.'}
      Clear-DnsClientCache
      Resolve-DnsName www.microsoft.com -DnsOnly -QuickTimeout -ErrorAction Stop | Out-Null
      $backup.state='Applied'; Save-Atomic $path $backup; Log-Operation ('DNS verified '+$adapter.Name)
    } catch {
      $failure=$_.Exception.Message
      if($backup.automatic){Set-DnsClientServerAddress -InputObject $dns -ResetServerAddresses} else {Set-DnsClientServerAddress -InputObject $dns -ServerAddresses $backup.original}
      $restored=@((Get-DnsClientServerAddress -InterfaceIndex $index -AddressFamily IPv4).ServerAddresses)
      $manualNow=[string](Get-ItemProperty -LiteralPath $key).NameServer
      if(($backup.automatic -and -not [string]::IsNullOrWhiteSpace($manualNow)) -or (-not $backup.automatic -and ($restored -join ',') -ne (@($backup.original) -join ','))){throw ('DNS setup failed and rollback needs attention. Original retained. '+$failure)}
      $backup.state='Restored'; Save-Atomic $path $backup; Log-Operation 'DNS setup failed; original DNS mode restored and verified'
      throw ('DNS setup failed; original DNS mode restored and verified. '+$failure)
    }
    [pscustomobject]@{message='IPv4 DNS configured and name resolution verified. DNS changes do not guarantee lower game ping.'}
    """;
    private const string NetworkRestore = """
    $adapter=Get-NetAdapter -Physical | Where-Object ifIndex -eq ([int]$request.index)
    if(-not $adapter){throw 'Adapter no longer exists.'}
    $path=Join-Path $rootData ('network\'+([guid]$adapter.InterfaceGuid).ToString('N')+'.json'); $b=Read-Backup $path
    if(-not $b -or $b.adapter -ne ([guid]$adapter.InterfaceGuid).ToString()){throw 'No DNS original exists for this adapter.'}
    $dns=Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4
    if((@($dns.ServerAddresses) -join ',') -ne (@($b.applied) -join ',') -and (@($dns.ServerAddresses) -join ',') -ne (@($b.original) -join ',')){throw 'External DNS configuration conflicts with restore.'}
    if($b.automatic){Set-DnsClientServerAddress -InputObject $dns -ResetServerAddresses} else {Set-DnsClientServerAddress -InputObject $dns -ServerAddresses $b.original}
    $actual=@((Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4).ServerAddresses)
    if(-not $b.automatic -and ($actual -join ',') -ne (@($b.original) -join ',')){throw 'DNS restoration did not verify.'}
    $manual=[string](Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\'+([guid]$adapter.InterfaceGuid).ToString('B'))).NameServer
    if($b.automatic -and -not [string]::IsNullOrWhiteSpace($manual)){throw 'Automatic DNS restoration did not verify.'}
    $b.state='Restored'; Save-Atomic $path $b; Clear-DnsClientCache; Log-Operation ('DNS restored '+$adapter.Name)
    [pscustomobject]@{message='Original IPv4 DNS mode verified.'}
    """;
    private const string DriverScan = """
    $updates=Driver-Updates
    @($updates | ForEach-Object { $u=$_; [pscustomobject]@{id=$u.Identity.UpdateID;revision=$u.Identity.RevisionNumber;title=$u.Title;manufacturer=$u.DriverManufacturer;model=$u.DriverModel;hardwareId=$u.DriverHardwareID;eula=$u.EulaText;eulaAccepted=$u.EulaAccepted} })
    """;
    private const string DriverBackup = """
    $destination=Join-Path $rootData ('driver-backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & "$env:SystemRoot\System32\pnputil.exe" /export-driver '*' $destination | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Windows driver export failed. No installation was started.'}
    Log-Operation ('Driver export '+$destination)
    [pscustomobject]@{message='Third-party driver packages exported. Inbox drivers are supplied by Windows.';path=$destination;files=@(Get-ChildItem $destination -Recurse -Filter *.inf).Count}
    """;
    private static string DriverInstall => """
    if($request.acceptTerms -ne $true){throw 'Review and accept the selected driver license terms first.'}
    if($request.vendor -cnotin @('NVIDIA','AMD')){throw 'Unsupported GPU update group.'}
    $ids=@($request.ids); if($ids.Count -eq 0){throw 'No driver updates selected.'}
    $available=Driver-Updates; $selected=New-Object -ComObject Microsoft.Update.UpdateColl
    foreach($u in $available){
      if($u.Identity.UpdateID -cin $ids){
        if(($u.DriverManufacturer+' '+$u.Title) -notmatch $(if($request.vendor -eq 'NVIDIA'){'NVIDIA'}else{'AMD|Advanced Micro Devices'})){throw 'Update vendor does not match the confirmed group.'}
        if(-not $u.EulaAccepted){$u.AcceptEula()}
        [void]$selected.Add($u)
      }
    }
    if($selected.Count -ne $ids.Count){throw 'The applicable driver update list changed. Search and review again.'}
    """ + "\n$archive = & {\n" + DriverBackup + "\n};\n" + """
    $restoreWarning=''; try {Checkpoint-Computer -Description 'Apex Tweaks - before GPU driver update' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop} catch {$restoreWarning=$_.Exception.Message}
    $session=New-Object -ComObject Microsoft.Update.Session; $downloader=$session.CreateUpdateDownloader(); $downloader.Updates=$selected
    $download=$downloader.Download()
    for($i=0;$i -lt $selected.Count;$i++){if(-not $selected.Item($i).IsDownloaded -or $download.GetUpdateResult($i).ResultCode -ne 2){throw 'A driver download did not verify successfully. Installation was not started.'}}
    $installer=$session.CreateUpdateInstaller(); $installer.Updates=$selected; $installer.ForceQuiet=$true
    Log-Operation ('Installing '+$selected.Count+' Windows Update GPU driver package(s)')
    $installed=$installer.Install(); $results=@()
    for($i=0;$i -lt $selected.Count;$i++){$r=$installed.GetUpdateResult($i); $results += [pscustomobject]@{title=$selected.Item($i).Title;resultCode=[int]$r.ResultCode;hresult=$r.HResult}}
    $failed=@($results | Where-Object resultCode -ne 2)
    Log-Operation ('Driver installation result '+$installed.ResultCode)
    [pscustomobject]@{message='Windows Update installation results';results=$results;failed=$failed;rebootRequired=$installed.RebootRequired;restoreWarning=$restoreWarning;driverArchive=$archive.path}
    """;
    private const string RuntimePreview = """
    if(-not (Get-Command winget.exe -ErrorAction SilentlyContinue)){throw 'Microsoft App Installer (winget) is not available. Install it from Microsoft Store first.'}
    $details=@()
    foreach($id in $runtimeIds){$text=& winget.exe show --id $id --exact --source winget --accept-source-agreements --disable-interactivity 2>&1; if($LASTEXITCODE -ne 0){throw ('Cannot verify runtime package '+$id)}; $details += ($id+"`n"+($text -join "`n"))}
    [pscustomobject]@{packages=$runtimeIds;details=($details -join "`n`n");message='Review publishers, licenses and package details before installation. Package hashes and installer signatures remain enforced by winget.'}
    """;
    private const string RuntimeInstall = """
    if($request.acceptTerms -ne $true){throw 'Package terms must be reviewed and accepted first.'}
    if(-not (Get-Command winget.exe -ErrorAction SilentlyContinue)){throw 'Microsoft App Installer (winget) is not available.'}
    $results=@()
    foreach($id in $runtimeIds){
      $text=& winget.exe install --id $id --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity 2>&1
      $exit=$LASTEXITCODE
      # winget uses 0x8A15002B for no applicable upgrade; verify actual installed inventory independently.
      $installed=& winget.exe list --id $id --exact --source winget --accept-source-agreements --disable-interactivity 2>&1
      $present=$LASTEXITCODE -eq 0 -and ($installed -join ' ') -like ('*'+$id+'*')
      $results += [pscustomobject]@{id=$id;exitCode=$exit;verified=$present;log=($text -join "`n")}
      Log-Operation ('Runtime '+$id+' exit '+$exit+' verified '+$present)
    }
    [pscustomobject]@{message='Recommended runtime installation checked against winget inventory. This is a defined package set, not every historical game runtime.';results=$results;failed=@($results | Where-Object verified -ne $true)}
    """;
}
