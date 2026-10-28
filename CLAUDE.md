# Madlands – notes for AI sessions

WoW-like MMORPG. Unity 6 client (URP) + authoritative .NET 10 server + shared C# code.
**Language:** the user chats in Swedish — reply in Swedish. But the **entire project is English**: code,
comments, in-game UI text, docs, README, commit messages, GitHub description, file names, lore. Never write
Swedish into repo files (non-ASCII *test data* like "Räksmörgås" in ProtocolTests is intentional).
Roadmap and phase status: `docs/roadmap.md`. Architecture and the lock-in decisions: `docs/architecture.md`, `docs/adr/`.

## Layout

| Path | What |
|---|---|
| `shared/Madlands.Shared` | Protocol, movement rules, ids, `UnitValues`, `FactionTable`, `SpellInfo`, `ItemInfo`/`ItemRules`/`ItemPos`/`Money`, `QuestInfo`, height map + collision mesh formats. Compiled by dotnet AND Unity (local UPM package). |
| `shared/Madlands.ClientCore` | Engine-free client logic (GameClient with unit state/auras/casts/cooldowns, inventory, quest log, loot and NPC dialog state, ClientGameData, ClockSync, InterpolationBuffer, MovementSender). Used by Unity client and bots. |
| `server/Madlands.Server` | One realm process (`ServerHost`): UDP game server (20 Hz loop, map instances, AoI, movement validation), combat (`Combat/`: CombatSystem, SpellSystem, AuraSystem, StatFormulas), items (`Items/`: `Inventory` rules, ItemSystem, LootSystem), quests (`Quests/`: QuestSystem, QuestLog), creature AI (`AI/`), chat + GM commands (`Commands/`), HTTP account API (`Api/`), persistence (`Persistence/`: Postgres + in-memory, DbUp migrations, write-behind `CharacterSaver`), Argon2id auth (`Auth/`). |
| `server/Madlands.Navigation` | DotRecast navmesh (built at server startup from height map + `collision.bin`) and line-of-sight ray casts. |
| `server/Madlands.Content` | Loads + validates `content/` (JSON + binary), cross-checks references. Server/tools only. |
| `server/Madlands.Bots` | Headless bot players (register/login via the API, then hunt creatures or wander) for load/integration testing. |
| `server/Madlands.Server.Tests` | xUnit: protocol roundtrips, world logic, client core, real HTTP+UDP realm tests (`TestRealm`), Postgres tests (`[PostgresFact]`, skipped if no DB). |
| `tools/Madlands.ContentTool` | `validate` content (same checks as server startup; used by CI). |
| `art/` | Low-poly art as Python scripts for Blender (ADR 0012, `art/README.md`): `blender/build.py` exports FBX into `client/Assets/Madlands/Art/`; characters are sculpted from blended shapes (`blender/sculpt.py`) in three LODs, `blender/lookbook.py` renders them in outfits; `audio/` synthesizes the music and sounds (OGG into `Audio/`). |
| `client/` | Unity project. Our code: `client/Assets/Madlands/{Scripts,Editor,UI}`. Scenes: `FrontEnd` (boot, UI, camera, networking) + `World_NNNN` map scenes loaded additively by mapId. In-game windows (bags, character, loot, vendor, quests) are plain C# classes owned by `HudController`, styled by `UI/Windows.uss`. |
| `docs/zones/` | Zone design: map, story, NPCs, creatures and quest chains (`hollowmere.md`). |
| `content/` | Game data: races, classes, factions, spells, creatures, levels, items, loot, quests, gatherable objects (JSON + `schemas/`), `maps/<id>/` (height.bin, collision.bin, spawns.json, objects.json, map.json: written by Unity, Madlands > Export Map). |

## Commands (PowerShell; dotnet may need PATH refresh in old shells)

