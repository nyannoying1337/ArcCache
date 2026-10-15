# Setup flow and cache-size controls — proposed revision

Status: design proposal, not a claim that the controls below have been built. Keep the existing visual styling; change the hierarchy and behaviour.

## One setup tracker

Replace repeating Your next step messages with a single tracker: **Find game → Prepare copy → Check loading → Ready**. Keep the same card in the same position. Advance its marker and replace only its instruction/action. Collapse it after setup, with Show setup details available. Do not show another instructional banner that asks for the same completed check.

Show a small indeterminate indicator for genuinely ongoing detection/checking, and determinate progress for file-copy work when counts are known. When waiting for a person, say **Waiting for you to start the game in DirectX 12** or **Close the game to apply this size**. Never spin indefinitely without explaining what is awaited. Keep navigation and the rest of the page stable.

## Make size selection the main control after setup

Use one mutually exclusive group of visible choices: **Default · 1 GiB · 2 GiB · 4 GiB · 8 GiB · 16 GiB**. Do not use separate on/off switches: only one limit can be selected. Keep one contextual primary action, such as **Set up this game**, **Apply 4 GiB**, or **Play through Steam**. Keep **Use default settings** as the consistent undo action.

Show **1 GiB set** for installed files; reserve **Loaded by the game** for an actual module observation. Never turn those statuses into claims about measured cache capacity or performance.

Unavailable choices remain visible with a short reason beside the group and accessible on keyboard focus: **Needs the preview version**, **Not supported by this driver yet**, or **Complete setup first**. 8/16 GiB keep a clear memory-risk label and confirmation. A generic Experimental toggle cannot supply a missing patch definition.

## Current technical boundary

The reviewed 9034 Xe2 workflow offers 1/2/4 GiB in standard and 8/16 GiB in preview. The 9150 pattern-based game trial currently offers 1 GiB. The new pinned Xe1 test patch offers 1 GiB only. Enlarging those latter ranges requires new patch work and validation; this proposal does not hide that behind a UI toggle.

## Connection to first-run introduction

The welcome screens explain the concepts once. They hand off to this single setup tracker for the chosen game. Later visits go directly to size selection and setup status; the introduction does not replay after every update or game selection.

## Across Intel families

The intended size choices apply to every eligible Intel family, not just B-series. See [coverage and larger-limit implementation requirements](INTEL-GRAPHICS-COVERAGE.md). Show 8/16 GiB with a highly experimental memory-risk confirmation once the selected image has its own verified implementation.
