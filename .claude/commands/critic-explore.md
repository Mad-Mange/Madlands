---
description: Launch the game and let Codex explore and playtest it through the Playtest Controller
argument-hint: "<world|combat|ui|navigation|smoke|all>... [--mode ai|scripted] [--max-actions N] [--max-minutes M]"
---
Playtest Madlands with AICritic's explore mode (tools/AICritic). Arguments: $ARGUMENTS (default scenario: world)

1. Run `python tools/AICritic/aicritic.py explore <arguments>` in the background. It builds the playtest
   client (the normal client plus the MADLANDS_PLAYTEST define) from HEAD in a temporary worktree, starts a
   fresh in-memory test realm, logs in a new test character per scenario and lets Codex choose one
   allowlisted action at a time from the screenshot and the controller's observation. A final Codex review
   scores the whole exploration. Client windows open on the desktop while it plays.
   - Scenarios: world, combat, ui, navigation, smoke (minimal), or all (the first four).
   - `--mode scripted` plays each scenario's fixed script instead (deterministic, no explorer calls).
   - `--max-actions N` per scenario (default from the scenario, at most 40); `--max-minutes M` overall.
   - To test uncommitted controller or game changes add `--snapshot --paths <paths>`.
   - Cost: a few minutes to build, then about 30-60 s and 35k Codex tokens per AI action, plus about 80k for
     the final review.
2. When it finishes, run `python tools/AICritic/aicritic.py report`, read the report (its Exploration
   section lists every action, its result and its screenshot) and look at the contact sheets with Read.
3. Tell the user what was played, the verdict and scores (null = NOT EVALUATED), the ranked issues with
   reproduction steps, and anything that stopped the run. Every listed action ran in the real client; never
   describe an action as done when its row says FAILED or REJECTED.
4. Exploring never changes the game. Offer /critic-loop with `--from-run <run-id>` to fix what it found.
