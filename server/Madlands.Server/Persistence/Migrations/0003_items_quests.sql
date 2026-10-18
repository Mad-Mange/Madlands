-- Phase 4: money, inventory and quests.

ALTER TABLE characters
    ADD COLUMN money        BIGINT  NOT NULL DEFAULT 0,       -- copper
    ADD COLUMN starter_kit  BOOLEAN NOT NULL DEFAULT FALSE;   -- the class's starting items were handed out

-- Everything a character carries, one row per occupied slot. Rewritten as a whole on every save
-- (same transaction as the character row), so a save is always a consistent snapshot.
CREATE TABLE character_items (
    character_id    BIGINT   NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    container       SMALLINT NOT NULL,   -- 0 equipment, 1 backpack, 2-5 bags (ItemPos)
    slot            SMALLINT NOT NULL,
    item_id         BIGINT   NOT NULL,   -- content/items.json id
    count           INTEGER  NOT NULL,
    PRIMARY KEY (character_id, container, slot)
);

-- Quests in the log (rewarded = false) and quests turned in (rewarded = true).
CREATE TABLE character_quests (
    character_id    BIGINT    NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    quest_id        BIGINT    NOT NULL,   -- content/quests.json id
    rewarded        BOOLEAN   NOT NULL,
    progress        INTEGER[] NOT NULL DEFAULT '{}',   -- per objective; collect objectives are recounted from the bags
    PRIMARY KEY (character_id, quest_id)
);
