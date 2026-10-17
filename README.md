<p align="center">
  <img src="docs/assets/arc-cache-banner.svg" alt="Arc Cache: per-game shader cache limits for Intel Arc" width="1000" />
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases/latest"><img src="https://img.shields.io/badge/download-Windows_x64-36bff5?style=flat-square" alt="Windows x64 download" /></a>
  <img src="https://img.shields.io/badge/release-2026.10.19-36bff5?style=flat-square" alt="Release 2026.10.19" />
  <img src="https://img.shields.io/badge/interface-WinUI_3-627d98?style=flat-square" alt="WinUI 3" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-7596b3?style=flat-square" alt="GPL-3.0-or-later" /></a>
</p>

<p align="center">
  <b>Give single games a bigger Intel shader cache: 1 to 16 GB instead of 512 MB.</b><br />
  The installed Windows driver stays untouched, every size is tested on your PC first,<br />
  and one click returns a game to normal.
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases/latest"><strong>Download</strong></a> ·
  <a href="src/ArcCache.WinUI/docs/QUICKSTART.md"><strong>Getting started</strong></a> ·
  <a href="docs/SCSKILLER.md"><strong>SCSKiller</strong></a> ·
  <a href="docs/INTEL-GRAPHICS-COVERAGE.md"><strong>Driver support</strong></a> ·
  <a href="docs/DEVELOPMENT.md"><strong>Build from source</strong></a> ·
  <a href="https://github.com/nyannoying1337/ArcCache/issues"><strong>Report a problem</strong></a>
</p>

## In short

- **What it does:** a game you pick gets its own copy of Intel's graphics files with a larger shader cache limit (1, 2, 4, 8 or 16 GB). Everything else on your PC keeps the normal driver.
- **Who it's for:** Intel Arc (Xe2, Xe3 and, with 1 GB, Xe1) owners whose games re-compile shaders and stutter because the 512 MB cache is full.
- **Who it's not for:** games where an anti-cheat runs while you play, even in single-player. Read [Anti-cheat and online games](#anti-cheat-and-online-games) before you set anything up.
- **How to undo:** **Use default settings** for one game, **Settings → Reset Arc Cache completely** for everything.

## Why

