<p align="center">
  <img src="docs/assets/arc-cache-banner.svg" alt="Arc Cache — per-game shader-cache controls for Intel Arc" width="1000" />
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases"><img src="https://img.shields.io/badge/download-Windows_x64-36bff5?style=flat-square" alt="Download for Windows x64" /></a>
  <img src="https://img.shields.io/badge/UI-WinUI_3-627d98?style=flat-square" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/status-preview-e4b651?style=flat-square" alt="Preview software" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0--or--later-7596b3?style=flat-square" alt="GPL 3.0 or later" /></a>
</p>

<p align="center">
  <strong>Choose a cache-limit preset for a game without replacing your installed Intel driver.</strong><br />
  A native Windows app with launcher discovery and manual games, verified private copies and recovery controls.
</p>

<p align="center">
  <a href="https://github.com/nyannoying1337/ArcCache/releases"><strong>Downloads</strong></a> ·
  <a href="src/ArcCache.WinUI/docs/QUICKSTART.md"><strong>Getting started</strong></a> ·
  <a href="src"><strong>Browse source</strong></a> ·
  <a href="docs/DEVELOPMENT.md"><strong>Build it yourself</strong></a> ·
  <a href="https://github.com/nyannoying1337/ArcCache/issues"><strong>Report a problem</strong></a>
</p>

## Why Arc Cache exists

