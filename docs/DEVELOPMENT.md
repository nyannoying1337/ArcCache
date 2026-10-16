# Build Arc Cache

The complete source is in this repository. Open **ArcCache.slnx** in an IDE with `.slnx` support, or use the commands below. SCSKiller is not required to build the app.

## Requirements

Windows; .NET 10 SDK; Windows SDK 10.0.26100 tooling; the Windows .NET Framework 4.8 compiler. Native research probes additionally require x64 MSVC/MASM. NuGet restores the pinned Windows App SDK dependencies. Players use the self-contained ZIP and do not need an SDK.

## Build the desktop app

From the repository root:

```powershell
dotnet publish src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache
```

The app build compiles the safety worker from `src/ArcCache.Engine` and `src/ArcCache.WinUI/Worker`. The unified preview produces `ArcCache.exe`. Keep the entire output directory, not only the EXE.

## Run checks

```powershell
dotnet run --project src/ArcCache.WinUI/Tests/ArcCache.Tests.csproj -c Release -- out/ArcCache/ArcCache.SafetyEngine.exe
cmd /c src\ArcCache.WinUI\MonitorTests\build.cmd
```

The first command includes read-only installed-driver checks and therefore expects the qualified package in the B580 test configuration. Run it without the worker argument for pure Core fixtures on an unsupported machine. Optional arguments are an existing matched-set receipt and the compiled `ScsBridgeFixture.cs` CLI fixture. Test directories are temporary; no real game/system patch is installed by these checks.

Engine tests and native research optionally require exact qualified original DLLs already on your own machine. Proprietary test inputs are not distributed. See [engine notes](../src/ArcCache.Engine/README.md) and [native test notes](../src/ArcCache.WinUI/NativeTests/README.md).

## Source map

| Folder | Purpose |
| --- | --- |
| `src/ArcCache.WinUI/App` | WinUI 3 XAML, game workflow and settings |
| `src/ArcCache.WinUI/Core` | Launcher discovery, presentation, diagnostics and optional CLI bridge |
| `src/ArcCache.WinUI/Worker` | Compiled safety worker and external read-only overlay |
| `src/ArcCache.Engine` | Pinned patches, file/ACL validation, private sets and rollback |
| `src/ArcCache.WinUI/Tests`, `MonitorTests`, `NativeTests` | Core, worker, safety and native checks |

The repository also has `research/LargeCache` and its reviewed larger-patch sources. This is research evidence, not universal GPU support.

## Everything at once, and releases

`tools/claude-build.ps1` builds and tests everything in one go on a Windows PC with VS 2022 Build Tools (C++, CMake) and the .NET 10 SDK:

- the SCSKiller-Arc fork from a sibling checkout `..\SCSKiller`: native warm exe and proxy (CMake), CLI, filtered tests;
- the engine, the worker, the probe, the app and all app tests;
- `-Release -Version 2026.10.16` additionally publishes the app (Release, x64), adds the SCSKiller CLI and native files under `SCSKiller\`, the license and runtime notices and `GETTING-STARTED.md`, and creates `out\release\ArcCache-<version>-Windows-x64.zip` with a SHA-256 file, plus the Inno Setup installer (`packaging\build-installer.ps1`) when Inno Setup's `ISCC.exe` is installed (otherwise that step is skipped with a warning).

Results go to `tools\claude-build.log`. The script never runs driver tests, game setups or compiles.

The bundled SCSKiller is the fork's `intel-cache-cap` branch. Arc Cache selects it automatically when no other SCSKiller was chosen. For games with a bigger size it requires `scskiller arc-cache-info` to report driver-copy support (see [SCSKiller guide](SCSKILLER.md)).

## Documentation and preview releases

User guides are under `src/ArcCache.WinUI/docs`. README artwork is an original SVG in `docs/assets`; no proprietary DLLs, personal receipts or diagnostic logs belong in Git. Preserve GPL/source attribution and runtime redistribution notices when packaging binaries.

## Native graphics-check helper

The app build compiles `src/ArcCache.Probe/DriverProbe.cpp` through its incremental build target. Install Visual Studio C++ Build Tools with the Windows SDK. The helper is bundled in downloads; users do not need a compiler. `src/ArcCache.Probe/build.cmd` can also build it directly.
