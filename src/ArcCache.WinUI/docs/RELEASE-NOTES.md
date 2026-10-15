# Unified library preview

The Windows x64 download runs as `ArcCache.exe`. It contains the reviewed graphics-test and large-cache features previously shipped in two editions. Source and development instructions are in the repository root. Extract the full ZIP; no Python, terminal commands or SDK are required to run it.

## Included

- Combined local game library with launcher filtering and manual AMD64 executable selection.
- Store-scoped profiles and receipts; legacy Steam names preserved.
- First-run introduction, a single setup guide, visible size buttons and driver-update monitoring.
- Reviewed 8/16 GiB Xe2 options with explicit experimental/RAM confirmation; separate graphics tests and default/1 GiB game trials remain available where eligible.
- Local diagnostics, recovery and an external read-only overlay. SCSKiller integration remains Steam-only.

See [launcher coverage](../../../docs/GAME-LIBRARIES.md), [driver coverage](../../../docs/INTEL-GRAPHICS-COVERAGE.md) and [test evidence](../../../docs/TESTING.md). A discovered title is not automatically game-compatible. 8/16 GiB remain limited to the reviewed Xe2 image; Xe1/Xe3 larger patches and real large-cache capacity are unverified. Windows driver files and integrity enforcement are unchanged.
