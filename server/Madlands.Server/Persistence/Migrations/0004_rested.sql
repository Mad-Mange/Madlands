-- Phase 5: rested experience. The time away is measured from last_saved_at (the save at logout).

ALTER TABLE characters
    ADD COLUMN rested_xp INTEGER NOT NULL DEFAULT 0;   -- kills give double experience until it runs out
