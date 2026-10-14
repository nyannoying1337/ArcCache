# Arc Cache desktop app

The WinUI 3 application handles Steam discovery, private-copy setup, presets, loading observations, recovery and optional diagnostics. It uses the separate compiled safety worker for validated operations. The installed system driver remains original.

## Guides

- [Getting started](docs/QUICKSTART.md)
- [Private routing and rollback](docs/PRIVATE-DRIVERS.md)
- [Troubleshooting logs](docs/TROUBLESHOOTING.md)
- [Build requirements and commands](../../docs/DEVELOPMENT.md)
- [Test coverage and compatibility](../../docs/TESTING.md)

## Behavior

New game profiles are discovered from a unique running DX12 executable under the selected Steam install and its loaded original Intel module. Executable and package hashes are pinned. Preparation, limit changes and removal require explicit consent and a closed game. The original private copy must be observed loading before a patched limit is applied.

The header distinguishes saved disk state from runtime observations. Missing receipts, changed files, extra files, unknown packages and changed executables stop affected operations. Recovery stays available where the recorded private set verifies. Old debug-helper-incompatible sets require return-to-stock and fresh preparation.

Multiplayer, anti-cheat indicators and incomplete checks require separate risk acknowledgment. Detection is incomplete and no anti-cheat is bypassed. The external overlay reports the shared Intel cache folder, not a proven per-game subset. The optional SCSKiller bridge is stock-driver-only; private-cap compilation and simultaneous recorder compatibility are unresolved.

Settings exposes the one-time machine-wide private-routing setting and opt-in bounded local diagnostics. The app never elevates itself or changes files silently. Normal play does not require the app running; its overlay does.

## Experimental driver inspection

Try a compatibility check can scan a selected DLL or the installed Xe3 DLL. Structural eligibility, reviewed patch bytes, isolated device results and game-profile qualification are shown separately. [Xe3 testing workflow](docs/XE3-EXPERIMENTS.md).
