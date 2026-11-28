-- A zero home map means the character has not chosen an inn yet.
ALTER TABLE characters
    ADD COLUMN home_map_id INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN home_x REAL NOT NULL DEFAULT 0,
    ADD COLUMN home_y REAL NOT NULL DEFAULT 0,
    ADD COLUMN home_z REAL NOT NULL DEFAULT 0,
    ADD COLUMN home_yaw REAL NOT NULL DEFAULT 0,
    ADD COLUMN home_ready_at_ms BIGINT NOT NULL DEFAULT 0;
