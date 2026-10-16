# SCSKiller: prepare shaders into the bigger cache

[SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) compiles a game's shaders **before** you play: it reads the game's shader files and community recordings and replays them through the graphics driver, so the game finds them in the driver's cache instead of compiling them mid-game.

On Intel that cache is limited to 512 MB per game. A normal SCSKiller run therefore keeps only part of a large game's shaders. With Arc Cache, SCSKiller can compile with the **game's own driver copy**, so everything up to the game's new limit (1–16 GB) is kept.

## How to use it

1. Set up the game in Arc Cache and apply a size until the card shows **"Ready to play · N GB"**.
2. Close the game.
3. In the **Prepare shaders with SCSKiller** card, choose **Prepare shaders…** and confirm.
4. Watch the progress, for example *"16 GB driver copy confirmed · Compiling 41,230 / 94,165 pipelines · cache file 512 MB of 16 GB"*.
5. Start the game. With **Show on-screen stats**, the overlay should say **"Cache use: GOOD"**.

**Advanced** in the same card:

- **Compile threads:** Automatic, or a fixed number.
- **Lower priority:** keeps the PC usable, but the compile takes longer.
- **Use community recordings:** uses your existing SCSKiller account and settings. Arc Cache never signs in, uploads or changes SCSKiller settings.

## Which SCSKiller is used

The card shows it, for example: *"Using Arc Cache's SCSKiller · 1.2.5-arc.20261016 · can use driver copies"*. Use **Change** to select another `scskiller.exe`, the command-line program, not the desktop app.

| SCSKiller build | Default-size games | Games with a bigger size |
| --- | --- | --- |
| Included with Arc Cache (`SCSKiller\cli`, from the [SCSKiller-Arc fork](https://github.com/nyannoying1337/SCSKiller)) | ✅ | ✅ compiles with the game's driver copy |
| Official SCSKiller release, portable or beta | ✅ | ⛔ blocked |

Official builds are blocked for bigger sizes on purpose. They would compile with the normal driver, which **shrinks the game's cache back to 512 MB**.

> ⚠️ **Don't compile Arc Cache games from the SCSKiller app itself** (or let its automatic re-compile after driver updates run for them) until SCSKiller supports driver copies officially. That path uses the normal driver and trims the cache to 512 MB. Arc Cache's overlay warns when this happens ("the file shrank").

## What happens during a compile (technical)

- SCSKiller starts a copy of its compile program, renamed to the game's exe name, because Intel keys its cache on the exe file name.
- Arc Cache passes along the game's driver copy (folder, file, SHA-256, size). The compile program links a `<game>.exe.local` folder next to its renamed copy to the game's copy. It's a junction: nothing is copied, and only the link is removed afterwards.
- **Before** the GPU is opened, the compile program checks that Windows loads the driver from the copy. **After** opening the GPU, it checks every Intel D3D12 module and the SHA-256. If anything doesn't match, **nothing is compiled** and your cache is left alone.
- Arc Cache shows the confirmation and links the cache file that grew to the game. The card and the overlay then show its size.

This handoff is a first version. Making it an official, less hacky interface on SCSKiller's side is tracked upstream; see the issue linked from the [README](../README.md).
