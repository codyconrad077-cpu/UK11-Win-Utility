# UK11 ULTIMATE

Milestone 01: a Windows 10/11 WPF desktop utility with a dark electric-blue interface, hardware overview, live memory use, searchable catalogue, persistent preferences, per-setting previews, backups, verification and restoration. This is an early development milestone, not the complete premium product or a claim of performance gains.

## Build and run

Install the .NET 8 SDK (the version is pinned in `global.json`). On Windows:

```powershell
./scripts/build.ps1
dotnet run --project src/UK11.App
# Build unsigned, self-contained x64 portable release:
./scripts/build.ps1 -Publish
./artifacts/win-x64/UK11.Ultimate.exe
```

The app runs as the current user. None of these three settings needs elevation. Build scripts propagate failed command exit codes. No third-party runtime dependencies are used; NuGet supplies Microsoft's Windows Desktop reference/runtime packs. Single-file deployment can extract native components to the user temp directory. Do not enable trimming for WPF.

On Linux, `dotnet build UK11.Ultimate.sln -c Release` cross-compiles the Windows target, and `dotnet run --project tests/UK11.Tests -c Release` runs the core assertion suite. Linux cannot run WPF, verify desktop effects, or exercise native hardware APIs. The tests are executable assertion runners: use `dotnet run`, not `dotnet test` (which would not execute them).

## Implemented actions

All changes are explicit DWORD writes under **HKEY_CURRENT_USER**, supported on Windows 10/11 only when the existing value is a DWORD. Missing values or unexpected types are marked unavailable rather than guessed. External policies or Windows releases may override the UI effect even after the stored value verifies.

| ID | Exact value set to 0 | Effect / trade-off | Refresh |
|---|---|---|---|
| visual.transparency | `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency` | Opaque desktop surfaces; visual preference, no guaranteed FPS benefit | May require sign out |
| visual.taskbar-animation | `Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarAnimations` | Reduced taskbar transitions; visual preference | Sign out |
| capture.app-capture | `Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled` | Disable app capture; recording functionality may be lost, other capture policies may override | Reopen games / Game Bar |

These registry settings are deliberately narrow. They are not a universal optimisation profile. Test the actual user-visible effect on your Windows version before treating a preference as supported for release. No FPS improvement has been measured or promised.

## Backups and recovery

Data lives in `%LOCALAPPDATA%\UK11Ultimate`: `settings.json`, `backups\<id>.json`, and `backups\transactions.log`. Keep this directory when updating. Do not delete it while any applied setting needs restoration. Each action saves and flushes the original value before writing, then reads back the result. A crash can leave a `Prepared` backup; it remains available for restore. Re-applying does not overwrite an unresolved original. Restoring reads back the original and marks the backup restored. A new application after restoration records a new original.

The UI shows actual registry detection independently from operation history. "Target value detected" does not mean UK11 originally applied it. Restore is unavailable without a backup. If another program changes the setting to a third value, restore refuses to overwrite it. Compare the current DWORD with the backup and decide manually in Registry Editor; do not delete a conflict just to force an operation. A failed operation retains its original backup and reports its exception. Corrupt or unreadable backup files block application of the affected setting. One app instance per user data directory prevents simultaneous UK11 writes; third-party programs can still race with registry operations.

Backups cover only the listed values, not the entire system. This milestone does not create Windows System Restore points, reboot automatically, modify firmware, install drivers, disable security, or run hidden scripts. There is no updater or signing certificate. Logs contain setting identifiers, timestamps, results and error messages, not hardware serial numbers or credentials.

## Architecture and next stages

- `UK11.Core`: typed definitions, compatibility checks, transaction backups, verification, conflict detection and preferences; portable and dependency-free.
- `UK11.Windows`: registry adapter and read-only CPU, baseboard, BIOS and physical-memory detection through Windows APIs.
- `UK11.App`: WPF UI, asynchronous actions, memory sampling and recovery manager.
- `UK11.Tests`: portable fault-injection tests against an in-memory settings adapter.
- `UK11.Windows.Tests`: isolated HKCU test keys and hardware API checks, without altering actual desktop settings.

See [PROGRESS.md](PROGRESS.md) for evidence and [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md) before a release. Future stages should add read-only SetupAPI driver inventory and network diagnostics, then measured benchmarking. Driver installation, firmware writes and experimental tuning require separate compatibility validation, recovery design and explicit confirmation. Do not add nonfunctional navigation or invented tweaks to meet a count.
