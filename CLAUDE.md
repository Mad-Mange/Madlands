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
| `server/Madlands.Server` | Game server (LiteNetLib UDP, 20 Hz loop, map instances, AoI, movement validation). |
| `server/Madlands.Content` | Loads + validates `content/` (JSON + binary). Server/tools only. |
| `server/Madlands.Bots` | Headless bot players for load/integration testing. |
| `server/Madlands.Server.Tests` | xUnit: protocol roundtrips, world logic, client core, real-UDP integration tests. |
| `tools/Madlands.ContentTool` | `validate` content (later: compile content, build navmeshes). |
| `client/` | Unity project. Our code: `client/Assets/Madlands/{Scripts,Editor}`. |
| `content/` | Game data. `maps/<id>/` is written by Unity (Madlands > Export Map). |

## Commands (PowerShell; dotnet may need PATH refresh in old shells)

```powershell
dotnet build Madlands.slnx                      # all .NET projects (output in /artifacts)
dotnet test Madlands.slnx                       # 46+ tests incl. real UDP server/client
dotnet run --project server/Madlands.Server -- --bind 127.0.0.1      # local server on UDP 7777
dotnet run --project server/Madlands.Bots -- --count 200 --seconds 60
dotnet run --project tools/Madlands.ContentTool -- validate

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
# Each: & $unity -batchmode -projectPath client -logFile <log> -executeMethod <method>   (no -quit; methods call Exit)
#   Madlands.Client.Editor.BuildTools.CompileCheck                 -> exit 0 if all scripts compile
#   Madlands.Client.Editor.BuildTools.BuildWindowsFromCommandLine  -> builds/client/Madlands.exe
#   Madlands.Client.Editor.BuildTools.CaptureWorldScreenshot       -> builds/screenshots/editor_*.png
#   Madlands.Client.Editor.TestWorldBuilder.BuildFromCommandLine   -> regenerates World_0001 + exports map
#   Madlands.Client.Editor.MapExporter.ExportFromCommandLine       -> content/maps/0001/{height.bin,map.json}
# Unity cannot run batchmode while the user has the project open in the editor.

# Visual end-to-end check (server + bots running):
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Tester -screenshot builds/screenshots/client_ingame.png -screenshotdelay 7 -quitafter 13
```
Then read the PNG to verify. Use `-logFile` for the player log.

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
