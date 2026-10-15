# Game libraries

Arc Cache discovers installed games from local records. No launcher account login, cloud library download, ownership check, token extraction or uninstall command execution is performed. Refresh games rescans those records. The source filter and search apply to the combined library.

## Discovery coverage

| Source | Read-only installation records | Scope |
| --- | --- | --- |
| Steam | Library VDF and installed app manifests | Existing Steam IDs, artwork and receipt names retained |
| Epic | ProgramData Epic Games Launcher `.item` manifests | Base games; incomplete installs and DLC excluded |
| Battle.net | Windows uninstall entries and bounded `product.db` install records | Existing installation folders; database entries need `.build.info`; client components excluded |
| GOG | GOG registry game records and matching uninstall entries | Registered local installs |
| Ubisoft Connect | Launcher Installs registry and matching uninstall entries | Registered local installs |
| EA, Riot, Amazon, Rockstar | Known-publisher Windows uninstall entries; EA Games registry entries | Only games with local installation records; client programs excluded |
| Xbox | `XboxGames/<game>/Content/MicrosoftGame.config` on fixed local drives | Accessible unpackaged game folders; no protected WindowsApps enumeration or ACL changes |
| Legendary / Heroic Epic | Standard Legendary `installed.json` and Heroic's Legendary index | Installed base games; other Heroic backends are not imported |
| Manual / other launchers | Explicitly selected AMD64 game `.exe` | Add game works independently of a launcher index |

Automatic discovery does not cover every game or storage layout. Custom Xbox root locations and stores without readable installation records require manual addition. One malformed manifest produces a warning without discarding other sources. Metadata size, nesting, record counts and protobuf field lengths are bounded. XML DTD/entity resolution is disabled.

## Launch actions

Steam and eligible Epic/Ubisoft records can request their normal launcher routes. Numeric GOG IDs can open the game in Galaxy. Known default Battle.net, EA, Ubisoft and Riot clients can be opened; custom client locations fall back to Open game folder. Xbox, Amazon, Rockstar and Legendary/Heroic records without a verified launch route open their game folder so the game can be started through its usual client. These actions do not provide or bypass authentication/DRM.

A manual entry starts only the executable selected by the person adding it, with its directory as working directory and no manifest-derived arguments. Games requiring launcher parameters should be started through that launcher. Importing a launcher shortcut does not establish the actual DX12 renderer; runtime observation is still required. Installation manifest launch/uninstall command strings are never executed.

## Profiles, safety and recovery

Discovery alone never enables patching. The rendering executable must be observed inside the selected installation with D3D12 and the appropriate original Intel module loaded. Multiple renderers remain ambiguous; a manual exact executable can narrow selection. Existing driver checks, original-copy loading, closed-game checks, consent and rollback apply to every source.

Non-Steam games use store-scoped identities based on native installation ID and canonical installation path. They cannot impersonate a Steam app ID. Their profiles and recovery receipts use distinct keys; existing `steam-<id>.json` files remain readable. Folder moves/reinstalls are new installations and do not silently migrate private folders or recovery records. Manual entries with missing executables retain their identity for recovery.

Non-Steam games retain the local anti-cheat scan but do not borrow Steam game-mode metadata. Unknown multiplayer/anti-cheat safety requires explicit risk acknowledgment. Absence of a marker is not proof of safety. No anti-cheat or Windows integrity enforcement is disabled. The SCSKiller bridge remains Steam-only.

## Format references

Installation formats and launcher routes were checked against the primary implementations in [Legendary's Epic manifest model](https://github.com/legendary-gl/legendary/blob/master/legendary/models/egl.py), [Playnite's Battle.net library](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/BattleNetLibrary/BattleNetLibrary.cs), its [install-record field definitions](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/BattleNetLibrary/BattleNetGames.cs), and [Epic launcher routes](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/EpicLibrary/EpicLauncher.cs). Arc Cache uses its own bounded parsers; those projects are not runtime dependencies.

Synthetic fixtures cover metadata parsing, damaged records, identity separation, manual persistence, XML/protobuf bounds and launch-plan construction. Non-Steam safety and discovery have read-only worker checks. These checks do not establish gameplay or private-driver loading for every launcher/title. Positive hardware/game coverage remains listed in [test coverage](TESTING.md).
