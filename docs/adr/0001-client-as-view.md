# ADR 0001 – Unity client as a view, engine-agnostic server, shared C#

**Status:** Decided 2026-10-16

## Decision

- **Client:** Unity 6 with URP.
- **Server:** a standalone .NET 10 console app. No Unity on the server.
- **Shared code:** `Madlands.Shared` and `Madlands.ClientCore` (netstandard2.1, C# 9) are compiled both by dotnet and by Unity, as local UPM packages.
- **Guard against engine code:** the asmdef files set `noEngineReferences: true`, so shared code can never use UnityEngine.

## Why

- One language everywhere.
- The server can be tested and load-tested headless, and the bots use the same client code as the game.
- The Unity client can be replaced (license, performance) without rewriting the game logic.

Unreal was rejected for three reasons:

- Blueprints are binary.
- Dedicated servers require building the engine from source.
- The workflow is a worse fit for AI-assisted development.

## Consequences

Unity's C# version limits Shared: no records/init and no file-scoped namespaces. The rules are listed in `CLAUDE.md`.
