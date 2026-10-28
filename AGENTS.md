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

## SCSKiller publication boundary
- Never commit, push, upload or distribute BlueHeisenberg's unpublished alpha/beta source, binaries, extracted assets or implementations derived from that unpublished source. A local fork or our own public branch does not make upstream material public.
- Public packages may contain only SCSKiller material already published publicly by BlueHeisenberg, plus our changes based on that public material. Verify provenance before publication.
- Users may provide their own alpha/beta locally. Local adapters, CLI hosting, settings and signing must remain on that user's machine; never include their supplied files, credentials, signatures or local host copies in the repo or release payload.
- A local compatibility test using unpublished source is not evidence that the public package supports those features. Clearly identify unsupported combinations; do not promise alpha/beta functionality through a public recorder without testing that public recorder.
