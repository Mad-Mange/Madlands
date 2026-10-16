-- Accounts, login sessions and characters.
-- Migrations are applied in file-name order by DbUp at server startup and are never edited once
-- merged: add a new numbered file instead.

CREATE TABLE accounts (
    id              BIGSERIAL PRIMARY KEY,
    username        TEXT        NOT NULL,   -- normalized lowercase (NameRules.NormalizeUsername)
    password_hash   TEXT        NOT NULL,   -- Argon2id PHC string
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at   TIMESTAMPTZ
);
CREATE UNIQUE INDEX accounts_username_uq ON accounts (username);

-- Only a SHA-256 hash of the session token is stored: a database leak cannot be replayed.
CREATE TABLE sessions (
    token_hash      BYTEA       PRIMARY KEY,
    account_id      BIGINT      NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at      TIMESTAMPTZ NOT NULL
);
CREATE INDEX sessions_account_idx ON sessions (account_id);

-- characters.id doubles as the persistent EntityGuid counter of the player entity.
CREATE TABLE characters (
    id              BIGSERIAL PRIMARY KEY,
    account_id      BIGINT      NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    name            TEXT        NOT NULL,   -- normalized "Capitalized" (NameRules.NormalizeCharacterName)
    race            SMALLINT    NOT NULL,   -- content/races.json id
    class           SMALLINT    NOT NULL,   -- content/classes.json id
    level           SMALLINT    NOT NULL DEFAULT 1,
    map_id          INTEGER     NOT NULL,
    pos_x           REAL        NOT NULL,
    pos_y           REAL        NOT NULL,
    pos_z           REAL        NOT NULL,
    yaw             REAL        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_saved_at   TIMESTAMPTZ,
    deleted_at      TIMESTAMPTZ             -- soft delete: frees the name, keeps the row
);
-- Names are unique per realm among live characters, case-insensitively.
CREATE UNIQUE INDEX characters_name_uq ON characters (lower(name)) WHERE deleted_at IS NULL;
CREATE INDEX characters_account_idx ON characters (account_id) WHERE deleted_at IS NULL;
