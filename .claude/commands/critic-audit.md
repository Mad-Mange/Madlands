---
description: Audit the game as it is with AICritic's independent Codex critic (no recent changes needed)
argument-hint: "[--focus \"what to look at\"] [--ref <commit>]"
---
Audit Madlands with AICritic (tools/AICritic). Arguments: $ARGUMENTS

1. Run `python tools/AICritic/aicritic.py audit <arguments>` in the background. It builds HEAD (or `--ref`)
   in a temporary worktree, runs the tests, plays the scripted scene (village, quest dialog, vendor, combat
   and running bursts, a bot load) and asks Codex for a review (8-12 minutes, about 80k Codex tokens).
   `--focus "..."` narrows what the critic should pay attention to.
2. When it finishes, run `python tools/AICritic/aicritic.py report`, read the report file it names and look
   at the key screenshots with Read.
3. Tell the user the verdict (PASS / IMPROVE / BLOCKED), the five scores (null = NOT EVALUATED) and the at
   most five ranked issues with reproduction steps and suggested fixes.
4. An audit never changes the game. Offer /critic-loop with `--from-run <run-id>` if the user wants the
   issues fixed.
