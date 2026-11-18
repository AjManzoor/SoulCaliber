-- Phase 13H-4: one selected relationship target per dialogue choice.
-- Authoring definitions only. The savegame schema is unchanged.
CREATE TABLE IF NOT EXISTS OptionDynamicTargets (
    OptionId INTEGER PRIMARY KEY REFERENCES Options(Id) ON DELETE CASCADE,
    OwnerRef TEXT NOT NULL CHECK(OwnerRef IN ('INITIATOR','RECIPIENT')),
    RelationshipDirection TEXT NOT NULL CHECK(RelationshipDirection IN ('OUTGOING','INCOMING')),
    RequiredStatusKey TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS OptionPairStatusEffects (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OptionId INTEGER NOT NULL REFERENCES Options(Id) ON DELETE CASCADE,
    Outcome TEXT NOT NULL CHECK(Outcome IN ('SUCCESS','FAILURE')),
    FromRef TEXT NOT NULL,
    ToRef TEXT NOT NULL,
    StatusKey TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT,
    ReverseStatusKey TEXT NULL COLLATE NOCASE REFERENCES RelationshipStatuses(Key) ON DELETE RESTRICT,
    CHECK (length(trim(FromRef))>0 AND length(trim(ToRef))>0)
);
CREATE INDEX IF NOT EXISTS IX_OptionPairStatusEffects_Option ON OptionPairStatusEffects(OptionId,Outcome);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('023_DynamicTargetChoices',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
