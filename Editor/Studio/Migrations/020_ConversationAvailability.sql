-- Phase 13E-4A: normal conversation eligibility in master content.
-- These are repeatable menu topics, not special encounters and not savegame completions.
CREATE TABLE ConversationAvailability (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AvailabilityKey TEXT NOT NULL COLLATE NOCASE UNIQUE
        CHECK(length(AvailabilityKey) BETWEEN 1 AND 48),
    Title TEXT NOT NULL CHECK(length(trim(Title)) BETWEEN 1 AND 120),
    InteractionId INTEGER NOT NULL REFERENCES Interactions(Id) ON DELETE RESTRICT,
    RecipientCharacterId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    InitiatorCharacterId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    DisplayOrder INTEGER NOT NULL DEFAULT 100 CHECK(DisplayOrder BETWEEN 0 AND 10000),
    IsActive INTEGER NOT NULL DEFAULT 0 CHECK(IsActive IN (0, 1)),
    ConditionJson TEXT NULL,
    CHECK (RecipientCharacterId IS NULL OR InitiatorCharacterId IS NULL
           OR RecipientCharacterId <> InitiatorCharacterId)
);
CREATE INDEX IX_ConversationAvailability_RecipientSort
    ON ConversationAvailability(RecipientCharacterId, IsActive, DisplayOrder, Id);
CREATE INDEX IX_ConversationAvailability_Interaction
    ON ConversationAvailability(InteractionId);
