---
description: Fix issues in a Builder/Critic loop - this session builds, Codex reviews, at most 3 rounds
argument-hint: "<task> [--from-run <audit/explore/review run id>]"
---
Run AICritic's fix loop with this session as the Builder and Codex as the independent Critic.
Arguments: $ARGUMENTS

Never run `aicritic.py run` from here: it starts another Claude. This loop uses you as the builder.

1. Start the loop: `python tools/AICritic/aicritic.py loop-start --task "<task in English>" [--from-run <id>]`.
   `--from-run` seeds the task with the verified issues of an earlier audit, explore or review run. It prints
   the run id, a new worktree on a new `aicritic/<run>` branch, and the task file. Read the task file.
2. Make the changes only inside that worktree (use its absolute paths), following CLAUDE.md. Do not touch
   the main checkout, do not commit or switch branches in the worktree, and do not edit `tools/AICritic/` or
   `.github/` there. Fix only issues the evidence supports; keep the change focused.
3. Run `python tools/AICritic/aicritic.py loop-review <run-id>` in the background (8-12 minutes): it builds,
   tests and plays the worktree and asks Codex.
4. On IMPROVE, read the round's `feedback.md` it names, fix the in-scope issues in the worktree, and run
   loop-review again. The loop ends at PASS, at BLOCKED, or after three evaluation rounds (loop-review
   enforces the limit).
5. Report the outcome with `python tools/AICritic/aicritic.py report <run-id>`. Never approve, merge or push
   on your own: accepting the changes (`python tools/AICritic/aicritic.py approve <run-id>`, a commit on the
   run's branch) and merging into main are the user's decisions.
