# Getting started with Arc Cache

Download the matching Windows x64 ZIP from this repository's [Releases](https://github.com/nyannoying1337/ArcCache/releases). Extract the **entire** folder and run `ArcCache.exe`. The unsigned self-contained app does not require Python, PowerShell commands, or a .NET SDK on the player's PC. The declared minimum is Windows 10 build 19041; testing was on Windows 11.

## One app

Run `ArcCache.exe`. This app includes game setup, driver checks, monitoring and the larger-cache options described in the support guide. The available sizes depend on the exact driver files. Close any older Arc Cache instance before using this build; settings and setup records are shared.

**Check my graphics** opens Graphics and starts the support check. The first-run introduction can be skipped and reopened in Settings. Refresh games reads local launcher installation records. Add game lets you select a 64-bit executable from another launcher or a standalone game. [Launcher coverage and launch behavior](../../../docs/GAME-LIBRARIES.md).

## First game: five steps

1. Open **Graphics** and choose **Allow game-specific graphics files**. Arc Cache explains the one-time Windows change and restart needed. Start Arc Cache manually as an administrator only for this change; afterward, open it normally. [Why this is required](PRIVATE-DRIVERS.md).
2. Open the app normally. Select an installed game or choose Add game…, launch it in DX12 with the system driver, and leave it at the menu so the app can detect its rendering executable.
3. Close the game. Choose **Set up this game** to make an unchanged copy of its Intel graphics files. This uses about 608 MiB per game.
4. Launch DirectX 12 again; keep Arc Cache open until it checks that the game can use its separate files. Close the game after verification.
5. Select a preset and apply it after confirmation. Launch normally through its usual launcher. The app can now be closed unless you want the overlay.

Read and acknowledge multiplayer/anti-cheat risk before applying. Detection is incomplete; a warning is not a guarantee that a game will accept modified driver files or that your account is safe. No anti-cheat is bypassed.

## Recovery

Close the game and choose **Use default settings**. Do not delete receipts or private folders manually. If verification fails, keep the files and receipt, read Activity, and import only the matching original receipt. Shader caches are not removed by rollback. The WinUI workflow never replaces system driver files and does not require Safe Mode; the older global recovery utility is a separate advanced tool. Use Intel's official installer for a damaged system package.

After a driver update, Arc Cache detects the changed active package while open. Check support reviews the current files. Saved private setups remain visible, but incompatible operations stay blocked until checked or restored to default. A signature match or launcher entry does not automatically qualify a driver or game.

## SCSKiller and monitoring

The optional bridge uses a separately installed, matching SCSKiller CLI chosen in Settings. These app-only downloads do not include the CLI. Community access depends on your existing SCSKiller account/settings. Compilation uses the **stock** driver; private-cap compilation and simultaneous recorder compatibility are unresolved. Do not interpret shared Intel cache growth as proven per-game shader growth. The external overlay uses no injection; borderless/windowed display is recommended and exclusive fullscreen is unverified.

## What has actually been checked

Gameplay loading was checked in Witcher 3 with a private copy. Menu loading was checked in Ready or Not with repaired original, 1 GiB and 4 GiB copies. Loaded paths/disk hashes and unchanged system-driver hashes were observed. The Ready or Not repair handles a known 32-bit bundled debug helper with exact qualified 64-bit helpers copied from the player's system; changing a cap alone does not migrate older setups.

2/4 GiB constructors, patch hashes, tamper rejection, receipts/recovery, semantic ACL equality, consent gates, Steam fixtures, and quiet UI polling have automated checks. Actual capacity/growth effects remain unmeasured. 8/16 GiB passed isolated B580 D3D12 device/pipeline creation probes and reload-wrapper/unwind tests, **not** large-cache gameplay/reload. Very large files can require roughly their file size in contiguous RAM: the larger patch does not implement a low-memory Intel parser.

For source build/test instructions see [the app README](../README.md) and [the larger-cache research README](../../../research/LargeCache/README.md). Proprietary DLL test inputs are optional local files, not shipped fixtures.

## Troubleshooting logs

Settings → Troubleshooting offers opt-in detailed local logging and Open logs folder. `--debug` enables session-only diagnostics. See [troubleshooting](TROUBLESHOOTING.md) for privacy, rotation and report instructions. No automatic upload. Discovery and monitoring use the selected game.
