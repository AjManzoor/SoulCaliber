-- Phase 13E-1 follow-up: let authored special encounters match any recipient.
-- SQLite does not support removing NOT NULL from an existing column directly.
-- Execute within a transaction, preserving existing encounter records and constraints.
CREATE TABLE SpecialEncounters_AnyRecipientMigration (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EncounterKey TEXT NOT NULL COLLATE NOCASE UNIQUE
        CHECK(length(trim(EncounterKey)) BETWEEN 1 AND 48),
    Title TEXT NOT NULL CHECK(length(trim(Title)) BETWEEN 1 AND 120),
    InteractionId INTEGER NOT NULL REFERENCES Interactions(Id) ON DELETE RESTRICT,
    RecipientCharacterId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    InitiatorCharacterId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    LaunchMode TEXT NOT NULL DEFAULT 'AUTOMATIC'
        CHECK(LaunchMode IN ('AUTOMATIC','OPTIONAL')),
    RepetitionMode TEXT NOT NULL DEFAULT 'ONCE_PER_PAIR'
        CHECK(RepetitionMode IN ('ONCE_PER_PAIR','ONCE_PER_OCCURRENCE','REPEATABLE')),
    Priority INTEGER NOT NULL DEFAULT 50 CHECK(Priority BETWEEN 0 AND 1000),
    IsActive INTEGER NOT NULL DEFAULT 0 CHECK(IsActive IN (0,1)),
    ConditionJson TEXT NULL,
    AnchorMemoryKey TEXT NULL REFERENCES MemoryTypes(Key) ON DELETE RESTRICT,
    AnchorActorRef TEXT NOT NULL DEFAULT 'ANY',
    AnchorAffectedRef TEXT NOT NULL DEFAULT 'ANY',
    AnchorKnowerRef TEXT NOT NULL DEFAULT 'ANY',
    CHECK(RecipientCharacterId IS NULL OR InitiatorCharacterId IS NULL
          OR RecipientCharacterId <> InitiatorCharacterId),
    CHECK(RepetitionMode <> 'ONCE_PER_OCCURRENCE' OR AnchorMemoryKey IS NOT NULL),
    CHECK(RepetitionMode = 'ONCE_PER_OCCURRENCE' OR AnchorMemoryKey IS NULL),
    CHECK(IsActive = 0 OR (ConditionJson IS NOT NULL AND length(trim(ConditionJson)) > 0))
);
INSERT INTO SpecialEncounters_AnyRecipientMigration
    (Id, EncounterKey, Title, InteractionId, RecipientCharacterId,
     InitiatorCharacterId, LaunchMode, RepetitionMode, Priority, IsActive,
     ConditionJson, AnchorMemoryKey, AnchorActorRef, AnchorAffectedRef,
     AnchorKnowerRef)
SELECT Id, EncounterKey, Title, InteractionId, RecipientCharacterId,
       InitiatorCharacterId, LaunchMode, RepetitionMode, Priority, IsActive,
       ConditionJson, AnchorMemoryKey, AnchorActorRef, AnchorAffectedRef,
       AnchorKnowerRef
FROM SpecialEncounters;
DROP TABLE SpecialEncounters;
ALTER TABLE SpecialEncounters_AnyRecipientMigration RENAME TO SpecialEncounters;
CREATE INDEX IX_SpecialEncounters_RecipientPriority
    ON SpecialEncounters(RecipientCharacterId, IsActive, Priority DESC, Id);
CREATE INDEX IX_SpecialEncounters_Interaction
    ON SpecialEncounters(InteractionId);
