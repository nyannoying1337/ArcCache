## UI rules for Arc Cache (apply to every page, every time)
- Every page derives ALL text, badges and button states from ONE state model per object (per game, per driver). Before writing UI, write the state table. No two places on screen may describe the same thing differently.
- Exactly one accent button per page: the next action. Everything else is secondary or a link.
- Never use a disabled button as a status message. Show status as text/badge; show a disabled control only with a one-line reason next to it.
- Hide options that can never work in the current state; mention them in one line instead.
- Max one expander per card, placed at the bottom of the card, named "Advanced" or "Details". Optional features live in a normal card, styled like the others.
- Content max width ~1100px. Text lines max ~80 characters. Buttons sit directly under what they act on.
- One term per concept: "driver copy", "game-specific settings", "cache size", "set up" (verb) / "setup" (noun). Page titles match nav labels exactly.
- The status bar shows only the result of an action from the last few seconds, then clears.
- Multi-step flows are numbered. Completed steps collapse to one line with a check mark. Progress at 100% reads "Done", never "Working...".
