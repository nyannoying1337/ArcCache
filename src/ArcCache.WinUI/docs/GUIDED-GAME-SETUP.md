# Guided game setup after a successful driver check

Both versions now connect **Your test copy worked → Choose a game and use this copy → Your next step**. Successful saved tests are rechecked when the app opens; they do not have to be repeated just to continue to a game.

## The app walks through four steps

1. **Start the game** in DirectX 12 with its usual driver. Keep Arc Cache open. It identifies one rendering executable inside the selected Steam installation and checks which Intel module it actually loaded.
2. **Close the game and set it up.** Confirm the change. Arc Cache copies the unchanged driver and matching files for this game. Other games and the installed Windows driver stay unchanged.
3. **Start the game once.** Keep Arc Cache open while it checks that the game's own unchanged copy loaded. Close the game when the app says that check finished.
4. **Apply the selected size**, then use **Play through Steam**. An unreviewed game trial offers 1 GiB only. Use **Use default settings** to undo this game's setup.

The guide points to Settings when the one-time Windows setting is missing, recovery when setup files changed, and default settings when a game has an older driver copy. A passed driver check does not cause repeated Check support prompts for an older game copy: the next action is to refresh that game's setup.

## Two kinds of setup

**Reviewed setup:** the existing pinned Xe2 package uses its reviewed presets.

**Explicit game trial:** a new Xe2/Xe3 package can be tried with a game only after the unchanged and 1 GiB isolated graphics tests pass for that same source. The app checks the active chip/package identity, original and patched file hashes, backups, the shipped test helper and signed dependencies. It requires fresh risk confirmation for setup and changes, plus a separate unchanged-copy loading check in the game before applying 1 GiB. Passing this path does not turn the package into a reviewed driver definition or enable larger limits.

Game trials use a schema-5 setup record with the exact original dependency hashes, tested driver name, original/1 GiB hashes, game executable hash and successful test record. They cannot silently enter the normal Change action. Added or changed files stop rollback. Rollback can remove a recorded game trial after a driver update or loss of the test proof; it preserves the game executable and shader cache. Test copies cannot be removed while a game still depends on that source. Keep the driver test folders until game setups are undone.

The app does not switch a game's GPU preference, disable anti-cheat, bypass Windows signature/integrity enforcement, elevate itself, or change games in the background. Multiplayer and anti-cheat warnings still require acknowledgment. Driver updates or missing/changed test files stop preparation, changes and app-controlled custom launches.

## What ready means

**Ready to set up** means the separate test passed and its files still match. **Ready to try this game** means the chosen files were installed and checked, including the game's unchanged-copy loading check. Neither statement proves cache growth, long-term game stability, or better performance.

## Validation

Both builds compile. Standard passes 32 core/workflow/worker/bridge/progress checks; preview passes 35. Twenty-three byte-patch, game-trial receipt and recovery checks cover unchanged controls, changed/ambiguous signatures, size limits, package membership, path boundaries, consent and rollback that preserves the game executable.

The saved B580 32.0.101.9150 unchanged/1 GiB tests passed previously. A full read-only check of those tests and their active signed dependencies passed during this update. Graphics and game screens were rendered for visual review. No real game setup was created, removed or changed during development. The new per-game 9150 workflow still needs a physical game run; positive Xe3/iGPU game results and cache capacity remain unmeasured.
