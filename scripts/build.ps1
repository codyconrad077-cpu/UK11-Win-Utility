param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $args" } }
Invoke-Dotnet restore UK11.Ultimate.sln
Invoke-Dotnet build UK11.Ultimate.sln -c Release --no-restore
Invoke-Dotnet run --project tests/UK11.Tests -c Release --no-build
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    Invoke-Dotnet run --project tests/UK11.Windows.Tests -c Release --no-build
}
if ($Publish) {
    Invoke-Dotnet publish src/UK11.App -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' -o artifacts/win-x64
    Get-FileHash artifacts/win-x64/UK11.Ultimate.exe -Algorithm SHA256 | Format-List
}
