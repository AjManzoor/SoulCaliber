-- Phase 12A: relationship statuses are discrete labels, separate from numerical
-- relationship attributes. These are MASTER content definitions, not save state.
-- A future phase will author directional character-to-character defaults.
CREATE TABLE RelationshipStatuses (
    Key TEXT PRIMARY KEY COLLATE NOCASE NOT NULL
        CHECK(length(Key) BETWEEN 1 AND 48 AND Key=upper(Key)),
    Name TEXT NOT NULL CHECK(length(trim(Name)) > 0),
    Description TEXT NOT NULL DEFAULT '',
    SortOrder INTEGER NOT NULL DEFAULT 100,
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
    IsDefault INTEGER NOT NULL DEFAULT 0 CHECK(IsDefault IN (0,1)),
    CHECK(IsDefault = 0 OR IsActive = 1)
);
-- SQLite enforces at most one fallback. Studio validates that exactly one exists.
CREATE UNIQUE INDEX UX_RelationshipStatuses_OneDefault
    ON RelationshipStatuses(IsDefault) WHERE IsDefault=1;
INSERT INTO RelationshipStatuses(Key,Name,Description,SortOrder,IsActive,IsDefault) VALUES
    ('ALLY','Ally','This character considers the other an ally.',10,1,0),
    ('NEUTRAL','Neutral','No declared alliance or hostility; default when not configured.',20,1,1),
    ('ENEMY','Enemy','This character considers the other an enemy.',30,1,0);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
    VALUES('009_RelationshipStatuses',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