Games compile shaders while you play, which causes stutter. The graphics driver keeps compiled shaders in a cache so this happens only once. On Intel Arc, the D3D12 driver allows **512 MB per game**. A modern game needs more than that, so the driver throws old shaders away and the game has to compile them again: in the next session, in the next area, after every restart. ([Intel on shader compilation stutter](https://www.intel.com/content/www/us/en/support/articles/000102440/graphics.html))

Arc Cache raises that limit **for the games you choose**. Together with [SCSKiller](docs/SCSKILLER.md), which compiles a game's shaders before you play, the whole shader set can stay on disk.

## How it works

```
 C:\Games\Palworld\...\Palworld-Win64-Shipping.exe
 C:\Games\Palworld\...\Palworld-Win64-Shipping.exe.local\   <- the game's driver copy (about 1.3 GB)
     igd12um64xe2.dll   <- the only changed file: cache limit 512 MB -> 16 GB
     ... every other Intel file, unchanged and hash-checked

 Windows: DevOverrideEnable = 1   <- "game-specific settings": Windows honours .exe.local folders
```

1. **A private copy for one game.** Arc Cache copies the installed Intel driver package into a folder next to the game's exe (`<game>.exe.local`). It changes **one value** in that copy, the per-game cache limit, and records the hash of every file.
2. **Windows loads that copy only for that game.** Normally Windows ignores such a folder. The Windows setting [`DevOverrideEnable`](https://learn.microsoft.com/windows/win32/dlls/dynamic-link-library-redirection) ("game-specific settings" in the app) tells it to look there first. You turn it on once (administrator rights, one restart). Programs without such a folder keep the normal driver.
3. **Every size is tested first.** On your PC, Arc Cache runs a small test program with an unchanged copy and then with each size. The test creates real shaders, writes the cache and reloads it. Only sizes that pass can be chosen. After a passing test, older tests are deleted automatically.
4. **Checks before and after.** Arc Cache shows which driver file the game actually loaded. "Use default settings" removes the copy again, but only the files it created and recorded.

Your installed driver is never modified, and Windows security (driver signature enforcement, Secure Boot) stays on.

## Anti-cheat and online games

**Short version:** use Arc Cache only for games where no anti-cheat runs while you play. Single-player isn't enough if the anti-cheat starts anyway. And expect Riot games to refuse to start while game-specific settings are on.

There are two separate things an anti-cheat can object to:

| | Affects | What can happen | How to fix it |
| --- | --- | --- | --- |
| **1. The Windows setting** (`DevOverrideEnable`) | The whole PC, even games Arc Cache never touched | Some anti-cheats refuse to start the game | Turn game-specific settings off in **Settings**, restart Windows |
| **2. The game's driver copy** | Only the set-up game | The anti-cheat may block the game or **flag the account** | **Use default settings** for that game |

### 1. Why the Windows setting itself is a problem

`DevOverrideEnable` lets *any* program load DLLs from a `<program>.exe.local` folder instead of the real system files. That is exactly what Arc Cache needs, but it is also a way to slip foreign code into a game: Microsoft's documentation notes that redirection works around the loader's normal protection against [DLL planting](https://learn.microsoft.com/windows/win32/dlls/dynamic-link-library-redirection). Some kernel-level anti-cheats therefore check the setting and refuse to start while it is on:

- **Riot Vanguard** (VALORANT, League of Legends, 2XKO): the game stops with *"Failed to Launch: Something unusual happened"*. Riot's own fix is [turning DevOverrideEnable off](https://support-valorant.riotgames.com/hc/en-us/articles/49818408307475-Deactivating-DevOverrideEnable-in-Windows-Registry-Editor). Arc Cache detects an installed Vanguard and warns you before you turn the setting on.
- **Other anti-cheats:** we don't have a confirmed list yet. If a game starts only while game-specific settings are off, please [report it](https://github.com/nyannoying1337/ArcCache/issues) with the game and the error message.

To play such a game: return every set-up game to default settings, turn game-specific settings off in **Settings**, restart Windows. Turn it back on (and restart) when you want to use Arc Cache again. Your shader caches stay on disk either way.

### 2. Why a driver copy and anti-cheat don't mix

Inside a set-up game, `igd12um64xe2.dll` (or its Xe1/Xe3 sibling) is a **modified Intel DLL**. Its Intel signature is no longer valid. Anti-cheats look at every module loaded into the game process; a changed, unsigned graphics DLL looks just like a cheat that hooks the renderer. The best case is that the game refuses to start; the worst case is an **account ban**.

What Arc Cache does about it:

- **The game list marks anti-cheat games** before you open them:
  - <kbd>Anti-cheat</kbd> (red): an anti-cheat that runs every time the game starts, such as Riot Vanguard, EA Javelin, Anti-Cheat Expert (ACE), the HoYoverse and NetEase anti-cheats, nProtect GameGuard, XIGNCODE3 or Denuvo Anti-Cheat. Don't set these games up.
  - <kbd>Anti-cheat (EAC)</kbd>, <kbd>Anti-cheat (BattlEye)</kbd> (amber): Easy Anti-Cheat, BattlEye or PunkBuster files. What counts is whether the anti-cheat **runs** while you play, not whether you play single-player. **Halo: The Master Chief Collection** can start with anti-cheat disabled, so Arc Cache is reasonable there in that mode only. **Le Mans Ultimate** starts Easy Anti-Cheat every time, so it sees the changed driver file even in a practice session: don't set up games like that.
  - The filter **No anti-cheat found** shows the rest. That isn't a guarantee: the check looks for known file names only.
- **Arc Cache watches what actually runs.** Files only show that an anti-cheat is installed. While the selected game runs (the first start with the normal driver, and every check after that), Arc Cache looks for a running Easy Anti-Cheat, BattlEye, EA Javelin, ACE or NetEase anti-cheat process. These start with the protected game and stop with it.
  - Seen **before setup**: the game is marked <kbd>Anti-cheat runs</kbd> and can't be set up. Nothing has been changed yet, so nothing is at risk. If the game can start without the anti-cheat (Halo MCC's "anti-cheat disabled" mode), start it that way and leave it running for 30 seconds: the mark goes away by itself.
  - Seen **with a driver copy**: Arc Cache shows an error right away and asks you to choose **Use default settings**.
  - Games with an always-on anti-cheat (red badge) are blocked the same way before setup.
- **You can still go ahead, at your own risk.** Blocked games have a **Use Arc Cache with this game anyway…** button. It explains what can happen (the game refusing to start, a delayed and possibly permanent or hardware-wide ban, no protection from single-player modes, no way to undo it) and only continues after you tick the box and type `BAN RISK`. The game then shows a red "Anti-cheat risk accepted" banner, which you can turn off at any time.
  - Example from testing: Satisfactory has no anti-cheat files at all, which is why it works. Le Mans Ultimate starts Easy Anti-Cheat through `start_protected_game.exe` every time, so Arc Cache stops at the first start.
- **Before setup**, a full scan of the game folder runs again. If it finds anti-cheat, multiplayer markers or can't finish, setup needs your explicit confirmation.
- The scan knows Easy Anti-Cheat, BattlEye, Riot Vanguard, EA Javelin, ACE/Tencent, HoYoverse, NetEase, nProtect GameGuard, XIGNCODE3, NCGuard, HackShield, BlackCipher, Denuvo Anti-Cheat, PunkBuster, EQU8 and Battle.net games. Steam's own "Valve Anti-Cheat" flag is shown as a warning too.

> **When in doubt, don't.** No tool can promise that an anti-cheat accepts a modified driver file. A warning-free scan only means no *known* anti-cheat files were found.

## What's new in 2026.10.19

- **Anti-cheat games are marked in My games.** A quick look into each game folder shows a red **Anti-cheat** badge for anti-cheats that run whenever the game starts (Riot Vanguard, EA Javelin, ACE, HoYoverse, NetEase, GameGuard, XIGNCODE3 …) and an amber **Anti-cheat (EAC)** / **(BattlEye)** badge where it depends on how the game starts. New filters: "No anti-cheat found" and "Anti-cheat".
- **Arc Cache watches what actually runs.** While the selected game runs, Arc Cache checks for a running Easy Anti-Cheat, BattlEye, EA Javelin, ACE or NetEase anti-cheat. Seen before setup: the game is marked **Anti-cheat runs** and can't be set up (nothing has been changed). Seen with a driver copy: Arc Cache asks you to use default settings right away. A start without the anti-cheat (e.g. Halo MCC's "anti-cheat disabled" mode) clears the mark after 30 seconds.
- **Riot Vanguard warning.** If Vanguard is installed, Arc Cache says before you turn game-specific settings on that VALORANT, League of Legends and 2XKO refuse to start while the setting is on.
- **README:** a full section on anti-cheat and online games, an FAQ, and a short overview.

<details>
<summary><b>Older versions</b></summary>

### What's new in 2026.10.18

- **Old driver tests no longer pile up.** Each test keeps a full 1.3 GB driver copy. After every passing test, Arc Cache now deletes older ones and keeps only the newest test per cache size and the tests your games use. Settings shows the real size and folder. Old tests from earlier app versions can be removed too (on the developer's PC: 56.7 GB down to 9.2 GB, games stayed set up).
- **Game-specific settings can be turned off again.** Graphics → Change and the new Settings card show the status, a turn-on/turn-off button, and which games still need their driver copy before it can go off. When administrator rights are needed, Arc Cache says so and reopens itself as administrator after Windows asks for approval.
- **A clearer introduction.** Three screens: what Arc Cache does, what the Windows setting (`DevOverrideEnable`) does and doesn't do, and the setup steps.

### What's new in 2026.10.17

- **No stutter from Arc Cache while you play.** Once the game's driver copy is confirmed, Arc Cache only checks every 15 seconds that the game still runs, with no extra process or driver hashing.
- **Faster checks.** File hashes use Windows' own fast SHA-256, and unchanged files aren't hashed again just to show a page.
- **The right cache file.** Arc Cache and the overlay ask Windows which cache file the running game has open, instead of guessing from file growth. A compile no longer re-links the game to its separate D3D11 file.
- **Overlay:** "GOOD" only appears when the game's driver copy is loaded; with the normal driver it warns.
- **Interrupted changes repair themselves:** leftovers of a stopped size change or removal are cleaned up (only Arc Cache's own files) before the next action.
- **SCSKiller runs in the background:** the app stays usable during a compile, and closing asks before stopping it.
- **Uninstall warns** while games still have a driver copy. Long install paths and broken manual library entries no longer cause problems.

### What's new in 2026.10.16

- **Works with your current driver.** Sizes are no longer tied to one reviewed driver file. Arc Cache finds the cache code in the installed driver and tests every size on your PC before it offers it. Tested: Arc B580, driver 32.0.101.9150, all sizes up to 16 GB.
- **Real reload test.** Each size test writes a shader cache and reads it back with the patched driver. It no longer just checks that the driver loads.
- **SCSKiller can fill the big cache.** "Prepare shaders" compiles with the game's own driver copy, so the result isn't cut back to 512 MB. Palworld at 16 GB: **760.8 MB** of shaders kept, where the normal driver stops at 512 MB.
- **Overlay checks that the cache is used.** It shows the game's cache file, its size against the limit, and whether the game reads it or keeps compiling.
- **The SCSKiller in use is shown**, with its version and whether it can use a driver copy. Builds that can't use one are blocked for large sizes, because they would shrink the cache back to 512 MB.
- **My games** no longer gets stuck on "Launch again", Apply always gives feedback, and the setup state survives restarts.
- **Settings → Reset Arc Cache completely** (everything, with a clear warning) and **Clean up old driver tests** (frees several GB).
- **More libraries and icons:** HoYoPlay (Genshin, Star Rail, ZZZ) and Kuro (Wuthering Waves) are detected, and games from more launchers show their icons.

</details>

## Download and install

Get the latest release on the [**Releases page**](https://github.com/nyannoying1337/ArcCache/releases/latest):

- **`ArcCache-Setup-2026.10.19-Windows-x64.exe`**: the installer, recommended. It installs for your Windows account only (no admin rights needed), adds a Start menu entry and updates an older Arc Cache in place.
- **`ArcCache-2026.10.19-Windows-x64.zip`**: the portable version. Extract the **whole** ZIP and run `ArcCache.exe`.

Each file has a `.sha256` next to it. Both are unsigned, so Windows SmartScreen may ask once. Arc Cache is self-contained, so you don't need .NET or anything else. It needs Windows 10 19041 or newer (tested on Windows 11) and an Intel Arc or Intel Xe graphics driver. A matching SCSKiller build is included in the `SCSKiller` folder.

## Set up a game

1. **Graphics → Check support.** If asked, allow **game-specific settings** once (admin rights and a restart).
2. **My games →** pick the game → **Set up this game**. Start the game once with the normal driver, so Arc Cache finds the exe that really renders.
3. **Launch and check.** Arc Cache confirms that the game loads its unchanged private copy.
4. **Cache size →** choose 1, 2, 4, 8 or 16 GB → **Apply**. Start the game once more; when the check passes, the card shows **"Ready to play · 16 GB"**.
5. Optional: **Prepare shaders** (SCSKiller) to fill the cache before you play. See [docs/SCSKILLER.md](docs/SCSKILLER.md).

To undo, use **Use default settings** (one game) or **Settings → Reset Arc Cache completely** (everything). The [Getting started guide](src/ArcCache.WinUI/docs/QUICKSTART.md) has screenshots and recovery steps.

## Check that it works: the overlay

Start the game, then click **Show on-screen stats** in Arc Cache (windowed or borderless mode):

```
Game setup: custom copy loaded · 16 GiB from the game setup record
This game's cache file: 727.83 MiB of 16 GiB (4.4%) · +2.26 MiB
Cache use: GOOD - reading the prepared cache (only +2.26 MiB new)
```

- **GOOD:** the file hardly grows, so the game reads shaders from the cache instead of compiling them.
- **Still adding shaders:** new shaders are being compiled, which is normal in new areas.
- **WARNING, the file shrank:** something trimmed the cache back to 512 MB (the normal driver). Please report this.

## Driver and GPU support

| GPU family | Driver file | Sizes | Status |
| --- | --- | --- | --- |
| **Xe2**: Arc B580/B570 (Lunar Lake Arc 140V/130V: untested) | `igd12um64xe2.dll` | 1, 2, 4, 8, 16 GB | **Tested on Arc B580, driver 32.0.101.9150**: all size tests pass; Palworld and Ready or Not run at 16 GB |
| **Xe3**: Panther Lake (Arc B370/B390) | `igd12um64xe3.dll` | 1, 2, 4, 8, 16 GB | The code is recognised on driver 9150, but there's no hardware result yet. The test on your PC decides. |
| **Xe1**: Arc A-series | `igd12um64xeh.dll` | 1 GB only | Larger sizes would break this driver's cache setup, so they are locked. No hardware result yet. |
| Others: Xe-LPG, Iris Xe, older | – | – | Not supported |

Arc Cache doesn't go by GPU name. It looks at the installed driver file and only offers what that file's tests pass. After a driver update, run **Check support** again. More details: [docs/INTEL-GRAPHICS-COVERAGE.md](docs/INTEL-GRAPHICS-COVERAGE.md).

## Good to know

- **Anti-cheat:** see [Anti-cheat and online games](#anti-cheat-and-online-games). In short: only for games where no anti-cheat runs while you play, and Riot games refuse to start while game-specific settings are on.
- **Smart App Control** (Windows 11) may block the changed Intel DLL in a game's driver copy. This hasn't been tested yet; Arc Cache notes it in Recent actions when Smart App Control is on.
- **Disk:** each game setup needs about **1.3 GB** for the driver copy (driver 9150), plus the cache itself on the system drive (`%USERPROFILE%\AppData\LocalLow\Intel\ShaderCache`). The cache file only grows as the game needs it: choosing 16 GB doesn't reserve 16 GB. Driver tests keep about 1.3 GB per cache size; Settings shows how much.
- **RAM:** with very large caches, the driver can need roughly the cache file's size in RAM while loading it.
- **D3D11 games:** only the D3D12 driver is changed. D3D11 caches stay at 512 MB.
- **Microsoft Store / Xbox app games** are listed, but they are packaged apps, and private driver copies probably don't apply to them. This is untested.
- **Driver updates:** a set-up game keeps loading its copy of the *old* driver until you set it up again. Whether an old copy works with a newer installed driver hasn't been tested yet. After an update, open Arc Cache and run **Check support**, then return set-up games to default and set them up again. Until then, Arc Cache blocks starting them from its own button; a start from Steam isn't blocked.
- **Not affiliated with Intel or the SCSKiller authors.** Use at your own risk.

## FAQ

<details>
<summary><b>Does choosing 16 GB take 16 GB of disk space?</b></summary>

No. The size is a limit. The cache file starts small and only grows as the game compiles shaders. A large game with a full shader set typically ends up at a few hundred MB to a few GB.
</details>

<details>
<summary><b>Does Arc Cache change my installed Intel driver?</b></summary>

No. It copies the driver files into the game's folder and changes only that copy. The installed driver, other games and Windows security stay as they are.
</details>

<details>
<summary><b>Why does it need administrator rights?</b></summary>

Only for the Windows setting (`DevOverrideEnable` lives under `HKEY_LOCAL_MACHINE`). Arc Cache asks Windows for approval and reopens itself as administrator for that one change. Everything else runs as your normal user, and the installer doesn't need admin rights either.
</details>

<details>
<summary><b>VALORANT / League of Legends won't start anymore.</b></summary>

That's Riot Vanguard refusing `DevOverrideEnable`. Return your set-up games to default settings, turn game-specific settings off in **Settings**, restart Windows. See [Anti-cheat and online games](#anti-cheat-and-online-games).
</details>

<details>
<summary><b>Can I get banned?</b></summary>

If an anti-cheat runs while you play, yes, possibly, even in single-player. The driver copy is a modified DLL inside the game process. The most likely result is that the anti-cheat refuses to start the game, but a delayed ban can't be ruled out. Use Arc Cache only for games where no anti-cheat runs.
</details>

<details>
<summary><b>What happens when Intel releases a new driver?</b></summary>

Set-up games keep using their copy of the old driver. Open Arc Cache, run **Check support**, return set-up games to default settings and set them up again with the new driver.
</details>

<details>
<summary><b>How do I remove everything?</b></summary>

**Settings → Reset Arc Cache completely** returns every game to default, restores the previous Windows setting and deletes Arc Cache's data. The uninstaller warns if a game still has a driver copy.
</details>

## Build from source

```powershell
# app, worker and probe (needs .NET 10 SDK, Windows SDK, VS 2022 Build Tools with C++)
dotnet build src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Debug
dotnet build src/ArcCache.WinUI/Tests/ArcCache.Tests.csproj -c Debug
src/ArcCache.WinUI/Tests/bin/Debug/net10.0-windows/ArcCache.Tests.exe
# engine tests (no Intel files needed for these modes)
src/ArcCache.Engine/build.cmd
src/ArcCache.Engine/bin/ArcCap.Tests.exe --capabilities
```

The full build, the release packaging and the SCSKiller build are described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Proprietary Intel driver files are never part of this repository. The app copies them from your installed driver at runtime.

## License

GPL-3.0-or-later, see [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md). The bundled SCSKiller build comes from the [SCSKiller-Arc fork](https://github.com/nyannoying1337/SCSKiller) of [SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) (GPL-3.0 with its own additional permissions).
