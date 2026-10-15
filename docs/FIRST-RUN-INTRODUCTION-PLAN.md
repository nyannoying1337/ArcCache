# First-run introduction — proposed plan

Status: design proposal, not an implemented welcome wizard. The existing per-game guide remains available.

## Five short screens

| Screen | Plain-language message | Main action |
| --- | --- | --- |
| 1. Welcome | Keep more prepared graphics for a game. Larger caches may reduce repeated shader preparation, but improvements are not guaranteed. Windows keeps its normal driver. | Get started |
| 2. Your graphics | Show the detected Intel chip and check its driver. Explain whether it is ready, needs a separate test, or cannot be changed. GPU selection does not change the chip a game uses. | Check my graphics |
| 3. Choose a game | Show installed Steam games with search and artwork. Explain that single-player is the simplest starting point and show multiplayer/anti-cheat warnings for the chosen title. | Use this game |
| 4. Follow the steps | Launch in DirectX 12, close and set up, launch once for the automatic loading check, close and apply the size. Show only the current instruction with one action button. | Continue setup |
| 5. You're ready to try | Explain Play through Steam, Use default settings, optional statistics, and how to save troubleshooting details. Say that setup success does not prove cache growth or faster gameplay. | Open my game |

## Behaviour

Show welcome once after the app's first successful local start. Store an onboarding schema/version and last completed screen in app settings. Save progress after each completed action and resume after restart. Never treat a skipped screen as consent.

Offer **Skip introduction** on every screen and **Show introduction again** in Settings. Back/close keep progress. A portable-folder move or app update must not repeatedly trigger welcome. Reappearing introduction is limited to an explicit reset or a migration that actually requires a new user decision.

No automatic elevation, Windows changes, downloads or game-file changes happen merely by advancing screens. The Windows routing setting gets a separate explanation, manual administrator launch, explicit confirmation, and restart/resume step. Game setup and unreviewed tests retain their own confirmation dialogs. Unknown or unsupported graphics get a useful result and a route to logs; no dead-end Continue button.

## Acceptance checks

Test first start, skip, back, close/resume, update, portable move, Settings replay, no Intel GPU, multiple GPUs, missing Steam, unsupported driver, failed test, required restart, game already running, anti-cheat warning, and rollback. Keyboard focus must reach the primary action, screen readers must announce changes, and progress must not reset the page or flicker. Technical names and hashes belong in expandable details.

The per-game flow should use [one setup tracker and visible size choices](UX-FLOW-PLAN.md), rather than repeating Your next step banners. This remains a proposed UI revision.
