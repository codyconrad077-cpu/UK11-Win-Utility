# UK11 ULTIMATE

A Windows 10/11 WPF control centre with a custom dark interface, live CPU/memory charts, hardware identity, searchable configuration library, favourites, previews, exact-value backups and recovery. Version 0.2 is a development preview, not a claim of measured gaming performance gains.

## Download

Self-contained, unsigned x64 executables are available under [GitHub Releases](https://github.com/codyconrad077-cpu/UK11-Win-Utility/releases). Download the EXE asset directly; it does not require a separate .NET installation. Do not disable Windows security to run unsigned software.

## Build and run

Install .NET 8 SDK (see `global.json`). On Windows:

```powershell
./scripts/build.ps1
# Launch the application:
dotnet run --project src/UK11.App
# Build and validate the self-contained portable EXE:
./scripts/build.ps1 -Publish
./artifacts/win-x64/UK11.Ultimate.exe
```

The publishing script runs the engine tests, isolated Windows integration tests, and WPF UI smoke checks. It generates actual rendered dashboard/catalogue screenshots and JSON reports in `artifacts/win-x64`. Test failures stop publishing. Tests never apply the real catalogue's desktop preferences or alter the active power plan.

On Linux:

```bash
dotnet build UK11.Ultimate.sln -c Release
dotnet run --project tests/UK11.Tests -c Release
```

These assertion runners must be invoked with `dotnet run`; `dotnet test` does not execute them. Linux can cross-compile WPF but cannot launch it or exercise native Windows APIs. Windows CI runs both test runners and the UI. Only Microsoft SDK/reference/runtime packs are needed; there are no third-party runtime packages. WPF trimming is disabled. Native components can extract to the user's temp directory during single-file startup.

## The catalogue

**45 fixed preferences**, each with a unique ID and registry target, cover appearance, Explorer, taskbar, suggestions, privacy and capture. These are existing Windows preference values, not 45 proven FPS optimisations. Missing values, unexpected types and incompatible builds are unavailable rather than created or guessed. Some legacy registry contracts may be ignored by later Windows versions or overridden by policy. The application distinguishes stored-value verification from actual user-visible effects and performance measurements.

**Power configuration** discovers Windows power parameters through documented PowrProf APIs. Each setting has separately stored plugged-in (AC) and battery (DC) targets, with OS-supplied descriptions and enumerated choices or min/max/increment validation. The count depends on OS, hardware and available schemes. AC/DC endpoints are counted explicitly, not advertised as distinct proven performance improvements. A desktop can store DC preferences without having a battery; they have no active battery-mode effect on such a machine.

Power values are advanced configuration choices. Increasing CPU responsiveness can increase power draw, heat, fan noise and battery drain. Sleep/battery-action changes can affect data loss. The app does not select an arbitrary "fastest" value or apply power controls in bulk. Choose a value, review its description and implications, and measure your own workload. Read-only and unknown-range parameters cannot be applied.

The library has category/state filters, search, persistent favourites, exact current/target details and technical implementation views. Only supported, unapplied fixed preferences can be selected for batches. A batch preflights every target, rejects stale previews and duplicate targets, verifies writes, and attempts reverse-order rollback after failure. Recovery failures or external conflicts are reported individually with originals retained.

## Backups and recovery

Keep `%LOCALAPPDATA%\UK11Ultimate` when upgrading:

- `settings.json`: page, search, category and favourites.
- `backups\<id>.json`: original DWORD, applied value, UTC timestamp and Prepared/Applied/Restored journal state.
- `backups\transactions.log`: operations and write/restore failures.
- `power-catalogue.json`: power target descriptors, including exact scheme, group, setting GUIDs and AC/DC side.

An original is flushed to disk before any mutation. Results are read back through the same Windows API/registry adapter. Interrupted Prepared operations remain recoverable. Reapplying does not replace unresolved originals. Once restored, a subsequent operation records a new original. An existing target value without a UK11 backup does not imply UK11 owns it and cannot be "restored" to a fabricated default.

Restoration preserves conflicting external values. If a value differs from both the original and UK11's applied value, inspect it and decide manually rather than forcing recovery. Corrupt metadata blocks the affected operation. Keep the power catalogue together with backup files so previous-plan targets remain identifiable after switching plans. The adapter writes the captured scheme and reapplies it only when observed active. Windows has no cross-process atomic plan-switch operation; other power tools can still race with it.

These are setting backups, not full-system backups or Windows System Restore points. The app does not flash firmware, install drivers, overclock, disable core security or execute hidden shell scripts. It requests no blanket elevation: current-user registry preferences need none, and power writes follow Windows policy/access checks. Only one application instance can access the per-user data folder at a time.

## Project layout

- `UK11.Core`: catalogue definitions, value/range rules, persistent transactions, conflict-aware restore, batch rollback and preferences.
- `UK11.Windows`: registry, PowrProf power discovery/read/write, CPU sampler and read-only hardware/memory APIs.
- `UK11.App`: custom WPF shell, dashboard, virtualized catalogue, detail inspector, favourites, review dialogs and recovery.
- `UK11.Tests`: portable fault-injection tests and catalogue integrity checks.
- `UK11.Windows.Tests`: temporary HKCU keys, native hardware checks, and write/verify/restore against an inactive duplicate power plan.

See [RELEASE-NOTES.md](RELEASE-NOTES.md), [PROGRESS.md](PROGRESS.md) and [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md) for scope and remaining acceptance work. Future work includes read-only SetupAPI driver inventory, network diagnostics and actual workload benchmarking. No invented tweak count or performance percentage is used.
