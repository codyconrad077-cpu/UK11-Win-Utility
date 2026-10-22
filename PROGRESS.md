# Delivery progress — 0.2 control centre preview

## Implemented

- [x] Custom WPF window shell, vector mark, consistent dark palette, rounded controls, type hierarchy, dashboard and catalogue/recovery workspaces.
- [x] Actual system-wide CPU and physical-memory readings with 60-sample graphs; CPU/board/BIOS/OS/thread inventory and active power-plan name.
- [x] 45 distinct reversible Windows current-user preferences, compared with three in 0.1.
- [x] Runtime PowrProf catalogue: subgroups, setting descriptions, independent AC/DC values, real enumerated options and numeric bounds/increments. Counts depend on the computer.
- [x] Virtualized searchable library, category/state filters, persistent favourites and detail inspector with current/target values and technical implementation.
- [x] Individual and batch confirmation previews, exact original backups, read-back verification and stale-preview rejection.
- [x] Batch preflight and reverse rollback on failure, including operations that fail after mutation.
- [x] Individual/full restore previews with external-conflict preservation, retained originals, recovery states and visible logs.
- [x] Power backups capture scheme/group/setting/AC-or-DC identity; unresolved previous-plan descriptors survive rescan.
- [x] Single-EXE self-contained publishing and version-tag GitHub Releases delivery.

## Current validation

- [x] Linux Release cross-compilation: zero warnings/errors.
- [x] 154 portable tests pass: per-definition apply/undo and missing-value behavior, idempotence, faults, conflicts, persisted originals, preferences, corrupted metadata, bounds/choices, batch failure rollback, stale previews, duplicate targets and catalogue integrity.
- [ ] Windows 0.2 native validation and rendered UI smoke run: in progress. The native test runner now checks nonzero targets correctly instead of assuming all preferences write zero.
- [ ] Inspect the actual dashboard/catalogue screenshots from the Windows UI run before final delivery.
- [ ] Record discovered targets, native write/restore checks and read-only/no-alternative skips from the native report.

## Still requires manual hardware/Windows acceptance

- [ ] Actual effect of every fixed registry preference on supported Windows builds; legacy or policy-overridden settings may be ignored.
- [ ] Thermal, battery, sleep and workload behavior of power changes on the user's hardware.
- [ ] Human UI/DPI/keyboard/high-contrast/screen-reader review.
- [ ] Standalone EXE on clean Windows 10/11 x64 without installed .NET.
- [ ] Repeated before/after measurements before claiming any performance benefit.

## Planned; not represented as functioning controls

- [ ] Read-only SetupAPI driver enumeration and official-source compatibility matching.
- [ ] Network ping/jitter/loss diagnostics and measured before/after frame-time capture.
- [ ] GPU/temperature/power/storage-health sensor integrations.
- [ ] Extended read-only firmware intelligence, game profiles and benchmark-backed recommendations.
- [ ] System Restore integration and narrowly scoped elevation for future administrator operations.
- [ ] Installer, secure updater and certificate-backed signing.

The long-term 300+ proven optimisations goal is not met by calling UI preferences or AC/DC parameter endpoints measured performance improvements. The application displays real discovered counts and distinguishes configuration verification from actual effect and benchmark evidence. It does not flash firmware, install drivers, alter voltage/overclock settings, disable security services or invent hidden tweaks.
