## Experimental update: DLL inspection and Xe3 device tests

Download **ArcCache-Experimental-Xe3-Preview-2026.10.13-Windows-x64.zip**, extract the whole folder, and run `ArcCache.Experimental.exe`. This unsigned Windows x64 build retains the existing Xe2 presets and adds a separate **Driver → Driver experiments** workflow.

### New workflow

- Scan any selected Intel renderer DLL, or the installed Xe3 DLL. The report identifies structural constructor candidates separately from reviewed patch definitions.
- For the reviewed Xe3 32.0.101.9034 source image, prepare an original 512 MiB or 1/2/4 GiB isolated device-test bundle. No game or system driver is modified. Original bytes/ACL, dependency hashes and results are retained locally.
- Run the owned D3D12 probe after explicit risk acknowledgment. It requires matching private Xe3 loading, device creation and a trivial compute pipeline for a pass. No GPU work is dispatched; a small cache entry may be created.
- Reopen a saved test receipt or inspect its result folder. Active-package drift, changed files and missing consent stop execution. Runs require normal privileges and time out after 60 seconds.

### What is established

28 app/core/worker/bridge checks, 19 catalogue/patch checks and 5 isolated Xe3 constructor checks passed. Known Xe3 original/1/2/4 GiB images have exact output hashes and unchanged bytes outside the reviewed constructor. Original-control bundle preparation and the B580 negative-control device test were exercised; the result correctly remained **runtime not verified**.

**Positive Xe3 device results, per-game Xe3 deployment and effective cache capacity remain unverified.** A matching byte pattern alone does not permit patching unknown images. This build does not apply Xe2's 8/16 GiB reload patch to Xe3. The 8/16 GiB Xe2 presets retain their existing high-RAM/unverified-gameplay limitations.

The one-time private-routing setting must be enabled and Windows restarted for the probe to load private copies. It can affect other `.local` apps machine-wide; no enforcement or anti-cheat is disabled. Dependencies come only from an active Intel package with valid offline embedded trust. Catalog-only/unknown dependencies are refused. The optional SCSKiller bridge remains stock-driver-only.

### Guides

[Xe3 test workflow](https://github.com/nyannoying1337/ArcCache/blob/large-cache-experimental/src/ArcCache.WinUI/docs/XE3-EXPERIMENTS.md) · [Test coverage](https://github.com/nyannoying1337/ArcCache/blob/large-cache-experimental/docs/TESTING.md) · [Private routing](https://github.com/nyannoying1337/ArcCache/blob/large-cache-experimental/src/ArcCache.WinUI/docs/PRIVATE-DRIVERS.md) · [Build instructions](https://github.com/nyannoying1337/ArcCache/blob/large-cache-experimental/docs/DEVELOPMENT.md).

Detailed diagnostics are opt-in under Settings → Troubleshooting or session-only with `--debug`. No automatic upload. Review personal paths before sharing test reports. No Intel or Windows debug-helper DLLs are included in the download.
