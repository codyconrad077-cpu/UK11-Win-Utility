# Windows acceptance gate

Not executed in the Linux development machine. Before distributing this milestone:

1. Run `./scripts/build.ps1 -Publish` on Windows 10 and Windows 11. The native test runner must pass; it uses temporary keys, not the real catalogue paths.
2. Launch the portable EXE as a standard user; confirm no console or UAC prompt. Verify CPU, board, BIOS, OS, thread count and memory against Windows tools. Unavailable hardware fields must stay visibly unavailable.
3. Check navigation, keyboard focus, search persistence and resizing at 100%, 150% and 200% DPI. Check high contrast and screen reader usability; accessibility has not yet been formally audited.
4. Record each original catalogue DWORD and its type in a disposable VM. Test supported, absent and unexpected-type detection. Confirm previews, cancelled changes, read-only/denied-access failures and backup creation before mutation.
5. Apply each supported action separately. Check the exact registry write, actual desktop/capture behaviour after the stated refresh, and accurate displayed state after restarting UK11. A stored value alone is not evidence of a performance gain.
6. Restore individually and confirm exact original DWORD. Apply again, interrupt the app after backup creation, reopen and restore. Check logs and pending recovery. Change a value externally to a third value and verify restore refuses it.
7. Open a second instance and confirm the data-directory lock. Close while an operation is active and confirm closing is deferred. Confirm no continuous background process remains after closing.
8. Copy the self-contained output to a clean x64 Windows VM without .NET; launch and repeat the tests. An unsigned executable may trigger Windows reputation prompts. Do not bypass security protections or advertise signing.

Do not publish as production-ready until this gate is complete and failures are fixed. Test performance using repeatable workloads and before/after measurements; none are supplied by this milestone.
