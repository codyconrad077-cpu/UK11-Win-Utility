param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $args" } }
Invoke-Dotnet restore UK11.Ultimate.sln
Invoke-Dotnet build UK11.Ultimate.sln -c Release --no-restore
Invoke-Dotnet run --project tests/UK11.Tests -c Release --no-build
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    Invoke-Dotnet run --project tests/UK11.Windows.Tests -c Release --no-build -- artifacts/win-x64/native-report.json
}
if ($Publish) {
    Invoke-Dotnet publish src/UK11.App -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' -o artifacts/win-x64
    Get-FileHash artifacts/win-x64/Apex.Tweaks.exe -Algorithm SHA256 | Format-List
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $preview = Join-Path (Get-Location) 'artifacts/win-x64/ui-preview.png'
        $exe = Join-Path (Get-Location) 'artifacts/win-x64/Apex.Tweaks.exe'
        $process = Start-Process -FilePath $exe -ArgumentList "--ui-smoke `"$preview`"" -PassThru
        if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'UI smoke test timed out' }
        if ($process.ExitCode -ne 0) {
            if (Test-Path "$preview.error.txt") { Get-Content "$preview.error.txt" | Write-Host }
            throw "UI smoke test failed with code $($process.ExitCode)"
        }
        if (-not (Test-Path $preview)) { throw 'UI smoke test did not produce a preview' }
        Write-Host 'PASS Apex WPF startup, nine-page navigation, toggle filtering and rendered previews'
        Get-Content "$preview.json" | Write-Host
    }
}
