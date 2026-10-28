# SCSKiller: prepare shaders into the bigger cache

[SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) compiles a game's shaders **before** you play: it reads the game's shader files and community recordings and replays them through the graphics driver, so the game finds them in the driver's cache instead of compiling them mid-game.

On Intel that cache is limited to 512 MB per game. A normal SCSKiller run therefore keeps only part of a large game's shaders. With Arc Cache, SCSKiller can compile with the **game's own driver copy**, so everything up to the game's new limit (1–16 GB) is kept.

## How to use it

1. Set up the game and choose a cache size. Launch it once and leave it at the menu until Arc Cache confirms that size's driver copy loaded. Gameplay recording is not required for this check.
2. Close the game.
3. In the **Prepare shaders with SCSKiller** card, choose **Prepare shaders…** and confirm.
4. Watch the progress, for example *"16 GB driver copy confirmed · Compiling 41,230 / 94,165 pipelines · cache file 512 MB of 16 GB"*.
5. Start the game and choose **Show on-screen stats** to watch cache use. **Settings → On-screen stats → Smooth updates** enables more frequent graph refreshes. The overlay's traversal row reports the recorder setting, not measured effectiveness.

**Advanced** in the same card:

- **Compile threads:** Automatic, or a fixed number.
- **Lower priority:** keeps the PC usable, but the compile takes longer.
- **Use community recordings:** uses your existing SCSKiller account and settings. Arc Cache never signs in, uploads or changes SCSKiller settings.

## Which SCSKiller is used

The card shows it, for example: *"Using Arc Cache's SCSKiller · 1.2.5-arc.20261016 · can use driver copies"*. Use **Change** to select another `scskiller.exe`, the command-line program, not the desktop app.

| SCSKiller build | Default-size games | Games with a bigger size |
| --- | --- | --- |
| Included with Arc Cache (`SCSKiller\cli`, from the [SCSKiller-Arc fork](https://github.com/nyannoying1337/SCSKiller)) | ✅ | ✅ compiles with the game's driver copy |
| Your supplied SCSKiller CLI selected in Arc Cache | Uses the included public compiler on a local data mirror | Uses the included public compiler and the verified driver copy; the supplied recorder stays local |

The supplied CLI handles its own recorder and settings. Arc Cache's public compiler handles shader preparation through a checked local data mirror. Automatic preparation refuses a compiler that cannot confirm the mirror's data isolation and recorder protection. A signed or unsupported supplied recorder is not adapted. Local compatibility has been checked with a supplied 1.4.1-alpha.2 recorder in Satisfactory and Witcher 3; this does not establish reduced traversal stutters or support for every recorder version. The public bundled recorder is not a substitute for unverified alpha/beta features.

Compatible community recordings or a game-file shader plan can be prepared without making a local gameplay recording. Some versions or effects still need a recording when those sources do not contain the required pipelines.

## Compatibility checks

| Game | Checked configuration | Evidence and limits |
| --- | --- | --- |
| Satisfactory | 16 GB driver copy, locally supplied 1.4.1-alpha.2 recorder and local loader adapter | Driver-copy loading verified; responsive menu confirmed; recorder frame data readable by the overlay. Gameplay stutter reduction was not measured. |
| Witcher 3 v5.01 DX12 | 2 GB driver copy, same supplied recorder and local adapter; bundled XeSS/XeFX libraries preserved | Driver-copy loading and launch verified; shader preparation completed 73,019 jobs with zero failures; recorder frame data readable by the overlay. The observed traversal pool placed no resources, so this is not evidence of traversal-stutter reduction. |

These checks establish compatibility in those configurations, not universal game support, crash-free gameplay, or a measured performance improvement. The overlay's smooth mode is also covered by fixture checks that its fast refresh does not repeat disk samples or CSV writes.

> ⚠️ **Don't compile Arc Cache games from the SCSKiller app itself** (or let its automatic re-compile after driver updates run for them) until SCSKiller supports driver copies officially. That path uses the normal driver and trims the cache to 512 MB. Arc Cache's overlay warns when this happens ("the file shrank").

## What happens during a compile (technical)

- SCSKiller starts a copy of its compile program, renamed to the game's exe name, because Intel keys its cache on the exe file name.
- Arc Cache passes along the game's driver copy (folder, file, SHA-256, size). The compile program links a `<game>.exe.local` folder next to its renamed copy to the game's copy. It's a junction: nothing is copied, and only the link is removed afterwards.
- **Before** the GPU is opened, the compile program checks that Windows loads the driver from the copy. **After** opening the GPU, it checks every Intel D3D12 module and the SHA-256. If anything doesn't match, **nothing is compiled** and your cache is left alone.
- Arc Cache shows the confirmation and links the cache file that grew to the game. The card and the overlay then show its size.

This handoff is a first version. Making it an official, less hacky interface on SCSKiller's side is tracked upstream; see the issue linked from the [README](../README.md).
