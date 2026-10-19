# ADR 0013 – Localization: our own string tables

**Status:** Decided and implemented 2026-10-19 (Phase 5: `client/Assets/Madlands/Scripts/Core/Loc.cs`,
`client/Assets/Madlands/Resources/Localization/`).

## Context

The roadmap planned Unity's Localization package. It depends on Addressables, which adds an asset build step to
every client build and a second content pipeline, while the game has one language today. What we need now is that
no interface text is hard-coded, so a translation is a data file and not a code change.

## Decision

- **Interface text lives in flat string tables:** `Resources/Localization/<code>.json`, `"key": "text"`, with
  `{0}`, `{1}` for arguments (`string.Format`, invariant culture). `languages.json` lists the languages
  (`"en": "English"`). English (`en.json`) is the fallback for missing keys.
- **Code** asks `Loc.T("key")` / `Loc.T("key", args)`. **UXML** writes `text="#key"` or `label="#key"`; the screens
  call `Loc.Apply(root)` once when they load.
- Keys are grouped by area: `login.*`, `characters.*`, `hud.*`, `chat.*`, `error.*` (one per `ActionError`),
  `cast.*` (one per `CastResult`), `tip.*`, `slot.*`, `stat.*`, `equip.*`, `quest.*`, `settings.*`, `action.*`
  (one per `GameAction`).
- The language is a preference (Settings > Graphics & interface > Language) and applies on the next start; screens
  build their text once.
- A missing key shows the key itself and logs one warning.
- **Not covered yet:** game content (item, spell, creature and quest texts, NPC gossip) comes from the server in
  `GameData` (ADR 0008) and stays English. When a second language arrives, the server sends content in the
  player's language from translated content files keyed by content ids (`quest.12.details`), so ids and rules do
  not change.

## Consequences

- No new packages, no build step, readable diffs; translators edit one JSON file per language.
- No plural rules or gendered forms yet: texts avoid them ("Bags", "{0} Slot Bag"). If a language needs them, the
  table format can grow a `key.one` / `key.other` convention.
- Switching to Unity Localization later stays possible: the keys and tables map one to one onto its string tables.
