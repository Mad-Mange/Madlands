# AICritic: an AI Builder/Critic loop for Madlands

A standalone tool that lets **Claude Code** (the Builder) implement a task and **OpenAI Codex CLI** (the
Critic) review the result from real evidence: the project's own builds and tests, a scripted play session
in the built game client, screenshots, short frame recordings and logs. The critic's review feeds back to
the builder for up to three rounds. A person decides at the end whether to accept the changes.

It changes nothing in the game. It only drives entry points the project already has: `dotnet build`,
`dotnet test`, `ContentTool validate`, `BuildTools.BuildWindowsFromCommandLine`, the server's
`--db memory --gm-all --seed` options, the client's `-autoconnect -exec -screenshot -screenshotburst ...`
flags and the headless bots.

```
            task / feedback                      evidence (this round only)
 ┌─────────┐  ───────────►  ┌──────────────────────────────────────────────┐
 │ Claude  │                │ worktree on aicritic/<run>                   │
 │ Builder │  edits files   │  dotnet build → dotnet test → content check  │
 └─────────┘  ───────────►  │  → Unity client build → fresh in-memory realm│
      ▲                     │  → client shots + bursts → bot load          │
      │                     └──────────────────────┬───────────────────────┘
      │ ranked in-scope issues                     │ copy (hashes, timestamps)
      │ (IMPROVE only)                             ▼
 ┌────┴──────────────┐   review JSON    ┌─────────────────────────────────┐
 │ orchestrator      │ ◄──────────────  │ Codex Critic: read-only sandbox,│
 │ scores, verdict,  │                  │ no user config, evidence folder │
 │ reports, budgets  │                  │ outside every Git repository    │
 └───────────────────┘                  └─────────────────────────────────┘
```

## Verdicts

The critic scores five criteria from 0 to 10, or marks one **not evaluable** (`null`) when the evidence
cannot support a defensible score. The orchestrator, never the critic, computes the weighted score:

| Criterion | Weight |
|---|---|
| Visual quality and art style | 25% |
| Gameplay | 30% |
| Animation and physics | 20% |
| Bugs and stability | 15% |
| Performance | 10% |

- **PASS**: all five criteria scored and the weighted score is at least 8.0.
- **IMPROVE**: all five scored, the weighted score is below 8.0. The ranked in-scope issues go back to the
  builder for another round.
- **BLOCKED**: there is no defensible score. A criterion is `null`, a required step failed (builder,
  `dotnet build`, `dotnet test`, the content validation, the Unity build, the play session), the evidence
  is older than the round's build, the critic failed or returned an invalid review, the critic changed a
  file, or a budget or time limit ran out. A BLOCKED round never gets a score, and the critic is not even
  asked when a required step failed.

The loop stops at PASS, at BLOCKED, or after three evaluation rounds (`loop.max_rounds`, at most 3).

## Safety

- **Separate branches.** Each run gets a new worktree in `../Madlands-aicritic/<run>` on a new branch
  `aicritic/<run>`, branched from `git.base_ref` (`main`). The main checkout, and whatever other work is
  uncommitted there, is never touched. Nothing is ever merged: `approve` commits on the run's branch, and
  merging stays a manual decision.
- **Manual approval.** Changes stay uncommitted in the worktree until a person runs `approve` and types the
  run id. Dry runs can never be approved.
- **The builder** (`claude -p`) runs in the worktree with `acceptEdits`, file tools and a few read-only git
  and `dotnet build` commands, no MCP servers, and is denied commits, branch switches, pushes, web access
  and subagents. After every call the orchestrator checks that HEAD and the branch did not move and that
  nothing under `tools/AICritic/` or `.github/` changed.
- **The critic** (`codex exec`) runs with `--sandbox read-only`, `--ignore-user-config` (no MCP servers,
  plugins or project trust), `--ephemeral`, without project instructions, in a copy of the evidence outside
  every repository. It never sees the builder or its conversation. The orchestrator fingerprints the
  worktree and the evidence folder before and after the call, and discards the review (BLOCKED) if
  anything changed. `check` verifies that the sandbox really blocks a write.
- **Fresh evidence only.** The old client build is deleted before Unity builds; every screenshot must be
  newer than this round's executable, which must be newer than the round's start. Each evidence file is
  listed with its SHA-256 in `manifest.json`.
