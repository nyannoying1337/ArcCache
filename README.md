<p align="center">
  <img src="docs/assets/arc-cache-banner.svg" alt="Arc Cache: per-game shader cache limits for Intel Arc" width="1000" />
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases/latest"><img src="https://img.shields.io/badge/download-Windows_x64-36bff5?style=flat-square" alt="Windows x64 download" /></a>
  <img src="https://img.shields.io/badge/release-2026.10.16-36bff5?style=flat-square" alt="Release 2026.10.16" />
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

## Why

Games compile shaders while you play, which causes stutter. The graphics driver keeps compiled shaders in a cache so this happens only once. On Intel Arc, the D3D12 driver allows **512 MB per game**. A modern game needs more than that, so the driver throws old shaders away and the game has to compile them again: in the next session, in the next area, after every restart. ([Intel on shader compilation stutter](https://www.intel.com/content/www/us/en/support/articles/000102440/graphics.html))

Arc Cache raises that limit **for the games you choose**. Together with [SCSKiller](docs/SCSKILLER.md), which compiles a game's shaders before you play, the whole shader set can stay on disk.

## What's new in 2026.10.16

- **Works with your current driver.** Sizes are no longer tied to one reviewed driver file. Arc Cache finds the cache code in the installed driver and tests every size on your PC before it offers it. Tested: Arc B580, driver 32.0.101.9150, all sizes up to 16 GB.
- **Real reload test.** Each size test writes a shader cache and reads it back with the patched driver. It no longer just checks that the driver loads.
- **SCSKiller can fill the big cache.** "Prepare shaders" compiles with the game's own driver copy, so the result isn't cut back to 512 MB. Palworld at 16 GB: **760.8 MB** of shaders kept, where the normal driver stops at 512 MB.
- **Overlay checks that the cache is used.** It shows the game's cache file, its size against the limit, and whether the game reads it or keeps compiling.
- **The SCSKiller in use is shown**, with its version and whether it can use a driver copy. Builds that can't use one are blocked for large sizes, because they would shrink the cache back to 512 MB.
- **My games** no longer gets stuck on "Launch again", Apply always gives feedback, and the setup state survives restarts.
- **Settings → Reset Arc Cache completely** (everything, with a clear warning) and **Clean up old driver tests** (frees several GB).
- **More libraries and icons:** HoYoPlay (Genshin, Star Rail, ZZZ) and Kuro (Wuthering Waves) are detected, and games from more launchers show their icons.

## Download and install

Get the latest release on the [**Releases page**](https://github.com/nyannoying1337/ArcCache/releases/latest):

- **`ArcCache-Setup-2026.10.16-Windows-x64.exe`**: the installer, recommended. It installs for your Windows account only (no admin rights needed), adds a Start menu entry and updates an older Arc Cache in place.
- **`ArcCache-2026.10.16-Windows-x64.zip`**: the portable version. Extract the **whole** ZIP and run `ArcCache.exe`.

Each file has a `.sha256` next to it. Both are unsigned, so Windows SmartScreen may ask once. Arc Cache is self-contained, so you don't need .NET or anything else. It needs Windows 10 19041 or newer (tested on Windows 11) and an Intel Arc or Intel Xe graphics driver. A matching SCSKiller build is included in the `SCSKiller` folder.

## How it works

1. **A private copy for one game.** Arc Cache copies the installed Intel driver package into a folder next to the game's exe (`<game>.exe.local`). It changes **one value** in that copy, the per-game cache limit, and records the hash of every file.
2. **Windows loads that copy only for that game.** This uses Windows' built-in per-app DLL redirection (`DevOverrideEnable`). You turn it on once (admin rights, one restart). Every other program keeps the normal driver.
3. **Every size is tested first.** On your PC, Arc Cache runs a small test program with an unchanged copy and then with each size. The test creates real shaders, writes the cache and reloads it. Only sizes that pass can be chosen.
4. **Checks before and after.** Arc Cache shows which driver file the game actually loaded. "Use default settings" removes the copy again, but only the files it created and recorded.

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

- **Anti-cheat:** the copy is a **modified, unsigned** Intel DLL inside the game process. Single-player games are fine. Games with anti-cheat (online shooters, many gacha games) may refuse to start or may **ban accounts**. Arc Cache warns and asks for confirmation, but its detection is incomplete. When in doubt, don't use it on online games.
- **Disk:** each game setup needs about **1.3 GB** for the driver copy (driver 9150), plus the cache itself, up to the size you choose, on the system drive (`%USERPROFILE%\AppData\LocalLow\Intel\ShaderCache`).
- **RAM:** with very large caches, the driver can need roughly the cache file's size in RAM while loading it.
- **D3D11 games:** only the D3D12 driver is changed. D3D11 caches stay at 512 MB.
- **Microsoft Store / Xbox app games** are listed, but they are packaged apps, and private driver copies probably don't apply to them. This is untested.
- **Driver updates:** a set-up game keeps loading its copy of the *old* driver until you set it up again. Whether an old copy works with a newer installed driver hasn't been tested yet. After an update, open Arc Cache and run **Check support**, then return set-up games to default and set them up again. Until then, Arc Cache blocks starting them from its own button; a start from Steam isn't blocked.
- **Not affiliated with Intel or the SCSKiller authors.** Use at your own risk.

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
