# Arc Cache patch and recovery engine

The engine validates exact qualified DLL bytes, constructor encodings, complete private sets, executable profiles and receipt-based recovery. Unknown bytes remain blocked. ACL validation compares owner, group, protection and access rules semantically, including equivalent auto-inheritance flags.

## Per-game workflow

The WinUI safety worker creates a matched private cohort beside the rendering executable only after explicit consent. The global installed driver is not replaced. Known 32-bit bundled-debug-helper conflicts use exact qualified 64-bit Windows helpers inside the private folder; bundled game files are preserved. Original-copy loading must be observed before a patch change. Changed/unowned files stop removal.

## Advanced legacy utility

The separate WinForms `IntelArcCacheCap` source supports inspecting a known Xe2 DLL and creating a backup/patch bundle. Its legacy global replacement requires an explicitly confirmed administrator-launched Safe Mode operation. Offline Windows install is not implemented. Prefer the WinUI private-copy workflow; use Intel's official installer for system recovery. No ownership takeover or enforcement bypass is implemented.

## Tests and attribution

[Compatibility/test matrix](../../docs/TESTING.md) · [Build instructions](../../docs/DEVELOPMENT.md) · [License/attribution](../../NOTICE.md).

Engine tests cover exact patch hashes, normalization, tamper rejection, closed-process gates, temporary install/rollback, semantic ACL equality and owned-file recovery. Original Intel/Windows test inputs stay local and are not shipped.
