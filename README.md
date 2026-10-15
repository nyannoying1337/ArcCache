<p align="center">
  <img src="docs/assets/arc-cache-banner.svg" alt="Arc Cache — per-game shader-cache limits for Intel graphics" width="1000" />
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases"><img src="https://img.shields.io/badge/download-Windows_x64-36bff5?style=flat-square" alt="Windows x64 downloads" /></a>
  <img src="https://img.shields.io/badge/interface-WinUI_3-627d98?style=flat-square" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/status-release--2026.10.15-36bff5?style=flat-square" alt="Public release" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-7596b3?style=flat-square" alt="GPL-3.0-or-later" /></a>
</p>

<p align="center">
  A Windows app for testing larger Intel shader-cache limits on a per-game basis.<br />
  It keeps the installed Windows graphics driver unchanged and includes setup checks and recovery.
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases"><strong>Download</strong></a> ·
  <a href="src/ArcCache.WinUI/docs/QUICKSTART.md"><strong>Getting started</strong></a> ·
  <a href="docs/INTEL-GRAPHICS-COVERAGE.md"><strong>Driver support</strong></a> ·
  <a href="docs/DEVELOPMENT.md"><strong>Build from source</strong></a> ·
  <a href="https://github.com/nyannoying1337/ArcCache/issues"><strong>Report a problem</strong></a>
</p>

## Why Arc Cache exists

Games compile shaders into instructions the GPU can use. The driver saves compiled results in a cache so they can be reused. If useful data is discarded, the game may need to compile it again. Arc Cache provides a guided way to try a larger cache limit for one game without replacing the installed driver. [Intel explains shader compilation and stutter](https://www.intel.com/content/www/us/en/support/articles/000102440/graphics.html).

This project changes a driver cache setting; it does not create shaders or promise smoother gameplay. Cache growth, effective capacity and performance improvements have not been measured. Larger cache files can use more disk space and memory. See the [test coverage and limits](docs/TESTING.md).

## Download and start

[**Get the Windows x64 release**](https://github.com/nyannoying1337/ArcCache/releases/tag/arc-cache-2026.10.15-r1)

For the easiest setup, download `ArcCache-Setup-2026.10.15.1-Windows-x64.exe` and follow the installer. It installs for your Windows account and adds an Arc Cache shortcut to the Start menu. Administrator access is not needed to install the app.

The ZIP is also available for portable use: extract the **entire** archive and run `ArcCache.exe`. Both downloads are unsigned. Keep all companion files together; the app uses programs and runtime files beside the main EXE. The source build does not include Intel driver DLLs.

## Set up a game

1. Open **Graphics** and choose **Check support**. Arc Cache checks the installed Intel driver files. Unknown or ambiguous files cannot be patched automatically.
2. If the app asks, enable **game-specific settings** once. This Windows setting applies to the PC, needs administrator approval and a restart. Arc Cache does not elevate itself.
3. Select a game from **My games**, or add its 64-bit executable. Start the game in DirectX 12 with Arc Cache open so it can identify the actual game process and graphics driver.
4. Close the game and choose **Set up this game**. Start it again while Arc Cache checks that its private original copy loads, then close it.
5. Choose a cache size and apply it after confirmation. Use **Use default settings** to undo that game's private setup. The shader cache is kept.

The [quickstart](src/ArcCache.WinUI/docs/QUICKSTART.md) has the full flow and recovery instructions.

## Driver support

Support is based on exact driver-file hashes and tested bytes, not only the GPU model. Current paths are summarized below; the [coverage guide](docs/INTEL-GRAPHICS-COVERAGE.md) has details.

| Driver files | Available path | Remaining limits |
| --- | --- | --- |
| Reviewed Xe2 32.0.101.9034 | Regular game setup: default, 1, 2 or 4 GiB; 8/16 GiB options for this exact image | 8/16 GiB gameplay, reload and effective capacity are unverified; high memory use is possible |
| Xe1 / Arc A-series `igd12um64xeh.dll`, versions 32.0.101.9034 and 32.0.101.9150 | Separate original and 1 GiB checks; a 1 GiB game trial can follow passing checks | No positive Arc A-series hardware or game result is available |
| Xe2/Xe3 candidate driver files | Separate original and 1 GiB checks; eligible files can continue to a 1 GiB game trial | This does not qualify the driver generally; positive Xe3 hardware/game results are unverified |
| Other or changed files | Read-only inspection where possible | Unknown, changed or ambiguous images remain blocked from patching |

The 8/16 GiB patch is limited to the exact reviewed Xe2 32.0.101.9034 image. A large cache can require roughly its file size in additional memory while loading. A configured limit does not immediately reserve that memory.

## Game libraries

The app reads local installation records for Steam, Epic, Battle.net, GOG, Ubisoft Connect, EA, Riot, Amazon, Rockstar, Xbox game folders and Legendary/Heroic Epic installs. **Add game** supports a manually selected executable from another launcher or a standalone install. Discovery does not prove that a game is compatible. See [library coverage and launch behavior](docs/GAME-LIBRARIES.md).

## Safety and recovery

- Only the selected game's private graphics files are changed. The installed Windows driver remains original.
- Patch and recovery operations check supported hashes, saved records and files before acting. The app asks before changing game files or the one-time Windows setting.
- Multiplayer and anti-cheat detection is incomplete. Modified graphics files may prevent a game from starting or lead to account penalties. No anti-cheat or Windows integrity enforcement is bypassed.
- The optional SCSKiller connection uses an existing CLI. Compilation uses the stock driver; private-cap compilation is not supported by this bridge.

Read [private driver routing](src/ArcCache.WinUI/docs/PRIVATE-DRIVERS.md) before enabling the machine-wide Windows setting.

## Tested behavior

Witcher 3 single-player loading was checked with a private copy. Ready or Not menu loading was checked with the original, 1 GiB and 4 GiB copies after its private setup was repaired. These results do not establish compatibility for other games. Automated checks cover patch bytes, setup/recovery rules, launcher records, consent and related policy; larger-cache performance and real cache growth remain unmeasured. See [test coverage](docs/TESTING.md).

## Source and development

The repository contains the C# and XAML source. Open [`ArcCache.slnx`](ArcCache.slnx), or build and run the checks on Windows:

```powershell
dotnet publish src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache
dotnet run --project src/ArcCache.WinUI/Tests/ArcCache.Tests.csproj -c Release
```

The default source build needs the .NET 10 SDK, Windows SDK and native build tools. The app ZIP is self-contained for players; they do not need a development SDK. See [development requirements and additional checks](docs/DEVELOPMENT.md).

**License:** GPL-3.0-or-later. See [source attribution and redistribution notes](NOTICE.md). Proprietary Intel driver DLLs and Windows debug-helper DLLs are not included in this repository.
