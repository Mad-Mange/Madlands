-- Phase 5: long buffs (class buffs, elixirs) survive logging out. Their time stands still while offline.

CREATE TABLE character_auras (
    character_id    BIGINT   NOT NULL REFERENCES characters (id) ON DELETE CASCADE,
    spell_id        BIGINT   NOT NULL,   -- content/spells.json id
    caster_level    SMALLINT NOT NULL,   -- the aura's amounts scale with it
    stacks          SMALLINT NOT NULL,
    remaining_ms    INTEGER  NOT NULL,
    PRIMARY KEY (character_id, spell_id)
);