Games compile shaders into code the GPU can use. Doing that during gameplay can cause pauses or stutter; a shader cache saves compiled results so they can be reused. [Intel's shader-compilation overview](https://www.intel.com/content/www/us/en/support/articles/000102440/graphics.html) explains why avoiding repeated compilation matters.

The driver image reviewed by this project has a **512 MiB default cache-file limit**. If useful cached data is discarded or the cache is reset as it reaches its limit, that work may need to be compiled again. Arc Cache provides a GUI for testing larger, reviewed limits through per-game private driver copies, with verification and rollback instead of manual DLL edits.

**What this could help with:** retaining more reusable shader data, reducing repeated compilation when revisiting content, and investigating cache-related stutter. These are the intended benefits, **not measured performance claims**. Larger limits do not fix every source of stutter, create shaders that have never been compiled, or guarantee higher FPS. Games can override driver defaults; larger caches also use more disk space and can increase memory/loading costs. See [test coverage and limitations](docs/TESTING.md).

## Xe1 / Arc A-series

The preview includes a hash-pinned **Xe1 1 GiB test patch** for `igd12um64xeh.dll` in drivers 32.0.101.9034 and 32.0.101.9150. Original/patched hardware checks are required before a game trial. Positive Arc A-series hardware and effective cache capacity remain unverified. [Definition and test boundaries](src/ArcCache.WinUI/docs/XE1-SUPPORT.md).

Coverage includes A-series test patches and integrated-device discovery; it is not a B-series-only app. [Driver support and verification](docs/INTEL-GRAPHICS-COVERAGE.md).

## Check a new driver

Open **Graphics → Check support → Try patching** in the preview. Arc Cache can test an unchanged copy and a 1 GiB copy in its separate graphics program, with live progress, logs and checked cleanup. The separate test leaves games and installed driver files unchanged. A pass offers **Choose a game and use this copy**, with an explicit 1 GiB game-trial workflow. Unknown drivers are not automatically promoted to reviewed definitions. [Read the test workflow and limits](src/ArcCache.WinUI/docs/GRAPHICS-TRIAL.md).

The Graphics page also shows whole-PC RAM and the shared Intel disk-cache size. Shader-cache RAM usage cannot currently be measured.

## Download

[**Download Arc Cache preview for Windows x64**](https://github.com/nyannoying1337/ArcCache/releases/tag/arc-cache-unified-libraries-preview-2026.10.15)

There is one application: `ArcCache.exe`. It includes the graphics checks, driver-update detection, overlay and reviewed 8/16 GiB options previously distributed separately. **8/16 GiB remain highly experimental and require their exact supported driver.** Older releases are retained for recovery and source history.

## Game libraries

Arc Cache reads installed-game records for **Steam, Epic, Battle.net, GOG, Ubisoft Connect, EA, Riot, Amazon, Rockstar, Xbox game folders and Legendary/Heroic Epic installs**. Coverage depends on the records available locally; accounts are not queried or signed in. The library filter shows the discovered sources.

Use **Add game…** to select a 64-bit game executable from any launcher or a standalone installation. Manual entries can be removed after their custom setup is restored to default. Discovery is separate from graphics-driver compatibility and anti-cheat safety. Protected Xbox/WindowsApps installations are not unlocked or modified. [Discovery formats, launch behavior and limitations](docs/GAME-LIBRARIES.md).

**Driver support:** reviewed game setups require the exact **Xe2 32.0.101.9034 files**. New Xe2/Xe3 files with one trusted cache-setting candidate can continue from passed graphics tests to an explicit **1 GiB game trial**, with per-game loading checks and rollback. Active built-in and add-in Intel graphics can be selected. Unknown/ambiguous files without matching successful tests stay blocked; Xe3/iGPU gameplay remains unverified. [Follow the guided setup](src/ArcCache.WinUI/docs/GUIDED-GAME-SETUP.md).

> **These are unsigned previews.** Loading tests do not prove effective shader-cache capacity. 8/16 GiB gameplay and large-cache reload remain unverified; a large cache can require roughly its size in contiguous RAM. [Test coverage and compatibility](docs/TESTING.md).

## Get started

A first-run introduction explains setup and recovery. Reopen it from Settings. The game page shows one setup guide and visible cache-size buttons; unavailable sizes include a reason.

1. **Download and extract the entire ZIP.** Open `ArcCache.exe`. No Python, terminal commands or .NET SDK are needed to run it.
2. **Enable “Enable game-specific settings” once.** Manually run the app as administrator for this setting, confirm, then restart Windows. Use the app normally afterward.
3. **Select or add a game and launch DX12.** Leave it at the menu so Arc Cache can detect its actual rendering executable.
4. **Close the game and prepare its original private copy.** Launch again with the app open for the automatic loading check, then close the game.
5. **Choose and apply a limit.** Play normally through its usual launcher. Arc Cache can be closed unless you want its overlay.

[Full setup guide →](src/ArcCache.WinUI/docs/QUICKSTART.md) · [Private routing explained →](src/ArcCache.WinUI/docs/PRIVATE-DRIVERS.md)

## What the app handles

- **Per-game copies:** your installed driver remains original; only the selected game's private copy is patched.
- **Game libraries:** local launcher discovery, manual executable selection, library filtering and automatic DX12 executable detection.
- **Verification and recovery:** exact driver hashes, checked patch bytes, file/ACL validation and receipt-based rollback.
- **Troubleshooting:** optional local diagnostic logs and an external overlay for shared cache size; no game injection.
- **Optional SCSKiller bridge:** use your existing CLI/account. Compilation currently uses the stock driver, not the game's private cap.

**Multiplayer / anti-cheat:** warnings require explicit acknowledgment before continuing. Detection is incomplete, and modified driver files can cause launch failures or account penalties. No anti-cheat or Windows integrity enforcement is disabled.

## How private routing works

Arc Cache creates a verified driver set in **`<game.exe>.local`**, beside the game's rendering executable. The one-time Windows setting lets manifest-bearing applications use these private DLLs. Games without a private setup keep using the installed driver.

The setting is **machine-wide** and can also affect other `.local` apps. To undo **one game**, close it and choose **Use default settings**. You do not need to toggle the Windows setting or delete shader caches. [Routing, restart and restore details](src/ArcCache.WinUI/docs/PRIVATE-DRIVERS.md).

## Source code & development

**The complete C# and XAML source is in [`src/`](src).** Open [`ArcCache.slnx`](ArcCache.slnx), or build the app directly:

```powershell
dotnet publish src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache
```

| Source | What it contains |
| --- | --- |
| [WinUI app](src/ArcCache.WinUI/App) | XAML views, controls and game workflow |
| [Core](src/ArcCache.WinUI/Core) | Launcher discovery, state, diagnostics and CLI bridge |
| [Safety worker](src/ArcCache.WinUI/Worker) | Validated requests and read-only overlay |
| [Patch engine](src/ArcCache.Engine) | Qualified patches, private sets, ACL handling and rollback |
| [Tests](src/ArcCache.WinUI/Tests) | Core, worker and bridge checks; adjacent folders contain safety/native checks |

[Build requirements and test commands →](docs/DEVELOPMENT.md) · [Experimental research source →](https://github.com/nyannoying1337/ArcCache/tree/main/research/LargeCache)

## Xe3 and new driver DLLs

The preview can **inspect the DLL itself** and distinguish a structural candidate from a reviewed patch definition. Its Try a compatibility check page offers original/1/2/4 GiB isolated tests for the reviewed Xe3 image. The development machine does not need an Xe3 GPU; the tester's machine does.

A matching pattern alone does not qualify an unknown driver. Device tests leave games and the installed driver untouched, and do not establish per-game support or effective capacity. [Testing details](docs/TESTING.md).

## Built-in Intel graphics

Open **Graphics**, then choose the Intel graphics chip your game uses. Built-in chips are included in detection and selection; this does not change Windows' choice of GPU for the game.

Game setup depends on the actual driver files and observed game driver, not a B580-only model list. Matching reviewed Xe2 files can use the normal setup workflow. Reviewed Xe3 files can use the preview version's separate graphics check. Older HD/UHD/Iris or other unknown driver images may be detected but remain unsupported for patching. Positive gameplay tests on integrated hardware have not been performed. [Support details](docs/INTEGRATED-GRAPHICS.md).

## Need help?

Turn on **Settings → Troubleshooting → Save extra troubleshooting details**, reproduce the problem once, then choose **Open logs folder**. `--debug` enables logging for one session. Logs stay on your PC; review usernames and paths before sharing.

[Reporting guide](src/ArcCache.WinUI/docs/TROUBLESHOOTING.md) · [Open an issue](https://github.com/nyannoying1337/ArcCache/issues)

<details>
<summary><strong>Do I need to keep Arc Cache running?</strong></summary>

No. Once a setup is applied, Windows loads its private files on later game launches. The external overlay needs the app open or minimized. There is no background installer or service.

</details>

<details>
<summary><strong>What happens after a game or driver update?</strong></summary>

A changed executable must be detected again. The preview offers read-only active-driver update review; it does not automatically qualify new versions. Unsupported packages remain blocked. Use the recorded recovery workflow to return affected games to the system driver.

</details>

<details>
<summary><strong>Does the overlay prove my game's cache is growing?</strong></summary>

It measures the shared Intel cache folder, not a proven game-specific subset. Paths and disk hashes show observed loading, not in-memory patch bytes or effective capacity. Borderless/windowed presentation is recommended; exclusive fullscreen is unverified.

</details>

---

**GPL-3.0-or-later** · [License](LICENSE) · [Source attribution](NOTICE.md). Arc Cache is a standalone project; [SCSKiller](https://github.com/nyannoying1337/SCSKiller) is an optional external integration. No Intel driver DLLs or Windows debug-helper DLLs are distributed.
