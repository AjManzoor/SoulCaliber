-- Phase 12D: gate a dialogue option on the CURRENT directional relationship statuses.
-- One choice may require several directions; all must match before it is available.
CREATE TABLE OptionStatusRequirements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OptionId INTEGER NOT NULL REFERENCES Options(Id) ON DELETE CASCADE,
    Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR','BOTH')),
    StatusKey TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT,
    UNIQUE(OptionId,Direction)
);
CREATE INDEX IX_OptionStatusRequirements_StatusKey ON OptionStatusRequirements(StatusKey);
-- Do not allow the author to disable a status which is used to gate a choice.
CREATE TRIGGER PreventInactiveRequiredStatus BEFORE UPDATE OF IsActive ON RelationshipStatuses
WHEN NEW.IsActive=0 AND EXISTS (SELECT 1 FROM OptionStatusRequirements WHERE StatusKey=OLD.Key)
BEGIN SELECT RAISE(ABORT,'Cannot deactivate a relationship status used by dialogue requirements'); END;
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('013_OptionStatusRequirements',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
