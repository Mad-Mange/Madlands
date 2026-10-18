# ADR 0010 – Items, inventory, loot and money

**Status:** Decided and implemented 2026-10-18 (Phase 4: `server/Madlands.Server/Items`, `content/items.json`, `content/loot.json`).

## Decision

**Items are content**

- `content/items.json` defines every item: name, quality, class (weapon, armor, consumable, container, quest, trade goods, junk), the slot it is worn in, required level and classes, flat stats (armor included), weapon damage and speed, stack size, buy and sell price, a use spell and bag slots.
- Ids are stable and never reused: they are stored on characters.
- The client gets a display copy of every item (`ItemInfo`) in `GameData` at login (ADR 0008). Rules stay on the server.

**Inventory**

- A character has 20 equipment slots (16 worn, 4 bag slots), a 16-slot backpack and up to four bags. An inventory position is `(container, slot)`: container 0 is the equipment, 1 the backpack, 2–5 the bags. A bag's size comes from the bag item in its bag slot.
- One rules class (`Inventory`) does everything: moving, swapping, merging and splitting stacks, the equip rules (slot, class, level), two-handed weapons (they push the off-hand item into the bags), bags (only empty bags move, never into themselves). It is pure logic without networking, unit-tested on its own.
- Clients only ask (`MoveItem`, `AutoEquip`, `UseItem`, `DestroyItem`). The server applies the rules to the real inventory and sends the changed slots back (`InventoryUpdate`). The client never changes its own copy.
- Worn items add their stats as flat modifiers before aura percentages (ADR 0005). The main-hand weapon sets the swing damage and speed; without one a class fights unarmed. Weapons and shields are visible to others through two unit fields (`MainHandDisplay`, `OffHandDisplay`); since protocol v8 the worn armor too (`Gear`: head, shoulders, back, chest, hands, waist, legs, feet, by the items' display ids).
- Consumables cast their use spell like a known spell (instant spells only); the item is used up only if the cast happened. Food and drink are ordinary auras (`periodicHeal`, the new `periodicEnergize`) that break when combat starts.

**Item instances**

- For now a slot holds `(item id, count)`. There are no per-item properties (durability, random enchantments, soulbinding) and no per-instance ids.
- When trading and mail arrive (Phase 6) items move between characters. That needs per-instance ids and transactional transfers (ADR 0007): a migration then adds an id column to `character_items`.

**Money**

- Counted in copper (100 copper = 1 silver, 100 silver = 1 gold) as a 64-bit number, capped at 99,999 gold.

**Loot**

- `content/loot.json` holds loot tables; creatures refer to one. A table has a money range, items that each roll their own chance, and groups that drop exactly one of their items with the group's chance.
- Loot is rolled when a creature dies, for the player who tapped it. Quest items (class `quest`) only drop while that player still needs them for a quest.
- Only the owner sees the corpse sparkle (`LootableUpdate` is private) and may open it within 6 m (`LootOpen`, `LootTake`). A corpse with loot stays at least 90 s; an emptied one goes on its normal timer.
- Groups (Phase 6) will extend "the owner" to a list of players and add loot rules.

**Vendors**

- A creature with a `vendor` list sells those items at their buy price, unlimited. Anything with a sell price can be sold back; quest items cannot. No buyback yet.

**Persistence**

- Migration 0003: `characters.money`, `characters.starter_kit`, `character_items` (one row per occupied slot) and `character_quests`.
- The write-behind snapshot (ADR 0007) now carries items and quests. Saving writes the character row, its items and its quests in **one transaction**, so a save is always consistent. Items and quests are rewritten as a whole with one statement each.
- A class's starting items are handed out on a character's first login (`starter_kit`), which also covers characters created before Phase 4.

## Why

- This is the WoW model in its simplest form. It carries the whole item loop (loot, gear, vendors, bags) without per-instance data, and per-instance ids can be added later without changing the wire format of the rules.
- Keeping all inventory rules in one pure class makes them testable and keeps exploits (duplication, equipping what you may not) in one place to review.
- Rolling loot on the server for one owner, and hiding it from everyone else, means no client can see or claim loot it should not.

## Consequences

- An inventory move costs a round trip before the client shows it. On a local network this is not noticeable; with high latency a "pending" ghost could be added later.
- Whole-inventory saves write about 20–60 rows per character every 60 s. That is cheap at our scale; diffs can replace it if it ever shows up in the database load.
