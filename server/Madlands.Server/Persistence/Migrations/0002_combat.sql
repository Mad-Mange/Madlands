-- Phase 3: experience, current health/power and GM permissions.

ALTER TABLE characters
    ADD COLUMN xp      INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN health  INTEGER,             -- NULL = full (new character)
    ADD COLUMN power   INTEGER;             -- NULL = class default (full mana, no rage)

-- 0 = player. Anything above may use GM chat commands (.tele, .spawn, ...).
ALTER TABLE accounts
    ADD COLUMN gm_level SMALLINT NOT NULL DEFAULT 0;
