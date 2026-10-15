# Game setup

## First start

Arc Cache shows a five-screen introduction covering graphics checks, game selection, setup, size selection and recovery. Next and Back navigate; Skip introduction dismisses it. The choice is saved in LocalAppData and shared between editions. Settings → Show introduction reopens it. The introduction does not change Windows, drivers or game files. Check my graphics opens the Graphics page without automatically running a patch.

## Set up a game

1. Open Graphics, select the Intel chip used by the game and choose Check support. When a separate test is required, choose Try patching and review its confirmation. Both original and patched checks must pass.
2. Select an installed game or add an executable manually. Enable game-specific settings in Settings if required; that separate Windows operation needs manual administrator launch, confirmation and restart.
3. Start the game in DirectX 12 with Arc Cache open for detection. Close the game before Set up this game creates an unchanged copy.
4. Start the game again. Arc Cache checks its loaded original copy automatically while open. Close the game after that check.
5. Choose a supported size and apply it after confirmation. Play normally through its usual launcher. Use default settings removes the recorded setup when the game is closed.

## Status and size controls

A single setup card shows the current action and stage. Waiting for a game is labelled explicitly. The card disappears once a configured custom size is ready; its status remains in the game header. Size buttons show Default, 1, 2, 4, 8 and 16 GiB. Only one can be selected. Unavailable choices stay visible and disabled with an explanation beside them. The unified preview offers up to 16 GiB on its reviewed Xe2 path, with explicit experimental/RAM confirmation above 4 GiB. The separate game-trial path currently offers Default and 1 GiB.

Busy indicators run during foreground operations. Background discovery does not clear the page or enter global busy mode. Setup-file verification, observed loaded module and effective cache capacity are separate results; a set limit does not prove cache growth or performance.

## Verification and recovery

Game trials require a successful graphics-test receipt from the active package, pinned original/patched hashes, the same native helper and validated signed dependencies. Known previous helper hashes are accepted only for their original Xe2/Xe3 proofs. The actual DX12 executable must be observed under the selected installation. Original-copy loading is required before applying the trial size. Driver, executable or tracked-file changes stop affected operations.

Setup, changes and removal require explicit confirmation and a closed game. Multiplayer/anti-cheat warnings require a separate acknowledgment. Recovery validates receipt membership and owned-file hashes before removal; unrelated files are not silently deleted. Shader caches are retained. Matching source/dependency checks, path bounds and Windows integrity enforcement remain in place.
