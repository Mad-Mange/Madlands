---
description: Review the latest implementation changes with AICritic's independent Codex critic
argument-hint: "[--snapshot [--paths <path>...]] [--ref <commit>] [--task \"what the change does\"]"
---
Review the latest Madlands changes with AICritic (tools/AICritic). Arguments: $ARGUMENTS

1. Decide what to review. Without arguments, review the last commit (`--ref HEAD`, compared with its
   parent). For uncommitted work use `--snapshot`, and when other uncommitted work is in the tree (Codex may
   be working in parallel) limit it with `--paths` to the files this session changed. Add `--task` with a
   one-line description of what the change is meant to do when you know it.
2. Run `python tools/AICritic/aicritic.py review <arguments>` in the background. It builds the game in a
   temporary worktree, runs the tests, plays the scripted scene and asks Codex (8-12 minutes, about 80k
   Codex tokens). Exit code 0 = PASS, 2 = IMPROVE, 3 = BLOCKED.
3. When it finishes, run `python tools/AICritic/aicritic.py report`, read the report file it names and look
   at the most relevant screenshots it lists with Read before you describe them.
4. Tell the user the verdict, the five scores (null = NOT EVALUATED), and the ranked issues with their
   reproduction steps and suggested fixes. Say plainly when the result is BLOCKED and why.
5. Do not change game files because of the review unless the user asks; /critic-loop is for fixing.
