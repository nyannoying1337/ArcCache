# Arc Cache

A standalone Windows WinUI 3 app for reviewed Intel Arc per-game shader-cache limits. **[Download previews](https://github.com/nyannoying1337/ArcCache/releases)** · [Quick start](tools/intel-cache-cap-winui/docs/QUICKSTART.md) · [Private drivers explained](tools/intel-cache-cap-winui/docs/PRIVATE-DRIVERS.md) · [Troubleshooting logs](tools/intel-cache-cap-winui/docs/TROUBLESHOOTING.md).

| Version | Branch | Presets |
| --- | --- | --- |
| Standard preview | [main](https://github.com/nyannoying1337/ArcCache/tree/main) | Original 512 MiB and 1/2/4 GiB |
| Large-cache experimental preview | [large-cache-experimental](https://github.com/nyannoying1337/ArcCache/tree/large-cache-experimental) | Also 8/16 GiB, active-driver update review and expanded overlay |

This checkout is the **standard** version. Both are unsigned previews. Only the exact qualified B580 Xe2 driver 32.0.101.9034 package is accepted; unknown versions stay blocked, and Xe3 remains research-only. General game compatibility and cache-capacity effects are unmeasured. 8/16 GiB gameplay/reload is unverified and large caches can consume roughly their size in contiguous RAM.

## Player workflow

Extract the entire release ZIP. Run `ArcCache.exe`. Enable Use private drivers once with manual administrator launch, explicit confirmation, and a Windows restart. Select a Steam game, let automatic DX12 detection identify it, prepare/observe its unchanged private copy, then close the game and apply a preset. The system driver stays original. The machine-wide setting can affect other apps with `.local` setups; signing/integrity enforcement and anti-cheat are never disabled. Return one game to stock with Use system driver again.

Settings → Troubleshooting offers opt-in local bounded diagnostics and Open logs folder. `--debug` enables session-only logging. Review personal paths before sharing. The app can be closed after setup unless using its external overlay. No Python or PowerShell commands are needed by players.

## Build and test

Requires Windows, .NET 10 SDK, Windows SDK 10.0.26100 tooling and Windows' .NET Framework 4.8 compiler. From the repository root:

```powershell
dotnet publish tools/intel-cache-cap-winui/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache
dotnet run --project tools/intel-cache-cap-winui/Tests/ArcCache.Tests.csproj -c Release -- out/ArcCache/ArcCache.SafetyEngine.exe
cmd /c tools\intel-cache-cap-winui\MonitorTests\build.cmd
```

Additional engine/native tests optionally use qualified original DLLs already on your PC; no proprietary fixtures are shipped. See [app build/research notes](tools/intel-cache-cap-winui/README.md). Ship the complete published folder, including runtime files/licenses, not just the EXE.

## Optional SCSKiller integration

Arc Cache builds and runs without the SCSKiller source tree. The optional bridge uses a separately installed CLI with `--community` support, chosen in Settings. Compilation is stock-driver-only; private-cap warming and simultaneous recorder compatibility are unresolved. Existing account/community access remains external. The companion packaging script builds only Arc Cache by default; pass `-ScsKillerRepository <separate-source-checkout>` to additionally package a matching CLI/native warmer. [SCSKiller fork with the opt-in CLI source](https://github.com/nyannoying1337/SCSKiller/tree/intel-cache-cap).

GPL-3.0-or-later. See [LICENSE](LICENSE) and [source attribution](NOTICE.md).
