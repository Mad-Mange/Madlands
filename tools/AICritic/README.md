# AICritic: AI review, playtests and fix loops for Madlands

**Claude Code** is the Builder, **OpenAI Codex CLI** the independent Critic and AI Playtester. Codex judges
the game from real evidence (the project's own builds and tests, the built client playing, screenshots,
short frame sequences, logs, performance numbers) and never changes a file. One Python orchestrator runs
every mode:

| Mode | Slash command | CLI | What happens |
|---|---|---|---|
| Review | `/critic` | `review` | Builds a commit (or a snapshot of uncommitted work), plays the scripted scene, Codex reviews the diff. |
| Audit | `/critic-audit` | `audit` | The same for the game as it is, without a diff. |
| Explore | `/critic-explore` | `explore` | Codex plays the game through the Playtest Controller, one validated action at a time, then reviews it. |
| Fix loop | `/critic-loop` | `loop-start`, `loop-review` | The calling Claude session fixes issues in a worktree; Codex reviews each round (at most 3). |
| Report | `/critic-report` | `report` | The latest verdict, scores, issues and screenshots. |
| Terminal loop | none | `run` | The original loop with `claude -p` as builder, for use outside Claude Code. |

Review, audit and explore never change the game. Fix loops work on their own `aicritic/<run>` branch, and a
person decides with `approve` (a commit on that branch, never a merge).

