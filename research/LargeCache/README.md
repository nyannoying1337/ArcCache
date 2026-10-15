# Arc Cache: larger-cap experimental build

Branch `large-cache-experimental`. The standard version is on this repository’s `main` branch. The native WinUI app and safety worker source are under `src/ArcCache.WinUI` and `src/ArcCache.Engine`.

[Quick start](../../src/ArcCache.WinUI/docs/QUICKSTART.md) · [Private routing explained](../../src/ArcCache.WinUI/docs/PRIVATE-DRIVERS.md) · [Downloads](https://github.com/nyannoying1337/ArcCache/releases)

## Build the app

Install .NET 10 SDK and the Windows SDK, then run from the repository root:

```
dotnet publish src/ArcCache.WinUI/App/ArcCache.WinUI.csproj -c Release -p:Platform=x64 -o out/ArcCache-Large-Experimental
```

The app compiles the x64 safety worker using the installed .NET Framework compiler. Windows App SDK and .NET runtime are published self-contained. The executable is `ArcCache.Experimental.exe`. No Intel or Windows driver DLL is redistributed: a matching installed package is copied locally only after explicit setup consent.

## Try the experimental build

1. Close the game and the older Arc Cache app. Open `ArcCache.Experimental.exe`.
2. Select a game with a working private setup. Choose 8 GiB first and apply after reading the separate experimental consent. If starting fresh, prepare/test the unchanged original copy before applying a limit.
3. Launch DX12 in single-player/offline mode. Click Show live overlay; use borderless/windowed display.
4. Check the loaded driver with the app. The overlay shows observed private/system path, qualified disk cap, private committed process memory, resident memory and shared Intel cache size/net growth. Module path/disk bytes do not prove the in-memory patch or shader capacity. Shared cache growth is not game-attributed.
5. Close the game before reverting. Select 4 GiB in THIS experimental app, or Use default settings. The normal app does not recognize the new experimental images and will fail closed.

Stop the game if memory climbs rapidly or it hangs. Keep the app open or minimized while displaying the overlay; closing the app stops its owned overlay. Exclusive fullscreen/anti-cheat can prevent a desktop overlay from appearing. There is no game-process injection, keyboard hook, elevation, system driver replacement or enforcement bypass.

## What changed in the Intel copy

Only exact original Xe2 driver 32.0.101.9034 is accepted, SHA-256 `1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229`.

8/16 GiB raise the constructor's cap AND separate aggregate append budget. They redirect only the full-cache reload call at RVA 0x17B620 to a new 87-byte assembly loop in an appended PE section. The loop calls the original Intel read helper for at most 256 MiB at a time; the original helper still owns asynchronous completion, offset updates and error behavior. MASM-generated unwind metadata is included; the original exception table is preserved verbatim in a copied table with one additional sorted entry. Windows loader mapping and function lookup were tested without executing the driver.

CRITICAL LIMITATION: this patch preserves Intel's contiguous allocation and parser ownership. Loading a file that reaches 8/16 GiB can request and zero roughly 8/16 GiB of RAM, in addition to game memory and other live buffers. Raising a preset does not allocate the whole limit immediately. The separate 1 MiB bounded-reader prototype is NOT substituted for Intel's parser. Both private copies passed an isolated B580 D3D12 device + trivial compute-pipeline creation probe. No real shader cache growth, large-file shader reload or gameplay above 4 GiB is verified.

Each large-cap change stores the previous/next DLL, original ACL and previous receipt under `%LOCALAPPDATA%/IntelArcCacheCap/large-cache-backups`. Output hashes, exact normalized source image, new code, unwind table, copied exception data, headers and checksum are validated. Changed images or unknown drivers are rejected. The app preserves Windows enforcement; a refusal to load is a failed experiment, not a reason to disable enforcement.

## Native and patch tests

`build-reader-tests.cmd`: real sparse-file bounded-read test (13 checks). Reader buffer 1 MiB, separate expected-data buffer 1 MiB, 8 GiB + 4096 bytes verified; fixture occupies 256 KiB and is deleted.

`build-large-tests.cmd [path-to-qualified-original-DLL]`: MASM wrapper tests, including real Windows exception unwinding. With the optional qualified original, also tests exact patch recognition, tamper rejection, byte-identical rollback, Windows PE mapping/function lookup and deterministic cap changes. Test images stay in ignored bin; they are not installed.

See `LargerCapsResearch.md` for the original tracing evidence. Fixed instruction addresses and hashes apply only to this qualified image.

## Driver updates and GPU families

The app checks active, started display devices through SetupAPI/Configuration Manager, then reads their actual driver registry keys. It does not pick a stale DriverStore folder by filename or modification date. While open, a 30-second metadata timer and window activation compare the driver version, registered package path and DLL/dependency file metadata. No driver bytes are hashed on those idle checks; a measured metadata request took about 109 ms in the B580 test configuration. There is no service or hidden installer.

When identity changes, the app retains the saved private setup but disables applying/launching it until review. Stock recovery remains enabled. Use Check support to perform a read-only current-package review: SHA-256, offline embedded-signature status, executable-section constructor candidates and exact dependency hashes. The last qualified review and explicit compatibility reports are stored under the app's LocalAppData directory. An installer changing the package during review causes rejection/retry. A matching signature on an unknown image never enables patching.

Unknown versions still need a reviewed patch definition. This is not automatic reverse engineering or automatic download of new patch offsets. If an updated driver is unsupported, Use default settings removes the recorded private set and preserves shader caches. Switching an older private copy between limits does not make it compatible with a newer kernel driver.

Xe3 32.0.101.9034 has a reviewed constructor at raw 0x1900E0 / RVA 0x190CE0, independently located from Xe2. The experimental Try a compatibility check page scans DLLs and can create original/1/2/4 GiB isolated device-test bundles for its exact source hash. Matching hardware is required on the testing machine, not the development machine. Per-game Xe3 dependency/recovery profiles and effective cache capacity remain unqualified. [Xe3 experiment workflow](../../src/ArcCache.WinUI/docs/XE3-EXPERIMENTS.md).

No Xe3 driver files are included in downloads. The optional compiled device probe is included in the experimental device-test release. See Xe3ConstructorSamples.cs / Xe3ConstructorProbe.cpp and DriverCatalogTests.cs for reproduction. Do not replace B580's Xe2 DLL with Xe3.

Run `build-driver-review-tests.cmd <qualified Xe2 original> <qualified Xe3 original>` after building the app to reproduce catalogue guards and the isolated Xe3 constructor tests. No driver or game installation is performed by that script.
