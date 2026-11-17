-- Phase 13A: authored memory TYPES, not gameplay memories.
-- Occurrences, knowledge, and acknowledgement belong to the savegame in later phases.
CREATE TABLE MemoryTypes (
    Key TEXT PRIMARY KEY COLLATE NOCASE NOT NULL
        CHECK(length(Key) BETWEEN 1 AND 48 AND Key=upper(Key)
              AND Key NOT GLOB '*[^A-Z0-9_]*'
              AND substr(Key,1,1) GLOB '[A-Z]'),
    Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 120),
    Description TEXT NOT NULL DEFAULT '' CHECK(length(Description) <= 1000),
    DefaultSeverity TEXT NOT NULL DEFAULT 'MINOR'
        CHECK(DefaultSeverity IN ('MAJOR', 'MINOR')),
    DefaultKnowledge TEXT NOT NULL DEFAULT 'QUIET'
        CHECK(DefaultKnowledge IN ('QUIET', 'ALLIES', 'GLOBAL')),
    SortOrder INTEGER NOT NULL DEFAULT 100,
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1))
);

-- A memory type can be relevant to zero, one, or many personality traits.
-- Deleting a memory type clears relevance links; deleting a referenced trait is blocked.
CREATE TABLE MemoryTypePersonalityRelevance (
    MemoryKey TEXT NOT NULL REFERENCES MemoryTypes(Key) ON DELETE CASCADE,
    TraitKey TEXT NOT NULL REFERENCES PersonalityTraits(Key) ON DELETE RESTRICT,
    PRIMARY KEY (MemoryKey, TraitKey)
);
CREATE INDEX IX_MemoryTypePersonalityRelevance_Trait
    ON MemoryTypePersonalityRelevance(TraitKey);

-- Editable examples. None implicitly changes relationships or creates an occurrence.
INSERT INTO MemoryTypes(Key,Name,Description,DefaultSeverity,DefaultKnowledge,SortOrder) VALUES
    ('BETRAYAL','Betrayal','A character betrays another character.', 'MAJOR','QUIET',10),
    ('DEFEAT','Defeat','A character defeats another character.', 'MAJOR','ALLIES',20),
    ('RESCUE','Rescue','A character saves someone from danger.', 'MAJOR','ALLIES',30),
    ('COMPLIMENT','Compliment','A character offers praise or a compliment.', 'MINOR','QUIET',40);

INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
    VALUES('015_MemoryCatalogue',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
