# Getting started with Arc Cache

Download the matching Windows x64 ZIP from this repository's [Releases](https://github.com/nyannoying1337/ArcCache/releases). Extract the **entire** folder and run `ArcCache.exe`. The unsigned self-contained app does not require Python, PowerShell commands, or a .NET SDK on the player's PC. The declared minimum is Windows 10 build 19041; testing was on Windows 11.

## One app

Run `ArcCache.exe`. This app includes game setup, driver checks, monitoring and the larger-cache options described in the support guide. The available sizes depend on the exact driver files. Close any older Arc Cache instance before using this build; settings and setup records are shared.

**Check my graphics** opens Graphics and starts the support check. The first-run introduction can be skipped and reopened in Settings. Refresh games reads local launcher installation records. Add game lets you select a 64-bit executable from another launcher or a standalone game. [Launcher coverage and launch behavior](../../../docs/GAME-LIBRARIES.md).

## First game: five steps

1. Open **Graphics** and choose **Allow game-specific graphics files**. Arc Cache explains the one-time Windows change and restart needed. Start Arc Cache manually as an administrator only for this change; afterward, open it normally. [Why this is required](PRIVATE-DRIVERS.md).
2. Open the app normally. Select an installed game or choose Add game…, launch it in DX12 with the system driver, and leave it at the menu so the app can detect its rendering executable.
3. Close the game. Choose **Set up this game** to make an unchanged copy of its Intel graphics files. This uses about 1.3 GB per game with driver 32.0.101.9150.
4. Launch DirectX 12 again; keep Arc Cache open until it checks that the game can use its separate files. Close the game after verification.
5. Select a size and apply it after confirmation, then start the game once more so Arc Cache can confirm the new copy loads. The card then shows **Ready to play** with the size. Launch normally through its usual launcher afterwards; the app can be closed unless you want the overlay or SCSKiller.

Read and acknowledge multiplayer/anti-cheat risk before applying. Detection is incomplete; a warning is not a guarantee that a game will accept modified driver files or that your account is safe. No anti-cheat is bypassed.

## Recovery

Close the game and choose **Use default settings**. Do not delete receipts or private folders manually. If verification fails, keep the files and receipt, read Activity, and import only the matching original receipt. Shader caches are not removed by rollback. The WinUI workflow never replaces system driver files and does not require Safe Mode; the older global recovery utility is a separate advanced tool. Use Intel's official installer for a damaged system package.

After a driver update, Arc Cache detects the changed active package while open. Check support reviews the current files. Saved private setups remain visible, but incompatible operations stay blocked until checked or restored to default. A signature match or launcher entry does not automatically qualify a driver or game.

## SCSKiller and monitoring

**Prepare shaders with SCSKiller** compiles a game's shaders before you play. The Arc Cache download includes a matching SCSKiller build (`SCSKiller\cli\scskiller.exe`); Settings or **Change** on the card can select another one. The card always shows which SCSKiller is used and whether it can use driver copies.

For a game with a bigger size, the compile runs with that game's verified driver copy. SCSKiller checks before and after opening the GPU that the copy (by path and SHA-256) is what Windows loaded, and compiles nothing otherwise. Official SCSKiller releases (also portable/beta) can't do this, so Arc Cache blocks them for bigger sizes: they would trim the cache back to 512 MB. Don't compile Arc Cache games from the SCSKiller app itself for the same reason. Details: [SCSKiller guide](../../../docs/SCSKILLER.md).

**Show on-screen stats** starts an external overlay (no injection; windowed or borderless display): the loaded driver copy, this game's Intel cache file with its size against the limit, and a verdict. *GOOD* means the file hardly grows while you play, so the game reads its prepared cache. *The file shrank* means the normal driver trimmed it. Arc Cache links a game to its cache file when it watches a SCSKiller compile; before that, the overlay shows the file that grows most as a guess.

## What has actually been checked

- Arc B580, driver 32.0.101.9150: the unchanged control and every size (1, 2, 4, 8, 16 GB) pass the isolated test, which writes a shader cache and reloads it with the patched driver.
- Palworld at 16 GB: a SCSKiller compile with the driver copy filled its cache file to 760.8 MB (the normal driver stops at 536.9 MB); the next game start loaded the copy, the file grew by only 2.3 MB and no other cache file changed.
- Ready or Not at 16 GB: the copy loads and the setup shows Ready to play. Witcher 3 (earlier private copy) loaded gameplay.
- Automated checks cover patch discovery, Xe1 size locks, receipts, recovery, consent gates, library fixtures, SCSKiller option/progress parsing and cache-file linking.

Not established: other games, Xe1/Xe3 hardware, anti-cheat acceptance and frame-time improvements. Very large cache files can need roughly their size in RAM while the driver loads them.

## Troubleshooting logs

Settings → Troubleshooting offers opt-in detailed local logging and Open logs folder. `--debug` enables session-only diagnostics. See [troubleshooting](TROUBLESHOOTING.md) for privacy, rotation and report instructions. No automatic upload. Discovery and monitoring use the selected game.
