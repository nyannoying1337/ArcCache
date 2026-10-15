# Try a new driver without changing a game

Arc Cache can test an eligible driver copy from **Graphics → Check support**. This separate test is available in the unified app; it does not require a second edition.

## What happens

1. Arc Cache checks the selected active Intel graphics file for one exact instruction sequence and valid offline embedded trust. A similar file is a test candidate, not an approved game patch.
2. After explicit risk confirmation, it saves the original file, its SHA-256 and ACL information, and makes a separate copy of the active signed 64-bit dependencies.
3. A separate graphics program tries the **unchanged copy first**. It must use the selected adapter name and load the requested DLL from its own folder. Failure stops the workflow before a patched copy is made.
4. Arc Cache creates a **1 GiB test copy**, changing only the four-byte cache value. It compares all other bytes, checks copied files and source identity, then runs the same graphics check.
5. The progress log and saved reports say whether each copy loaded and prepared one trivial compute shader. A pass does **not** establish actual cache capacity, game stability, or game support.

Unknown versions remain outside normal reviewed setup. After both tests pass, **Choose a game and use this copy** opens the [guided explicit game-trial workflow](GUIDED-GAME-SETUP.md). The graphics test itself only creates isolated artifacts. It cannot enroll its own output as a supported driver, change Windows driver files, bypass signature/integrity enforcement, force a game's GPU preference, or disable anti-cheat. XE3 game support remains unverified.

## Before testing

Close games. Open **Graphics**, choose **Allow game-specific graphics files**, and follow the one-time Windows change and restart instructions. Start Arc Cache manually as an administrator only for that change; run the graphics test normally, without administrator rights. Dependencies must have valid embedded signatures; catalog-only trust is not accepted by this test.

Each graphics process has a **60-second timeout** and a **3 GiB allocated-memory stop**. An unreviewed driver can crash the test or cause a graphics reset; these limits do not guarantee recovery from a GPU/Windows fault. A small shader-cache entry may be written. Allow space for two dependency copies and two backups; the app checks free disk space before copying.

## Results and recovery

**Open test results** shows `%LOCALAPPDATA%\IntelArcCacheCap\driver-trials`. Each copy has `experiment.json`, the backed-up original, its private dependencies and, when run, `device-test.txt`. Preparation failures can leave partial test files; the log names the saved record when available.

**Remove a test copy** asks for that copy's `experiment.json`. It validates the owned folder and its contents. Changed or extra files stop removal. It can remove an old test after a driver update without needing to modify the installed driver. Games and shared shader caches are not removed. If preparation failed before a usable record was saved, inspect that specific test folder manually; the app will not guess what to delete.

## Memory and cache statistics

While Graphics is open, statistics refresh every five seconds without rebuilding the page or rehashing driver files. **PC RAM** is whole-system used/total physical memory. **Intel cache on disk** is the shared Intel cache folder, its file count and net size change since opening the app. Unreadable files are reported. These figures cannot identify a single game's shader data or measure the shader cache's RAM allocation.

## Validation

The source includes automated checks for reviewed Xe2/Xe3 patch bytes, unchanged controls, malformed or ambiguous candidates, unsupported sizes, separation between graphics checks and game approval, progress events, changed or extra files and cleanup of owned paths. Run the commands in [development](../../../docs/DEVELOPMENT.md) to reproduce the checks on Windows.

The 32.0.101.9150 Xe2 candidate path has been inspected, but the driver has not been loaded through this trial workflow and its cache capacity has not been measured. GPU resets, timeout/memory-guard behavior on a real driver, and positive integrated-GPU tests remain unverified.
