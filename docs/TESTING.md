# Test coverage and compatibility

Tests establish the behavior listed here. They do not establish support for every game or an increase in effective shader-cache capacity.

| Area | Checked | Not established |
| --- | --- | --- |
| Steam discovery | Local library fixtures, executable paths/hashes, DX12 process/module observation | Every launcher, executable layout or game update |
| Xe2 patches | Original/1/2/4 GiB image hashes, constructor state and exact normalization/rollback | Actual capacity, eviction or game-specific overrides |
| Private game loading | Witcher 3 single-player gameplay; Ready or Not menu loading with repaired original, 1 and 4 GiB sets | Universal compatibility, multiplayer/anti-cheat acceptance |
| Recovery | Consent gates, receipt/hash validation, temporary transactions, owner/group/DACL preservation and semantically equivalent ACLs | Recovery after arbitrary untracked modifications |
| 8/16 GiB Xe2 experiments | Bounded wrapper, Windows unwind metadata, PE mapping, deterministic hashes, isolated B580 device/pipeline creation | Gameplay or reload with a cache larger than 4 GiB; low-memory parsing |

## Xe3

The reviewed Xe3 32.0.101.9034 DLL has one independently located cache constructor. Original/1/2/4 GiB copies are checked by full image hashes and preservation of bytes outside that constructor. Isolated constructor tests use a stub for the external helper; they are not graphics-driver loading tests.

The experimental build offers an **isolated device-test bundle** for that reviewed image. It copies the active package into a private folder for its own probe, backs up the original Xe3 DLL/ACL, validates the copied files and requires explicit consent before running. No game or system driver is replaced. A successful probe records private Xe3 module observation, D3D12 device creation and a trivial compute-pipeline creation. No GPU work is dispatched. A small shader-cache entry may be created.

Xe3 hardware is required on the tester's machine, not the development machine. Runtime/game compatibility and effective capacity are not yet established. Xe3 per-game profiles remain unqualified; Xe2's 8/16 GiB reload patch must not be applied to Xe3.

The isolated original-control preparation and execution were exercised against the active B580 package. All copied dependencies and source identities verified; the probe correctly returned **runtime not verified** because it did not observe private Xe3 loading. This is a negative control, not an Xe3 runtime success.

## Automatic DLL assessment

The experimental scanner inspects the selected DLL rather than guessing from GPU branding. It reports SHA-256, version, executable-section constructor matches and offline embedded-signature status. One signature match is a structural candidate. Multiple matches are ambiguous. No match means this build has no applicable constructor pattern.

Unknown image hashes can be assessed, but cannot be patched merely because a pattern matches. A reviewed patch definition validates the full source image and exact output bytes. A device-test result is separate from a qualified per-game profile.

## Reproduce checks

See [build/test commands](DEVELOPMENT.md). Engine/native fixtures use local qualified originals, never redistributed driver files. Core/safety fixtures use temporary directories. Full worker checks may inspect the installed package read-only; fixture checks do not install a real game/system patch.

## Built-in chip selection and plain-language workflow

The preview enumerates active Intel devices and retain a user-selected device identity. Policy fixtures verify built-in selection, available-device fallback and byte-qualified eligibility independent of model names. Changed files and unknown versions remain blocked. Primary UI states retain saved sizes during driver/routing changes and distinguish loaded-copy observations from unmeasured performance. Integrated-hardware gameplay has not been verified.

## Introduction and setup controls

Introduction preferences are tested for first start, dismissal persistence and invalid preference recovery. The preview builds with the five-screen introduction, a single setup guide and visible size buttons. Read-only UI previews cover the welcome panel and game page. Existing Core/worker checks cover setup stages, missing receipts, closed-game checks, unsupported-driver refusal and consent gates. Hardware and cache-capacity limits above remain unchanged.

## Local libraries and manual games

Fixtures cover Epic base-game/incomplete/DLC filtering, Legendary indexes, Battle.net registry and protobuf records, registered publisher classification, Xbox XML/DTD rejection, duplicate identities, malformed records, manual add/remove persistence and missing-executable recovery identity. Store-scoped keys preserve legacy Steam filenames and reject a non-Steam profile impersonating a Steam ID. Seven backend profile checks cover bounded executable paths and legacy compatibility without writing real profiles.

Worker checks verify that non-Steam safety stays unknown without Steam category data and that read-only discovery never pins a stopped game's profile. Launch plans are constructed without executing manifest arguments or uninstall commands. Actual authentication, game launching and private-driver loading across every third-party launcher are not established by these fixtures. The launcher coverage table describes installation-record support, not universal game compatibility.

The unified library preview passes 45 Core/worker checks and seven standalone backend identity/profile checks. A read-only local discovery/render check found Steam, Ubisoft, Xbox-folder and EA records with unique keys; no game or driver setup was installed by that check. Native helper and app builds passed.
