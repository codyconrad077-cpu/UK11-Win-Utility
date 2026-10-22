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
- [x] 155 portable tests pass: per-definition apply/undo and missing-value behavior, idempotence, faults, conflicts, persisted originals, preferences, corrupted metadata, bounds/choices, non-restorable originals, batch failure rollback, stale previews, duplicate targets and catalogue integrity.
- [x] Windows validation passed in [release run 39846513901](https://github.com/codyconrad077-cpu/UK11-Win-Utility/actions/runs/39846513901): 155 portable tests, 45 isolated registry checks, hardware/memory checks, 338 native power write/verify/exact-restore checks on an inactive duplicate plan, and eight WPF checks including startup/navigation/search/favourites/minimum-size layout/rendering.
- [x] The test machine exposed 401 targets: 45 fixed preferences and 356 AC/DC endpoints for 178 power parameters. 338 power endpoints were writable and verified; 18 were read-only; zero no-alternative skips. Counts vary by computer.
- [x] Inspected final Windows dashboard/catalogue screenshots from v0.2.0-preview.2. Dark scrollbars, readable filters, bounded titles and a visible matched selection are rendered correctly. Minimum-size layout passed. Human DPI/accessibility review remains outstanding.
- [x] Published the self-contained unsigned v0.2.0-preview.2 EXE on GitHub Releases and verified its direct download returns HTTP 200. Attached native/UI JSON reports preserve the evidence and counts.

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
