# Delivery progress

## Milestone 01 — implemented

- [x] Modular .NET 8 solution: WPF UI, portable engine, Windows adapters and two executable test runners.
- [x] Dark blue dashboard, navigation, search, persistent search/page preferences, live physical-memory bar, uptime, CPU/board/BIOS/OS inventory.
- [x] Three narrowly scoped existing-DWORD settings with IDs, exact implementations, compatibility detection, permissions, effects, risk and refresh guidance.
- [x] Confirmation preview, durable original backup, re-read verification, individual restore, external-change conflicts, crash recovery journal, activity log and per-user instance lock.
- [x] Self-contained unsigned Windows x64 portable publishing and Windows CI configuration.

## Tested in Linux

- [x] Release cross-compilation including WPF and the Windows integration runner: zero warnings/errors.
- [x] 19 portable assertion tests: each catalogue action, missing/unsupported settings, idempotence, exact undo, write/verification faults, conflict handling, restart recovery, preferences and corrupt/invalid backups.
- [x] Windows x64 self-contained single-file executable produced. This proves cross-compilation and bundling only, not Windows execution.

## Windows validation

- [ ] WPF launch, visual polish, keyboard/accessibility, DPI and resize acceptance.
- [x] Native registry adapter and hardware integration test runner: four checks passed on GitHub's Windows runner, alongside all 19 portable tests.
- [ ] Actual desktop/capture effects and real-setting rollback on Windows 10 and 11.
- [ ] Portable EXE on a clean machine without installed .NET; release signing and malware/reputation review.
- [x] Windows CI build, tests, self-contained publishing and artifact upload passed in [run 39839302119](https://github.com/codyconrad077-cpu/UK11-Win-Utility/actions/runs/39839302119). The PowerShell publishing argument issue was fixed. Artifact: `UK11-Ultimate-win-x64-unsigned` (64.4 MB).

## Planned; not presented as functioning features

- [ ] SetupAPI/Configuration Manager driver enumeration, signed driver metadata and official-source update matching.
- [ ] Ping/jitter/loss diagnostics and network inventory.
- [ ] Per-game profiles and measured before/after frame-time capture.
- [ ] CPU/GPU temperature, clocks, GPU usage, power and storage health integrations.
- [ ] Read-only firmware compatibility reports, extended BIOS intelligence.
- [ ] Category/profile/full rollback, favourites, exports and richer health/diagnostic reporting.
- [ ] System Restore integration and elevated broker for justified administrator operations.
- [ ] Secure updater, installer and certificate-backed signing.

Unsupported in this milestone: firmware writes, driver installation, overclocking, automatic debloating and speculative timer/HPET/security changes. No benchmark data or performance gains are claimed. The 500+ catalogue is a long-term direction, not the delivered action count.
