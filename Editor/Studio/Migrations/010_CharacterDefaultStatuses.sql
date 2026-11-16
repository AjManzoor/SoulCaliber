-- Phase 12B: authored STARTING relationship statuses, directional by character.
-- Missing pair => NEUTRAL from the Phase 12A status catalogue.
-- These rows are master-content only and are NEVER changed by gameplay.
CREATE TABLE CharacterDefaultStatuses (
    SourceCharacterId TEXT NOT NULL COLLATE NOCASE REFERENCES Characters(Id) ON DELETE CASCADE,
    TargetCharacterId TEXT NOT NULL COLLATE NOCASE REFERENCES Characters(Id) ON DELETE CASCADE,
    StatusKey TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT,
    PRIMARY KEY(SourceCharacterId, TargetCharacterId),
    CHECK(SourceCharacterId <> TargetCharacterId COLLATE NOCASE),
    CHECK(StatusKey <> 'NEUTRAL' COLLATE NOCASE)
) WITHOUT ROWID;
CREATE INDEX IX_CharacterDefaultStatuses_Target ON CharacterDefaultStatuses(TargetCharacterId);

-- An inactive catalogue item must not remain assigned to a character pair.
-- Deleting an assigned status is already blocked by the FK above.
CREATE TRIGGER TR_RelationshipStatuses_ActiveDefaults
BEFORE UPDATE OF IsActive ON RelationshipStatuses
WHEN NEW.IsActive=0 AND EXISTS (
    SELECT 1 FROM CharacterDefaultStatuses WHERE StatusKey=OLD.Key COLLATE NOCASE
)
BEGIN
    SELECT RAISE(ABORT, 'Cannot deactivate a relationship status used by starting relationships.');
END;

INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('010_CharacterDefaultStatuses',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