- **Limits.** Rounds (≤ 3), a timeout per step, a total run time, a dollar cap per builder call and per run
  (Claude's `--max-budget-usd`), and a token cap for the critic (Codex reports tokens, not dollars).
- Unity rewrites some files while importing. After each build the orchestrator keeps only the `.meta` files
  Unity created for assets the builder added and puts everything else back, so the builder's diff stays clean.

## Installation

- Python 3.11 or newer (only the standard library; **Pillow** is optional and makes contact sheets and GIFs
  of the bursts: `pip install pillow`).
- Claude Code CLI (`claude`), logged in.
- Codex CLI (`npm i -g @openai/codex`), logged in. On Windows the elevated sandbox must be set up once
  (Codex does that on first use); `critic.windows_sandbox` passes the mode because the user config is ignored.
- The project's own toolchain: .NET 10 SDK, Unity 6000.6.5f1 (path in `paths.unity`), Git with Git LFS.
- About 10 GB free per worktree (checkout, Unity Library, builds). The Unity project must not be open in the
  editor *in the worktree*; the main checkout may stay open, but its Library is then not used as a seed.

Optional: copy `config.example.toml` to `config.toml` (gitignored) and change what you need.

## Usage

```powershell
# 1. Validate each part on its own (tools, a tiny Claude call, a tiny Codex call with a sandbox probe,
#    the build pipeline in a fresh worktree, the play session against that build):
python tools/AICritic/aicritic.py check
python tools/AICritic/aicritic.py check --only tools,claude,codex
python tools/AICritic/aicritic.py check --only capture --worktree "..\Madlands-aicritic\check-<ts>"

# 2. Run the loop:
python tools/AICritic/aicritic.py run --task "Color the target frame's level by difficulty" --rounds 1
python tools/AICritic/aicritic.py run --task-file my-task.md          # up to three rounds

# Dry runs: no AI calls. With the real pipeline (worktree, builds, play session):
python tools/AICritic/aicritic.py run --task "pipeline smoke" --dry-run --rounds 1
# Everything fake, a few seconds; --scenario picks the fixture behaviour:
python tools/AICritic/aicritic.py run --task "loop logic" --dry-run --fake-pipeline --scenario build-fail

# 3. Look at the result, then decide:
python tools/AICritic/aicritic.py status [<run-id>]
python tools/AICritic/aicritic.py approve <run-id>     # asks for the run id, commits on aicritic/<run-id>
python tools/AICritic/aicritic.py discard <run-id>     # removes the worktree and the branch, keeps the reports
```

`run` exits with 0 for PASS, 2 for IMPROVE and 3 for BLOCKED. In a terminal it offers the approval right
away; otherwise (or with `--no-prompt`) the changes wait for `approve`.

Tests of the tool itself (seconds, no AI, no Unity):

```powershell
python -m unittest discover -s tools/AICritic/tests
```

## Output

Everything stays under `builds/aicritic/` (gitignored):

```
builds/aicritic/
  latest-review.json / .md        the newest round of any run
  checks/<ts>/check.json          results of `check`
  <run-id>/
    run.json                      state: rounds, verdicts, costs, stop reason, approval
    task.md
    latest-review.json / .md      the run's newest round
    round-NN/
      review.json / review.md     this round's verdict, scores, ranked issues, pipeline, costs
      builder-prompt.md  builder-output.json  builder.log
      diff.patch (cumulative)  round-diff.patch  diffstat.txt
      critic/critic-prompt.md  critic-events.jsonl  critic-review.raw.json  critic.log
      evidence/
        manifest.json             every file with SHA-256 and time, build identity, metrics, log summaries
        logs/                     dotnet, Unity, server, client player logs, bots
        tests/                    TRX files and summary.json
        screenshots/              shots, burst frames, *_sheet.png contact sheets, *.gif recordings
```

The critic's copy of the evidence is in `../Madlands-aicritic/_critic/<run>/round-NN/`.

## The play session

`scenes/hollowmere_smoke.toml` lists the shots. Each is one run of the built client against a fresh
in-memory realm with a fixed seed, with a new character. The default scene covers the start area with the
HUD and the debug overlay (fps, ping), an overview framing, a quest dialog, a vendor, a level 3 Shardmage
fighting (a 12-frame burst) and a run cycle (an 8-frame burst), followed by 20 bots for 20 seconds so the
server's tick-time statistics show it under load. Add shots with any of the client's existing flags.

## Limitations

- **The critic's scores are noisy.** In the first three-round run the weighted score rose from 7.25 to 7.45
  in a round where the builder changed nothing, and two criteria rose a full point after a stylesheet-only
  change. Treat differences below about 0.5 as noise, and the 8.0 threshold accordingly: a person still
  decides with `approve`.
- When the critic puts every issue outside the task, the builder has nothing to act on and the round
  mostly re-measures the same build (it still costs a builder and a critic call).
- Bursts are 0.25 s apart: enough to judge poses, ground contact and effects, not smoothness. The critic
  is told so and marks a criterion `null` when the evidence is not enough.
- Performance evidence is the frame rate in the debug overlay, client peak memory and CPU, and the server's
  tick times. There is no frame-time capture.
- Creature AI is seeded but timing still varies a little between runs: the scene is the same, the pixels
  are not.
- The first Unity build in a worktree without a seeded Library imports every asset and takes a long time.
- Client windows open on the desktop during the play session.
