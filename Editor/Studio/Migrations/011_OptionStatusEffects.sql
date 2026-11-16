-- Phase 12C: authored discrete relationship-status outcomes for dialogue choices.
-- An effect changes a status, NOT a numerical relationship attribute.
CREATE TABLE OptionStatusEffects (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OptionId INTEGER NOT NULL REFERENCES Options(Id) ON DELETE CASCADE,
    Outcome TEXT NOT NULL CHECK(Outcome IN ('SUCCESS','FAILURE')),
    Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR','BOTH')),
    StatusKey TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT,
    UNIQUE(OptionId,Outcome,Direction)
);
CREATE INDEX IX_OptionStatusEffects_StatusKey ON OptionStatusEffects(StatusKey);
-- Don't deactivate a status which is referenced by authored dialogue.
CREATE TRIGGER PreventInactiveDialogueStatus BEFORE UPDATE OF IsActive ON RelationshipStatuses
WHEN NEW.IsActive=0 AND EXISTS (SELECT 1 FROM OptionStatusEffects WHERE StatusKey=OLD.Key)
BEGIN SELECT RAISE(ABORT,'Cannot deactivate a relationship status used by dialogue effects'); END;
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('011_OptionStatusEffects',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
