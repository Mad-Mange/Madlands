Round {round} of {max_rounds}. The orchestrator built and played your previous changes and an independent
critic reviewed the evidence.

Verdict: **{verdict}**. Weighted score: {overall}/10 (a pass needs {pass_score}).

| Criterion | Weight | Score | Confidence | Critic's rationale |
|---|---|---|---|---|
{score_rows}

Critic's summary: {summary}

Task assessment: {task_assessment}

## Issues to address, most important first

{in_scope_issues}

## Other observations (outside the task: change them only if your own change caused them)

{out_of_scope_issues}

Address the issues above, starting with the highest ranked. The original task still applies:

{task}

The same rules apply as before: work only in this worktree, do not commit or switch branches, do not touch
`tools/AICritic/` or `.github/`, follow CLAUDE.md, keep the change focused. The play session and the
critic are fixed: you can only improve the score by improving the game. End with a short summary of what
you changed this round.
