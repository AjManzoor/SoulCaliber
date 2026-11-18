-- Phase 13H-4A: an alternative to selecting the live relationship group.
-- Existing choices remain GROUP mode. Player savegame schema is unchanged.
ALTER TABLE OptionDynamicTargets ADD COLUMN SelectionMode TEXT NOT NULL DEFAULT 'GROUP'
    CHECK(SelectionMode IN ('GROUP','CUSTOM'));

CREATE TABLE OptionDynamicTargetCharacters (
    OptionId INTEGER NOT NULL REFERENCES OptionDynamicTargets(OptionId) ON DELETE CASCADE,
    CharacterId TEXT NOT NULL COLLATE NOCASE REFERENCES Characters(Id) ON DELETE CASCADE,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (OptionId,CharacterId)
);
CREATE INDEX IX_OptionDynamicTargetCharacters_Order
    ON OptionDynamicTargetCharacters(OptionId,SortOrder,CharacterId);

INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('024_CustomDynamicTargetLists',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
