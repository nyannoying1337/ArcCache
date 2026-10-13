# Build Arc Cache

The complete source is in this repository. Open **ArcCache.slnx** in an IDE with `.slnx` support, or use the commands below. SCSKiller is not required to build the app.

## Requirements

Windows; .NET 10 SDK; Windows SDK 10.0.26100 tooling; the Windows .NET Framework 4.8 compiler. Native research probes additionally require x64 MSVC/MASM. NuGet restores the pinned Windows App SDK dependencies. Players use the self-contained ZIP and do not need an SDK.

## Build the desktop app

From the repository root:

```powershell
dotnet publish src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache
```

The app build compiles the safety worker from `src/ArcCache.Engine` and `src/ArcCache.WinUI/Worker`. Standard produces `ArcCache.exe`; the experimental branch produces `ArcCache.Experimental.exe`. Keep the entire output directory, not only the EXE.

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
| `src/ArcCache.WinUI/Core` | Steam discovery, presentation, diagnostics and optional CLI bridge |
| `src/ArcCache.WinUI/Worker` | Compiled safety worker and external read-only overlay |
| `src/ArcCache.Engine` | Pinned patches, file/ACL validation, private sets and rollback |
| `src/ArcCache.WinUI/Tests`, `MonitorTests`, `NativeTests` | Core, worker, safety and native checks |

The experimental branch additionally has `research/LargeCache` and its reviewed larger-patch sources. This is research evidence, not universal GPU support.

## Optional CLI packaging

`src/ArcCache.WinUI/build-companion.ps1 -Output <fresh-directory>` builds the app alone. Add `-ScsKillerRepository <separate-source-checkout>` to include a matching SCSKiller CLI/native warmer. The bridge requires its `--community` support. End-user downloads remain app-only; the bridge uses the stock driver and does not apply private caps to compilation.

## Documentation and preview releases

User guides are under `src/ArcCache.WinUI/docs`. README artwork is an original SVG in `docs/assets`; no proprietary DLLs, personal receipts or diagnostic logs belong in Git. Preserve GPL/source attribution and runtime redistribution notices when packaging binaries.

## Xe3 probe (experimental branch)

Build `research/LargeCache/build-xe3-probe.cmd` before publishing to include `ArcCache.Xe3Probe.exe`. MSVC is required for this optional native component. The app itself still builds without it; preparing a device-test bundle then reports the missing component. See [test coverage](TESTING.md).
