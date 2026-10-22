# Apex Tweaks

**made by riley** · A blue-accent Windows 10/11 x64 utility with a compact sidebar, live dashboard and simple tweak-name/on-off rows. This is an unsigned development preview. Configuration verification does not establish gaming performance gains.

Download [**Apex.Tweaks.exe — v0.3.0-preview.4**](https://github.com/codyconrad077-cpu/UK11-Win-Utility/releases/download/v0.3.0-preview.4/Apex.Tweaks.exe) from [GitHub Releases](https://github.com/codyconrad077-cpu/UK11-Win-Utility/releases). It is one self-contained executable: no ZIP extraction or separate .NET installation. Compatible sensor drivers, Windows components and internet-based installation services remain external prerequisites.

## Pages

| Page | Implemented behavior |
| --- | --- |
| Home | Actual CPU/GPU/RAM usage, CPU/GPU temperature selector and history, hardware identity, active power plan and disk usage. Unsupported sensors show unavailable. |
| System Restore | Enumerate Windows restore points, create and verify a new point through Windows, open System Protection/recovery, restore Apex's saved tweak originals. Windows can refuse creation when protection is disabled or its frequency limit applies. |
| Drivers | NVIDIA/AMD compatible update scanning and installation through Windows Update; export installed third-party drivers; a defined Microsoft VC++, .NET desktop and DirectX runtime set through winget. Package/driver terms are shown before installation. |
| General | 45 current-user Windows preferences, each with one toggle. Missing values, wrong types, unsupported builds and already-target values without an Apex original remain read-only. |
| Debloating | Preview/remove optional current-user Appx packages; Microsoft/other package tabs. Auto debloat uses an eight-name optional-app allowlist. Protected Store, App Installer, framework and shell packages are excluded. Desktop applications use Windows Installed Apps. |
| Networking | Select an active physical adapter; configure Cloudflare IPv4 DNS, clear cache and verify resolution; restore original static/DHCP DNS mode; run an actual ICMP latency/loss test. No unmeasured MTU/TCP presets or guaranteed ping improvements. |
| Advanced | Up to 120 AC/DC endpoints for 60 identified power parameters. Targets are Windows-reported High Performance defaults validated against the OS's actual enumerations/ranges. Hardware/build availability varies. |
| Services | Nine allowlisted optional services with saved startup/delayed-start/running state and verified restore. Store access policy is available only on supported Enterprise/Education editions; Store infrastructure is preserved. Critical security, updating and driver services are excluded. |
| BIOS / SCEWIN | Read-only motherboard/revision/BIOS/Secure Boot information, report export and OEM support. No generic firmware writes. |

**Apply all tweaks** applies compatible unapplied targets in the current General section or Advanced AC/DC view after a preview. **Revert all tweaks** restores saved General/Advanced originals, including retained prior-plan descriptors. Services, DNS, drivers and app removal have separate recovery workflows; they are not silently combined into this button.

Advanced values can increase heat, noise, power draw or battery drain. AC/DC are independent stored values, not 120 independent measured FPS improvements. A desktop without a battery can store DC values without using them. The requested “300+ proven premium optimisations” claim is not supported and is not advertised.

DDU remains a guided manual workflow linked to its official source. SCEWIN automation requires an exact motherboard/revision, OEM tool version, validated setting mappings and recovery procedure; an AMI BIOS or present executable alone cannot establish compatibility. Apex does not install sensor drivers, bypass Windows protections, flash firmware or automatically reboot.

## Recovery and permissions

Keep `%LOCALAPPDATA%\UK11Ultimate` when upgrading from earlier UK11 releases. The legacy data directory deliberately preserves existing originals. It contains `backups`, `power-catalogue.json`, `services`, `network`, `apps`, `driver-backups`, per-operation `jobs` results and logs. Do not delete it before restoring changes you intend to undo.

General/Advanced originals are flushed before writes, verified after mutation and restored exactly. Batches preflight every target, reject stale previews and attempt reverse-order rollback after a failure. Conflicting external values are preserved with originals retained. Power operations target the captured scheme and reapply it only if observed active. Other power utilities can race with Windows plan switching.

Service/Store-policy and DNS workflows journal originals before changing them. Pending service changes remain recoverable through their toggle; DNS failures attempt and verify rollback. Windows UAC is requested only for administrator operations. Cancelling it does not launch the workflow. App removal records inventory before removal, but removed package files may be gone: reinstall requires retained files or the original publisher/Store. Driver export is not a full system image or guaranteed rollback; use Device Manager or Windows recovery when appropriate.

## Build and validation

Install .NET 8 SDK (see `global.json`). On Windows:

```powershell
./scripts/build.ps1
# Run from source:
dotnet run --project src/UK11.App
# Validate and publish a standalone executable:
./scripts/build.ps1 -Publish
./artifacts/win-x64/Apex.Tweaks.exe
```

On Linux, cross-compile and run the portable engine assertions:

```bash
dotnet build UK11.Ultimate.sln -c Release
dotnet run --project tests/UK11.Tests -c Release
```

These are executable assertion runners; `dotnet test` does not execute them. Linux cannot launch WPF or test Windows APIs. Windows CI runs portable assertions, isolated HKCU adapter checks, inactive duplicate-plan write/verify/restore, PowerShell 5.1 parser/rejection/read-only checks, sensor capability reporting, and WPF nine-page navigation/search/rendering. It saves actual screenshots and native/UI JSON reports. Mutations such as real app removal, service/DNS changes and driver/runtime installation need disposable-client acceptance; see [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md).

The app uses unmodified LibreHardwareMonitorLib 0.9.6 and System.Management with locked dependency hashes. Licenses/source notices are embedded under About & licenses and included in `licenses/`. Sensor availability depends on hardware, privileges and compatible installed driver support. Missing readings are never estimated. Zero, non-finite and out-of-range temperature values are treated as unavailable; subzero cooling is outside the supported monitor range. Single-file publishing disables trimming and can extract native components to the user's temporary directory.

Projects retain their UK11 namespaces: Core is the transactional engine, Windows contains native adapters/workflows/sensors, App contains WPF, and the two executable test projects separate portable and Windows checks. See [PROGRESS.md](PROGRESS.md) and [RELEASE-NOTES.md](RELEASE-NOTES.md).
