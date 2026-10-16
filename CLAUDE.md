# Madlands – notes for AI sessions

WoW-like MMORPG. Unity 6 client (URP) + authoritative .NET 10 server + shared C# code.
**Language:** the user chats in Swedish — reply in Swedish. But the **entire project is English**: code,
comments, in-game UI text, docs, README, commit messages, GitHub description, file names, lore. Never write
Swedish into repo files (non-ASCII *test data* like "Räksmörgås" in ProtocolTests is intentional).
Roadmap and phase status: `docs/roadmap.md`. Architecture and the lock-in decisions: `docs/architecture.md`, `docs/adr/`.

## Layout

| Path | What |
|---|---|
| `shared/Madlands.Shared` | Protocol, movement rules, ids, height map format. Compiled by dotnet AND Unity (local UPM package). |
| `shared/Madlands.ClientCore` | Engine-free client logic (GameClient, ClockSync, InterpolationBuffer, MovementSender). Used by Unity client and bots. |
| `server/Madlands.Server` | One realm process (`ServerHost`): UDP game server (20 Hz loop, map instances, AoI, movement validation), HTTP account API (`Api/`), persistence (`Persistence/`: Postgres + in-memory, DbUp migrations, write-behind `CharacterSaver`), Argon2id auth (`Auth/`). |
| `server/Madlands.Content` | Loads + validates `content/` (JSON + binary). Server/tools only. |
| `server/Madlands.Bots` | Headless bot players (register/login via the API, then play) for load/integration testing. |
| `server/Madlands.Server.Tests` | xUnit: protocol roundtrips, world logic, client core, real HTTP+UDP realm tests (`TestRealm`), Postgres tests (`[PostgresFact]`, skipped if no DB). |
| `tools/Madlands.ContentTool` | `validate` content (later: compile content, build navmeshes). |
| `client/` | Unity project. Our code: `client/Assets/Madlands/{Scripts,Editor,UI}`. Scenes: `FrontEnd` (boot, UI, camera, networking) + `World_NNNN` map scenes loaded additively by mapId. |
| `content/` | Game data: `races.json`, `classes.json`, `maps/<id>/` (written by Unity: Madlands > Export Map). |

## Commands (PowerShell; dotnet may need PATH refresh in old shells)

```powershell
docker compose -f deploy/docker-compose.yml up -d   # PostgreSQL (+ Seq) for the server and the Postgres tests
dotnet build Madlands.slnx                      # all .NET projects (output in /artifacts)
dotnet test Madlands.slnx                       # 72+ tests incl. real HTTP+UDP realm and Postgres
dotnet run --project server/Madlands.Server -- --bind 127.0.0.1                    # UDP 7777 + HTTP 7780, PostgreSQL
dotnet run --project server/Madlands.Server -- --bind 127.0.0.1 --db memory        # no Docker; data lost on exit
dotnet run --project server/Madlands.Bots -- --count 200 --seconds 60   # server needs --api-rate-limit 0 for big runs
dotnet run --project tools/Madlands.ContentTool -- validate

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
# Each: & $unity -batchmode -projectPath client -logFile <log> -executeMethod <method>   (no -quit; methods call Exit)
#   Madlands.Client.Editor.BuildTools.CompileCheck                 -> exit 0 if all scripts compile
#   Madlands.Client.Editor.BuildTools.BuildWindowsFromCommandLine  -> builds/client/Madlands.exe
#   Madlands.Client.Editor.BuildTools.CaptureWorldScreenshot       -> builds/screenshots/editor_*.png
#   Madlands.Client.Editor.TestWorldBuilder.BuildFromCommandLine   -> regenerates World_0001 + exports map
#   Madlands.Client.Editor.FrontEndBuilder.BuildFromCommandLine    -> regenerates FrontEnd scene + PanelSettings
#   Madlands.Client.Editor.MapExporter.ExportFromCommandLine       -> content/maps/0001/{height.bin,map.json}
# Unity cannot run batchmode while the user has the project open in the editor.

# Visual end-to-end checks (server running). -autoconnect logs in as the lowercased -name with the dev
# password, creating account and character if needed:
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Tester -screenshot builds/screenshots/world.png -screenshotdelay 7 -quitafter 13
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Tester -stopat charselect -screenshot builds/screenshots/chars.png -screenshotdelay 6 -quitafter 8
builds/client/Madlands.exe -screen-fullscreen 0 -screenshot builds/screenshots/login.png -screenshotdelay 4 -quitafter 6
```
Then read the PNG to verify. Use `-logFile` for the player log.

## Gotchas learned the hard way

- Setting `UIDocument.panelSettings` from an editor script is NOT persisted: set the serialized field
  `m_PanelSettings` via `SerializedObject` (FrontEndBuilder does). Symptom: UI invisible in builds, no errors.
- Unity has its own `UnityEngine.CharacterInfo`; avoid type names that clash with UnityEngine in Shared.
- `ScreenCapture.CaptureScreenshot` does include UI Toolkit; if UI is missing, the UI is really not rendering.
- Windows: a Git Credential Manager prompt cannot be shown from here; push with
  `git -c credential.helper= -c "credential.helper=!gh auth git-credential" push` (or the user runs `gh auth setup-git`).

## Rules

- **Shared/ClientCore must compile in Unity**: netstandard2.1, C# 9, block-scoped namespaces, no records/init/
  file-scoped namespaces/global usings/nullable annotations, no NuGet deps in Shared. The asmdefs use
  `noEngineReferences: true`, so UnityEngine can never leak into shared code.
- **Any wire or shared-rule change bumps `ProtocolVersion.Current`.** Never renumber opcodes. Every message
  gets a roundtrip test in `ProtocolTests.cs`.
- **Server is authoritative.** Clients only predict their own movement; the server validates
  (`MovementValidator`) and corrects. Never trust client data; reject NaN; rate-limit every message type.
- **Content ids are stable and never reused.** Content is data (`content/`), validated by `ContentLoader`
  (collect all errors, then fail). The server sends `displayId`s, never asset names.
- Entities live in `(mapId, instanceId)`; map instances share no mutable state (future: one thread/process each).
- Coordinates: meters, +Y up, yaw 0 = +Z, same as Unity. Maps within +-8192 m.
- Keep LiteNetLib versions in sync: `shared/Madlands.ClientCore/*.csproj` and `server/Madlands.Server/*.csproj`.
  Building ClientCore copies `LiteNetLib.dll` into `shared/Madlands.ClientCore/Plugins/` for Unity.
- `Directory.Build.props` routes bin/obj to `/artifacts` — required, or Unity imports obj/*.cs from the shared packages.
- Unity writes `.meta` files into `shared/*`; commit them.
- Binary assets go through Git LFS (`.gitattributes`), except `docs/images/` (plain git so README images render).
- **The GitHub repo (Mad-Mange/Madlands) is PUBLIC.** Never commit paid/licensed asset packs (Synty etc.):
  they go in `client/Assets/ThirdParty/Paid/`, which is gitignored. Only free/CC0 or own assets in git.
- README progress images: `docs/images/phaseN-*.jpg`, captured with the client's `-screenshot` flags
  (`-campitch/-camdistance/-camyaw/-hidedebug` for framing) and `BuildTools.CaptureWorldScreenshot`.
  Update the README progress table + development log when a phase completes.