```powershell
docker compose -f deploy/docker-compose.yml up -d   # PostgreSQL (+ Seq) for the server and the Postgres tests
dotnet build Madlands.slnx                      # all .NET projects (output in /artifacts)
dotnet test Madlands.slnx                       # 130+ tests incl. real HTTP+UDP realm, combat/loot/quests end to end, Postgres
dotnet run --project server/Madlands.Server -- --bind 127.0.0.1 --gm-all           # UDP 7777 + HTTP 7780, PostgreSQL, GM commands for all
dotnet run --project server/Madlands.Server -- --bind 127.0.0.1 --db memory        # no Docker; data lost on exit
dotnet run --project server/Madlands.Server -- set-gm <account> 3                  # permanent GM level for one account
dotnet run --project server/Madlands.Server -- set-password <account> <password>   # new password (ends the account's sessions)
# Public realm (docs/deployment.md): --bind 0.0.0.0 --api-bind 127.0.0.1 --trust-proxy --public-host <domain>, Caddy in front
dotnet run --project server/Madlands.Bots -- --count 200 --seconds 60   # fight creatures (--mode wander: just run); server needs --api-rate-limit 0
dotnet run --project tools/Madlands.ContentTool -- validate

$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
& $blender -b --factory-startup -P art/blender/build.py -- [asset ...] [--preview <dir>]   # FBX into the Unity project (+ pictures)
& $blender -b --factory-startup -P art/blender/lookbook.py -- [look ...] [--lod N]         # races in outfits -> builds/lookbook/*.png
& $blender -b --factory-startup -P art/blender/icons.py -- [icon ...]                      # interface symbols as PNG
& $blender -b --factory-startup -P art/audio/build.py -- [sound ...]                       # synthesized music/sounds as OGG
python art/audio/kenney.py                                                                  # re-import the CC0 Kenney sounds

$unity = "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe"
# Each: & $unity -batchmode -projectPath client -logFile <log> -executeMethod <method>   (no -quit; methods call Exit)
#   Madlands.Client.Editor.BuildTools.CompileCheck                 -> exit 0 if all scripts compile
#   Madlands.Client.Editor.BuildTools.BuildWindowsFromCommandLine  -> builds/client/Madlands.exe
#   Madlands.Client.Editor.BuildTools.CaptureWorldScreenshot       -> builds/screenshots/editor_*.png
#   Madlands.Client.Editor.HollowmereBuilder.BuildFromCommandLine  -> regenerates World_0001 (Hollowmere) + exports map
#   Madlands.Client.Editor.FrontEndBuilder.BuildFromCommandLine    -> regenerates FrontEnd scene + PanelSettings
#   Madlands.Client.Editor.MapExporter.ExportFromCommandLine       -> content/maps/0001/{height.bin,map.json}
#   Madlands.Client.Editor.ArtImporter.ReportFromCommandLine       -> logs every model: size, avatar, materials, clips
# Unity cannot run batchmode while the user has the project open in the editor.

# Visual end-to-end checks (server running). -autoconnect logs in as the lowercased -name with the dev
# password, creating account and character if needed:
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Tester -screenshot builds/screenshots/world.png -screenshotdelay 7 -quitafter 13
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Tester -stopat charselect -screenshot builds/screenshots/chars.png -screenshotdelay 6 -quitafter 8
builds/client/Madlands.exe -screen-fullscreen 0 -screenshot builds/screenshots/login.png -screenshotdelay 4 -quitafter 6
# Combat (server with --gm-all): -class picks the class of a new character, -exec sends chat lines/GM commands
# after entering the world, -autofight targets the nearest enemy and uses the action bar.
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Magetest -class shardmage -exec ".level 3;.tele -60 -62" -autofight -screenshot builds/screenshots/fight.png -screenshotdelay 10 -quitafter 20
# Items and quests: -open bags,character,questlog,spellbook,map opens windows, -talkto "Old Tamsin" (+ -quest 1) opens an NPC dialog,
# -loot opens loot of corpses in reach, -gather gathers objects in reach (mushrooms), -tooltip <item id> shows a bag item's
# tooltip, -equip 1020,1210 equips from the bags, -baritems "7=4003,13=4008" puts items onto action buttons 1-24 and
# -pressbar 13,7 presses buttons (a second apart). -face 180 turns the character to a compass direction (framing).
# -autorun runs forward from the start, -sprint holds the sprint key (test sprint and stamina against the server).
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Shopper -exec ".money 5000;.tele 10 -142" -talkto "Quartermaster Holt" -screenshot builds/screenshots/vendor.png -screenshotdelay 9 -quitafter 13
```
Then read the PNG to verify (`-screenshotdelay` counts from entering the world, `-quitafter` from startup: leave
room for logging in and the `-exec` lines, sent 0.7 s apart; character names are letters only, so a `-name` with a
digit never gets into the world). Use `-logFile` for the player log. The server keeps characters between client runs:
use a new `-name` per screenshot run, or `.additem` lines add up. From PowerShell `Start-Process`, quote arguments
that contain spaces yourself (`"-exec", "`".tele 3 4`""`), it joins the list unquoted. A running Release server locks
`artifacts/bin/*/release`; build and test with `-c Debug` meanwhile (or run a second realm on other ports:
`--port 7787 --http-port 7790`, client `-httpport 7790`).

## Gotchas learned the hard way

- Setting `UIDocument.panelSettings` from an editor script is NOT persisted: set the serialized field
  `m_PanelSettings` via `SerializedObject` (FrontEndBuilder does). Symptom: UI invisible in builds, no errors.
- Unity has its own `UnityEngine.CharacterInfo`; avoid type names that clash with UnityEngine in Shared.
- `ScreenCapture.CaptureScreenshot` does include UI Toolkit; if UI is missing, the UI is really not rendering.
- Content enums are camelCase strings in JSON (`"powerType": "rage"`); spell flags are a string list.
- `GameData` (spells, factions, classes, XP table, items, quests) is built from content at server startup and sent
  at login (ADR 0008): content changes need a server restart, not a client rebuild.
- URP uses linear color space: UI Toolkit backgrounds with even 5% transparency let bright text behind them show
  through. Windows and tooltips use opaque backgrounds.
- GM commands of GM accounts have their own rate limit (`GmCommandLimiter`); normal chat is 2 lines/s.
- Pushing changes to `.github/workflows/` needs a GitHub token with the `workflow` scope
  (`gh auth refresh -h github.com -s workflow`, run by the user: it opens a browser).
- Windows: a Git Credential Manager prompt cannot be shown from here; push with
  `git -c credential.helper= -c "credential.helper=!gh auth git-credential" push` (or the user runs `gh auth setup-git`).

- Unity keeps the orientation an FBX declares: changing Blender's export axes changes nothing. `ArtImporter`
  turns every model root 180 degrees so Blender's front (-Y) faces Unity's +Z.
- Humanoid avatars: `ArtImporter` maps all 21 bones by name (Unity's own guess drops `Chest` and the shoulders, and
  their clip rotations with them) and needs a stored skeleton for that; `ArtImporter.RemeasureSkeletons` (run by
  builds and the art report, and by the editor after imports) stores a new one when a race's bones moved. Unity
  only re-imports existing art after an importer change when `ArtImporter.GetVersion()` is raised.
- `HumanoidAnimations.fbx` has its own avatar: it is exported in the Brightvale rest pose. Copying the race's avatar
  fails (Unity drops the lone `Armature` node, the hierarchies differ) and silently imports no humanoid clips:
  check the art report's clip count. In the report, `idle ... weapon points` shows where a held weapon points.
- Unassigning a Blender action keeps its last pose: `lowpoly.export_fbx` resets the pose before exporting.
- To check animated poses in batch mode use `AnimationMode.SampleAnimationClip`; `PlayableGraph.Evaluate` does
  nothing there.
- A MonoBehaviour method named `Start` (or another Unity message) with parameters logs "Script error ... can not take
  parameters" in player builds: name helpers differently.
- Character meshes come three times, named `*_LOD0` .. `*_LOD2` (`humanoid.build_race`). `DisplayModel` strips the suffix
  (gear piece names), tints every body mesh (body, hair, beard) and builds the `LODGroup` at runtime (`SetUpLods`).
  New mesh names make `ArtImporter` measure the avatar again: harmless.
- `sculpt.Star` and `sculpt.sweep` cast rays from points that must lie inside the shape: an origin outside gives an
  empty, invisible mesh without any error (count the triangles). `humanoid.Head.surface` casts straight forward at a
  height; rays from the head's center reach the face at other heights.
- Blender's Workbench draws back faces: cloth (a surface plus a lining facing the other way) z-fights in previews
  unless backface culling is on (the lookbook turns it on; Unity culls back faces).
- Props with mesh colliders become navmesh obstacles (Madlands > Export Map). Anything hanging lower than the 2 m agent
  height over a path (a gate's banner, a beam) closes it for creatures; `NavigationTests` walks out of Mirewatch's gates.

## Rules

- **Shared/ClientCore must compile in Unity**: netstandard2.1, C# 9, block-scoped namespaces, no records/init/
  file-scoped namespaces/global usings/nullable annotations, no NuGet deps in Shared. The asmdefs use
  `noEngineReferences: true`, so UnityEngine can never leak into shared code.
- **Any wire or shared-rule change bumps `ProtocolVersion.Current`.** Never renumber opcodes. Every message
  gets a roundtrip test in `ProtocolTests.cs`.
- **Client input goes through `KeyBindings`** (`IsHeld`/`WasPressed` of a `GameAction`), never `Keyboard.current[...]`
  directly, so every key stays rebindable. Exceptions: Esc and text-field submit. Settings live in `Preferences`.
- **Server is authoritative.** Clients only predict their own movement (and the global cooldown, and sprint stamina:
  `Movement/Stamina.cs` is simulated identically on both sides); the server validates
  (`MovementValidator`, `SpellSystem.Check`) and corrects. Never trust client data; reject NaN; rate-limit every message type.
- **Combat rules are data + one formula file.** New spell/aura behaviour = a new effect type with a handler, never a
  special case per spell id (ADR 0005). Balance numbers live in `content/` and `Combat/StatFormulas.cs`.
- **Art comes from the `art/` scripts** (ADR 0012). `ArtImporter` owns the import settings of everything in
  `Assets/Madlands/Art`; never set them by hand. Character material names are the gear regions the client recolors.
- **No hard-coded interface text** (ADR 0013): use `Loc.T("area.key")` in code and `text="#area.key"` in UXML, and
  add the English text to `client/Assets/Madlands/Resources/Localization/en.json`. Game content texts come from the
  server (content/) and are not keys.
- **Sounds are played by name** through `SoundSystem` (`Play`, `PlayAt`, `SetMusic`, `SetAmbience`); game events map
  to sounds in `GameSounds`. Only CC0 or self-made audio goes in the repo; list third-party sources in `CREDITS.md`.
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
  (`-campitch/-camdistance/-camyaw/-hidedebug/-hidehud` for framing) and `BuildTools.CaptureWorldScreenshot`.
  Update the README progress table + development log when a phase completes.