```
 Review / audit / fix-loop round               Explore
 ───────────────────────────────               ──────────────────────────────────────────────
 worktree (commit, snapshot or loop branch)    worktree → playtest client (MADLANDS_PLAYTEST)
  dotnet build → dotnet test → content check    fresh in-memory realm, a new test character
  → Unity client → in-memory realm               ┌────────────────────────────────────────┐
  → scripted shots + bursts → bot load           │ observation + screenshot ──► Codex     │
              │                                  │ explorer picks one allowlisted action  │
              ▼                                  │ ──► validate ──► client executes it ───┘
  evidence copy (hashes, timestamps) outside     └─► final evidence ──► Codex review
  every repository ──► Codex critic (read-only)
              │                                  Scores, verdict, reports: the same for all modes
              ▼
  orchestrator scores ──► PASS / IMPROVE / BLOCKED ──► (fix loop: feedback to the builder)
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
- **IMPROVE**: all five scored, the weighted score is below 8.0.
- **BLOCKED**: there is no defensible score. A criterion is `null`, a required step failed (builder,
  `dotnet build`, `dotnet test`, the content validation, the Unity build, the play session, the playtest
  client), the evidence is older than the round's build, the critic failed or returned an invalid review,
  the critic changed a file, or a budget or time limit ran out. A BLOCKED round never gets a score.

The critic names at most five genuine issues, ranked, each with evidence, reproduction steps and a
concrete fix. Fix loops stop at PASS, at BLOCKED, or after three evaluation rounds.

## Explore: the Playtest Controller

`client/Assets/Madlands/Scripts/Playtest/PlaytestController.cs` lets the orchestrator play the real client.

- **Test builds only.** The whole file, and the small hook in `KeyBindings`, compile only with the
  `MADLANDS_PLAYTEST` scripting define, which `Editor/PlaytestBuild.cs` adds when it builds
  `builds/playtest/Madlands.exe`. Normal builds (`BuildTools`) do not contain it. Even a playtest build does
  nothing unless started with `-playtest <folder>`.
- **File mailbox.** The orchestrator writes `commands/NNNN.json`; the client executes it and answers with
  `observations/NNNN.json` and `screenshots/NNNN.png`; `status.json` is its heartbeat. No network port.
- **Real input.** Movement holds actions through `KeyBindings.SimulatedHeld` (the same path as the keyboard,
  with the game's own physics, prediction and server validation); targeting, fighting, interaction and
  windows use the client's own methods, like `AutoPlay`.
- **Observations**: the player (position, facing, level, health, combat, airborne), the target, the ten
  nearest units and objects (name, kind, level, reaction, health, distance, bearing), loot and dialog
  state, events since the last action (chat and errors, damage, experience, quest progress, disconnects),
  client log errors, the frame rate during the action (average, 5th percentile, worst frame) and whether
  movement got stuck. Moves, walks, jumps, fights and casts also get a short burst of pictures.
- **Honest frame rates.** Pictures are taken with Unity's native `ScreenCapture.CaptureScreenshot` (no
  managed garbage), and the frames around each picture are left out of the statistics. Before that, the
  pictures' own stalls and garbage collection showed up as 13-16 fps "hitches" during fights and walks.

The actions Codex can choose (`orchestrator/playtest.py`, checked again in the client):

| Action | Parameters |
|---|---|
| `wait`, `screenshot` | `seconds` 0.2-10 |
| `move` | `direction` forward/backward/left/right, `seconds` 0.2-6, `sprint` |
| `turn`, `face` | `degrees` -180..180; `yaw` 0-360 |
| `jump` | |
| `camera` | `pitch` 5-80, `distance` 2-40, `yaw_offset` -180..180 |
| `walk_to` | world `x`, `z`, `seconds` 1-30: reports reached, stuck or out of time |
| `goto` | a named location from `scenarios/locations.toml` (a teleport) |
| `target_nearest`, `target` | the nearest attackable creature; a unit or object by exact name |
| `attack` | `seconds` 1-12: face the target, melee, cast the usable spells |
| `cast` | action bar `slot` 1-12 |
| `interact`, `interact_object`, `loot` | talk to or use something by name; the nearest object; the nearest lootable corpse |
| `open_window`, `close_windows` | bags, character, questlog, spellbook, map; Esc |
| `reset` | revive, heal, back to the scenario's start, respawn creatures nearby |

Everything else is rejected before it reaches the client, and the rejection is fed back to Codex. GM
commands (`.level`, `.money`, the teleports behind `goto` and `reset`) come only from scenario files and the
orchestrator, never from Codex. An action counts as executed only when the client's own observation and
screenshot came back; if the client exits or stops answering, the scenario stops and says why.

**Scenarios** (`scenarios/*.toml`): `smoke` (move, turn, jump, camera), `world` (terrain, lighting,
collisions across Hollowmere), `combat` (a level 3 Shardmage against Mire Croakers: targeting, damage,
death, loot), `ui` (bags, character, quest log, spellbook, map, an NPC dialog and a vendor) and
`navigation` (walking out through Mirewatch's gates). `camera` replays the Greywood retreat among the trees
(a Dusk Wolf fight, two seconds backward, the kill, then low camera angles all around; always scripted): the
regression test for trees hiding the player or the target. Each has a goal for the AI explorer, a class, GM setup
lines, a start location and a script for `--mode scripted`. `all` plays world, combat, ui and navigation.

**Developer GM menu** (`docs/GMMenu/README.md`): scenario scripts, never Codex, may also use `gm_command`
(a menu command by its stable id, with arguments, through the client's GM command service, the same path
as the menu), `open_developer_menu`, `press_key` (a real Esc or § key press, optionally with Shift),
`developer_pick_on_map` and `click_map` (real mouse clicks on the world map at a world x, z). Observations
carry the GM state (`gm`), the structured result of a `gm_command` (`gm_result`) and whether the developer
menu, the game menu or a text field is active. The mailbox also gets `gm_commands.json` (every command with
its argument schema) and `gm_catalog.json` (creature and teleport destination ids). `gm_tools` is the
sample: always scripted, never part of `all`, and marked CHEATS ENABLED for the critic, whose prompt says
that an OK answer to a GM command is not evidence that the game works.

## Safety

- **Separate worktrees.** Every mode builds in `../Madlands-aicritic/<run>`, branched from `git.base_ref`
  (review, audit and explore: detached, removed afterwards; fix loops: a new `aicritic/<run>` branch). The
  main checkout and its uncommitted work are never touched. `--snapshot` reviews uncommitted work through a
  commit object without a branch or ref; `--paths` limits it to some files.
- **Local test realm only.** A fresh `--db memory --gm-all` realm on its own ports (7787/7790) with a
  fixed seed and new test characters for every shot and scenario: no real accounts or player data.
- **Codex is read-only.** It runs with `--sandbox read-only`, `--ignore-user-config` (no MCP servers,
  plugins or project trust), `--ephemeral`, without project instructions, in a copy of the evidence outside
  every repository. The orchestrator fingerprints the worktree and the evidence before and after each call
  and discards the review (BLOCKED) if anything changed. `check` verifies that the sandbox blocks writes.
- **No recursive Claude.** `run` starts `claude -p`, so it refuses to run inside a Claude Code session;
  `/critic-loop` uses the calling session as the builder instead.
- **The builder** (in `run`) may only edit files and run a few read-only git commands and `dotnet build`;
  after every round the orchestrator checks that HEAD and the branch did not move and that nothing under
  `tools/AICritic/` or `.github/` changed.
- **Fresh evidence only.** The old client build is deleted before Unity builds; every screenshot must be
  newer than this round's executable. Each evidence file is listed with its SHA-256 in `manifest.json`.
- **Limits.** At most 3 rounds, 40 actions per scenario, a time limit per exploration, a timeout per step
  and per Codex call, dollar caps for the builder (`claude --max-budget-usd`) and a token cap for Codex.
- **Immutable reports.** Each round's `review.json`/`review.md` is written once and made read-only.

## Installation

- Python 3.11 or newer (standard library; **Pillow** is optional and makes the contact sheets and GIFs:
  `pip install pillow`).
- Claude Code CLI (`claude`) and Codex CLI (`npm i -g @openai/codex`), both logged in. On Windows the
  elevated Codex sandbox must be set up once; `critic.windows_sandbox` passes the mode because the user
  config is ignored.
- The project's toolchain: .NET 10 SDK, Unity 6000.6.5f1 (`paths.unity`), Git with Git LFS.
- About 10 GB free per worktree (checkout, Unity Library, builds). The Unity project may stay open in the
  main checkout; its Library is then not used as a seed for new worktrees.

Optional: copy `config.example.toml` to `config.toml` (gitignored) and change what you need.

## Usage

In Claude Code, use the slash commands in `.claude/commands/` (`/critic`, `/critic-audit`,
`/critic-explore`, `/critic-loop`, `/critic-report`). From a terminal:

```powershell
python tools/AICritic/aicritic.py check                                  # validate everything once
python tools/AICritic/aicritic.py audit [--focus "combat feedback"]
python tools/AICritic/aicritic.py review                                 # the last commit against its parent
python tools/AICritic/aicritic.py review --snapshot --paths client/Assets/Madlands/Scripts/UI --task "..."
python tools/AICritic/aicritic.py explore combat                         # AI exploration (default mode ai)
python tools/AICritic/aicritic.py explore all --max-actions 8 --max-minutes 40
python tools/AICritic/aicritic.py explore smoke --mode scripted --no-review   # quick controller check
python tools/AICritic/aicritic.py explore world --reuse-build ..\Madlands-aicritic\<run>   # no new build
python tools/AICritic/aicritic.py loop-start --task "Make enemy nameplates readable" --from-run <run-id>
python tools/AICritic/aicritic.py loop-review <run-id>                   # after editing the loop's worktree
python tools/AICritic/aicritic.py report [<run-id>]
python tools/AICritic/aicritic.py approve <run-id>                       # a person accepts a loop's changes
python tools/AICritic/aicritic.py discard <run-id>                       # removes its worktree and branch
python tools/AICritic/aicritic.py run --task "..."                       # terminal only: claude -p as builder
```

Dry runs make no AI calls: `review`, `audit` and `run` take `--dry-run` (fixture reviews) and
`--dry-run --fake-pipeline` (no worktree, build or game either); `explore --dry-run` plays the scenario
scripts with a fixture review, and `--fake-client` adds a simulated client for testing the tool itself.

Exit codes: 0 PASS, 2 IMPROVE, 3 BLOCKED. The tool's own tests (seconds, no Unity, no AI):
`python -m unittest discover -s tools/AICritic/tests`.

## Output

Everything stays under `tools/AICritic/runs/` (gitignored):

```
runs/
  latest-review.json / .md        the newest round of any run
  checks/<ts>/check.json
  <run-id>/
    run.json                      kind, state, rounds, verdicts, costs, stop reason, approval
    task.md
    latest-review.json / .md      the run's newest round
    round-NN/
      review.json / review.md     read-only: verdict, scores, ranked issues, pipeline, exploration, costs
      builder-prompt.md ...       (run) builder prompt, output and log
      diff.patch  round-diff.patch  diffstat.txt   (review and fix loops)
      feedback.md                 (fix loops, after IMPROVE) what the builder should fix next
      critic/                     the critic's prompt, event stream, raw answer, log
      evidence/
        manifest.json             every file with SHA-256 and time, build identity, metrics, log summaries
        logs/  tests/  screenshots/
        explore/<scenario>/       (explore) commands/, observations/, screenshots/, steps.jsonl, codex/
        findings.json             (explore) what the explorer noticed while playing (leads, not verdicts)
```

The critic's copies of the evidence are in `../Madlands-aicritic/_critic/<run>/`.

## The scripted scene

`scenes/hollowmere_smoke.toml` lists the shots that review, audit and fix loops play: the start area with
the HUD and the debug overlay (fps, ping), an overview framing, a quest dialog, a vendor, a level 3
Shardmage fighting (a 12-frame burst) and a run cycle (an 8-frame burst), followed by 20 bots for 20
seconds so the server's tick-time statistics show it under load.

## Limitations

- **The critic's scores are noisy.** In the first three-round run the weighted score rose from 7.25 to 7.45
  in a round where the builder changed nothing. Treat differences below about 0.5 as noise, and the 8.0
  threshold accordingly: a person still decides.
- When the critic puts every issue outside the task, a fix-loop round has nothing to act on.
- Bursts are 0.25 s apart and explore takes one picture per action: enough for poses, ground contact and
  effects, not for smoothness. The critic is told so and marks a criterion `null` when evidence is missing.
- Performance evidence is the frame rate measured by the client during each action or shown in the debug
  overlay, client peak memory and CPU, and the server's tick times. There is no frame-time capture.
- AI exploration costs about 35k Codex tokens and 30-60 seconds per action; Codex sees one screenshot and
  the observation per step, not a video.
- Creature AI is seeded but timing still varies a little between runs: the scene is the same, the pixels
  are not.
- Client windows open on the desktop while a scene or an exploration plays.
- Windows sometimes refuses a client its socket right after a bot run (WSAENOBUFS, "Could not start the
  network socket"). A shot or scenario whose client never got into the world for that reason is retried
  once (the first attempt's log is kept as `*.attempt1.log` and the report says so); tests are never
  retried, so such a test failure still makes the round BLOCKED.
