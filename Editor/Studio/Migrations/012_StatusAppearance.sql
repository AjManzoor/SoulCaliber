-- Phase 12D polish: presentation only. These fields do not alter status rules,
-- the existing default relationships, or any runtime savegame overrides.
ALTER TABLE RelationshipStatuses ADD COLUMN IconPath TEXT NOT NULL DEFAULT '';
ALTER TABLE RelationshipStatuses ADD COLUMN Color TEXT NOT NULL DEFAULT '#D4B8F5';

UPDATE RelationshipStatuses
SET IconPath='res://Assets/StatusIcons/ally.svg', Color='#8AE6A8'
WHERE Key='ALLY';
UPDATE RelationshipStatuses
SET IconPath='res://Assets/StatusIcons/neutral.svg', Color='#BDB8CC'
WHERE Key='NEUTRAL';
UPDATE RelationshipStatuses
SET IconPath='res://Assets/StatusIcons/enemy.svg', Color='#FF9994'
WHERE Key='ENEMY';

INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
    VALUES('012_StatusAppearance',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
