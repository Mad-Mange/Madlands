<div align="center">

# ⚔️ Madlands

**A WoW-inspired MMORPG, built from scratch, one playable step at a time.**

![Status](https://img.shields.io/badge/status-work%20in%20progress-orange)
![Phase](https://img.shields.io/badge/phase-1%20of%2010%20done-blue)
![Unity](https://img.shields.io/badge/Unity-6-000000?logo=unity)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![Tests](https://img.shields.io/badge/tests-46%20passing-brightgreen)

<img src="docs/images/phase1-overview.jpg" alt="The player (blue) among 200 bots (orange) in the test world" width="100%">

<sub><i>Phase 1: a real player (blue) and 200 bots (orange) sharing one world, kept in sync by the server.</i></sub>

</div>

> [!NOTE]
> **This is an early-stage, work-in-progress project.** Madlands is a solo project with a big goal: a homemade MMORPG in the spirit of World of Warcraft, with tab-target combat, quests, dungeons and a shared world.
> What you see today is **the engine room**. All art is placeholder (capsules and lollipop trees). Real art arrives in Phase 5.
> Development is AI-assisted (Claude Code).

## What is Madlands?

Three hundred years ago the moon **Ysolde** cracked and rained shards across the continent of **Calderra**. Wherever the shards fell, the land twisted into what people now call *the Madlands*. The shards are also the source of all magic: some want to harvest their power, others want to seal them away forever. <sub>([lore draft](docs/gdd.md))</sub>

**The goal** is a small but real MMO, with the same kinds of systems as WoW and a much smaller world:

- Tab-target combat with cast times, cooldowns and auras
- Classes and races, quests, loot and gear
- Groups, guilds and dungeons
- A stylized low-poly world

**The approach:** the game is built **one playable phase at a time**. The architecture is designed for the finished game from day one, so nothing built early has to be thrown away later.

## 🗺️ Progress

| Phase | Scope | Status |
|:---:|---|:---:|
| 0 | Foundation: repo, tooling, architecture, decision log | ✅ Done |
| 1 | **Walking skeleton:** many players in one world, synced by the server | ✅ Done |
| 2 | Accounts, login, characters, database | 🔜 Next |
| 3 | Core combat: targeting, spells, auras, NPC AI, XP | ⬜ |
| 4 | Items, loot, economy, quests | ⬜ |
| 5 | Vertical slice: the first real zone, with real art. *"Is it fun?"* | ⬜ |
| 6–7 | Social (chat, groups, guilds) and dungeons | ⬜ |
| 8–10 | Bigger world, talents, professions, PvP, launch | ⬜ |

Details: [docs/roadmap.md](docs/roadmap.md)

### Latest milestone: Phase 1 ✅

| Measurement | Result |
|---|---|
| Concurrent players in the test | 200 bots + 1 real client |
| Server tick (target < 5 ms) | **0.4–0.9 ms** average |
| Movement corrections by the server | **0** during the whole test |
| Local ping | ~15–30 ms |
| Client FPS with 200 visible players | 375–1100 |
| Automated tests | 46 passing, including a real server and clients over UDP |

## 📸 Progress gallery

<table>
  <tr>
    <td width="50%"><img src="docs/images/phase1-gameplay.jpg" alt="Gameplay view with debug overlay"></td>
    <td width="50%"><img src="docs/images/phase1-test-world.jpg" alt="The test world"></td>
  </tr>
  <tr>
    <td><sub><b>Gameplay view.</b> WoW-style third-person camera, nameplates, and the debug overlay (F3) with ping, entity count and position.</sub></td>
    <td><sub><b>The "Testlands" test world.</b> 512 × 512 m of hills, forest and fog, generated entirely from code.</sub></td>
  </tr>
  <tr>
    <td><img src="docs/images/phase1-map.jpg" alt="The test world from above"></td>
    <td valign="top">
      <sub><b>Top-down map.</b> The dirt clearing in the middle is the spawn point. The rocky rim keeps players inside. The height map is exported from Unity to the server, which uses it to decide whether a movement is possible.</sub>
      <br><br>
      <b>Dev log</b>
      <ul>
        <li><sub><b>2026-10-16 – Phases 0 & 1 done.</b> Server, client, bots and tests are in place. 200 players run around without a single movement correction.</sub></li>
      </ul>
    </td>
  </tr>
</table>

## 🏗️ How it works

```mermaid
flowchart LR
    subgraph client["🎮 Unity 6 client"]
        input["Controls & camera<br/>(predicts own movement)"]
        view["Other players<br/>(interpolation)"]
    end
    subgraph shared["📦 Shared C# code"]
        core["ClientCore<br/>networking · clock sync"]
        sh["Shared<br/>protocol · game rules"]
    end
    subgraph server["🖥️ .NET 10 server – authoritative"]
        loop["Game loop 20 Hz"]
        maps["Map instances<br/>AoI · movement validation"]
        loop --> maps
    end
    bots["🤖 Bots<br/>(same client code)"]
    content[("content/<br/>maps & game data")]

    client --> core
    bots --> core
    core <-->|"UDP · LiteNetLib"| loop
    content --> maps
```

**Core principles**

- **The server is the authority.** The client moves its own character immediately so it feels responsive. The server checks every move (speed, clock, terrain) and corrects cheating.
- **The engine is replaceable.** All game logic lives in the server and in shared C# code that is not allowed to reference Unity. The client is "just" a view.
- **Content is data.** Maps, and later spells, items and quests, are text files with stable IDs, validated automatically.
- **Bots run the exact same client code as the game.** That makes it possible to load-test with hundreds of "players" on one machine.

More in [docs/architecture.md](docs/architecture.md) and the decision log in [docs/adr/](docs/adr/).

## 🧰 Tech stack

| Part | Choice |
|---|---|
| Client | Unity 6 (URP), Input System |
| Server | .NET 10, LiteNetLib (UDP), Serilog |
| Shared code | C# (netstandard2.1), compiled by both Unity and .NET |
| Testing | xUnit, headless bot clients |
| Coming up | PostgreSQL, UI Toolkit, Addressables, DotRecast (navmesh) |

## 🚀 Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/) and Unity 6000.6.5f1. Docker is needed from Phase 2.

```powershell
# 1. Start the server (UDP 7777, local only)
dotnet run --project server/Madlands.Server -c Release -- --bind 127.0.0.1

# 2. (optional) 200 bots running around
dotnet run --project server/Madlands.Bots -c Release -- --count 200

# 3. Client: open client/ in Unity Hub, open Assets/Madlands/Scenes/World_0001 and press Play.
#    Or build an exe via the menu Madlands > Build Windows Client and start two windows:
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Alice
builds/client/Madlands.exe -screen-fullscreen 0 -autoconnect -name Bob
```

Run the tests with `dotnet test Madlands.slnx`.

### 🎮 Controls (WoW-style)

| Input | Action |
|---|---|
| W / S | Forward / backward (backpedaling is slower) |
| A / D | Turn (strafe while holding the right mouse button) |
| Q / E | Strafe left / right |
| Right mouse + drag | Steer the character and the camera |
| Left mouse + drag | Orbit the camera freely |
| Both mouse buttons | Run forward |
| Mouse wheel | Zoom |
| Space | Jump (no air control) |
| Num Lock / R | Autorun |
| F3 | Toggle the debug overlay |

## 📁 Project structure

```
client/                    Unity 6 project (our code in Assets/Madlands)
shared/Madlands.Shared     Protocol and game rules, shared by server and client
shared/Madlands.ClientCore Engine-free networking & sync, used by the client and bots
server/Madlands.Server     The game server
server/Madlands.Bots       Bot players for load testing
server/Madlands.Content    Loads and validates game data
content/                   Game data (maps; later spells, items, quests …)
tools/                     Tooling (ContentTool)
docs/                      Architecture, decision log (ADR), roadmap, game design
```

## 📚 Documentation

- [Roadmap & status](docs/roadmap.md)
- [Architecture](docs/architecture.md) and [decision log (ADR)](docs/adr/)
- [Game design & lore (draft)](docs/gdd.md)

## License

No license has been chosen yet, so all rights are reserved for now. Feel free to look around and get inspired.
Madlands is not affiliated with Blizzard Entertainment or World of Warcraft.
