# Arc Cache — clearer controls and Intel graphics selection

The app now describes actions in everyday language. Choose a game, set it up, and choose a cache size. Use **Use default settings** to undo that game. Technical file checks remain available in expandable details and **Recent actions**.

## What changed

- **Graphics** replaces Driver experiments and technical setup wording. **Check support**, **Create a test copy**, and **Run the graphics check** describe the available actions.
- **Enable game-specific settings** explains the one-time Windows setting, restart, and effect of turning it off. Existing consent requirements remain.
- Active built-in and add-in Intel graphics can be selected. This choice does not switch the GPU used by a game. Eligibility depends on the exact reviewed driver files, not the GPU's name.
- Game checks distinguish an observed game-specific driver copy from measured cache growth. Multiplayer and anti-cheat warnings remain explicit; details can be expanded.
- Separate app instances use independent logs. A locked log no longer crashes app actions.

## Support boundaries

Game setups require the reviewed Xe2 32.0.101.9034 package and successful original-copy observation in the game. Unknown versions and older UHD/Iris Xe/XeH files cannot be patched. The preview build also offers an isolated check for the reviewed Xe3 file; this does not enable Xe3 game setups. Integrated-GPU gameplay has not been verified on physical hardware.

Standard offers 512 MiB, 1, 2 and 4 GiB. Preview adds 8 and 16 GiB as test options. Cache-growth and performance improvements remain unmeasured.

## Validation

Standard: 27 core, workflow, read-only worker and bridge checks. Preview: 30. Driver-policy checks: 24, including built-in chip selection, exact package qualification and refusal of changed files or unknown versions. Both Windows x64 builds compile. Game, graphics and settings screens were rendered for visual review.

No game setup or Windows driver file was changed during these checks.
