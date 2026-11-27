---
description: Show the latest AICritic review - verdict, scores, issues and screenshots
argument-hint: "[run-id]"
---
Show an AICritic review (tools/AICritic). Arguments: $ARGUMENTS

1. Run `python tools/AICritic/aicritic.py report $ARGUMENTS` (without a run id: the newest run).
2. Read the report file it names (`latest-review.md`) and look at the screenshots it lists with Read: the
   contact sheets first.
3. Present the verdict (PASS / IMPROVE / BLOCKED and why), the five scores (null = NOT EVALUATED), the ranked
   issues with reproduction steps and suggested fixes, and for explore runs what was played. Mention a
   pending approval if the report shows one.
