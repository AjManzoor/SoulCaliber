-- Representative version-1 save layout matching the 13G-5B/5C contract.

CREATE TABLE "InteractionHistory" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_InteractionHistory" PRIMARY KEY AUTOINCREMENT,
  "OccurredUtc" TEXT NOT NULL, "InteractionKey" TEXT NOT NULL,
  "NodeKey" TEXT NOT NULL, "OptionKey" TEXT NOT NULL,
  "ActorId" TEXT NOT NULL, "PartnerId" TEXT NOT NULL,
  "Succeeded" INTEGER NOT NULL, "Chance" INTEGER NOT NULL,
  "Roll" INTEGER NOT NULL, "TargetReaction" TEXT NOT NULL
);
CREATE TABLE "SavedRelationshipValues" (
  "FromCharacterId" TEXT NOT NULL, "ToCharacterId" TEXT NOT NULL,
  "AttributeKey" TEXT NOT NULL, "Value" INTEGER NOT NULL,
  CONSTRAINT "PK_SavedRelationshipValues" PRIMARY KEY
    ("FromCharacterId", "ToCharacterId", "AttributeKey")
);
CREATE TABLE "RelationshipChanges" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_RelationshipChanges" PRIMARY KEY AUTOINCREMENT,
  "SavedInteractionId" INTEGER NOT NULL, "FromCharacterId" TEXT NOT NULL,
  "ToCharacterId" TEXT NOT NULL, "Attribute" TEXT NOT NULL,
  "Before" INTEGER NOT NULL, "After" INTEGER NOT NULL,
  FOREIGN KEY("SavedInteractionId") REFERENCES "InteractionHistory"("Id") ON DELETE CASCADE
);
CREATE TABLE "SavedInteractionStatusChanges" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_SavedInteractionStatusChanges" PRIMARY KEY AUTOINCREMENT,
  "SavedInteractionId" INTEGER NOT NULL, "FromCharacterId" TEXT NOT NULL,
  "ToCharacterId" TEXT NOT NULL, "Before" TEXT NOT NULL, "After" TEXT NOT NULL,
  FOREIGN KEY("SavedInteractionId") REFERENCES "InteractionHistory"("Id") ON DELETE CASCADE
);
CREATE INDEX "IX_InteractionHistory_OccurredUtc" ON "InteractionHistory"("OccurredUtc");
CREATE INDEX "IX_RelationshipChanges_SavedInteractionId" ON "RelationshipChanges"("SavedInteractionId");
CREATE INDEX "IX_SavedInteractionStatusChanges_SavedInteractionId" ON "SavedInteractionStatusChanges"("SavedInteractionId");


        CREATE TABLE SavedCharacterStatusOverrides (
            FromCharacterId TEXT NOT NULL COLLATE NOCASE,
            ToCharacterId TEXT NOT NULL COLLATE NOCASE,
            StatusKey TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY(FromCharacterId,ToCharacterId),
            CHECK(FromCharacterId <> ToCharacterId COLLATE NOCASE)
        ) WITHOUT ROWID;
        CREATE TABLE SavedMemoryOccurrences (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MemoryKey TEXT NOT NULL COLLATE NOCASE,
            ActorCharacterId TEXT NOT NULL COLLATE NOCASE,
            PlayerCharacterId TEXT NOT NULL COLLATE NOCASE,
            Severity TEXT NOT NULL CHECK(Severity IN ('MAJOR','MINOR')),
            KnowledgeLevel TEXT NOT NULL CHECK(KnowledgeLevel IN ('QUIET','ALLIES','GLOBAL')),
            OccurredUtc TEXT NOT NULL
        );
        CREATE INDEX IX_SavedMemoryOccurrences_MemoryKeyActor
            ON SavedMemoryOccurrences(MemoryKey, ActorCharacterId);
        CREATE TABLE SavedMemoryAffectedCharacters (
            MemoryId INTEGER NOT NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CharacterId TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY (MemoryId,CharacterId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_SavedMemoryAffectedCharacters_Character
            ON SavedMemoryAffectedCharacters(CharacterId,MemoryId);
        CREATE TABLE SavedMemoryKnowledge (
            MemoryId INTEGER NOT NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CharacterId TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY (MemoryId,CharacterId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_SavedMemoryKnowledge_Character
            ON SavedMemoryKnowledge(CharacterId,MemoryId);
        CREATE TABLE SavedSpecialEncounterCompletions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EncounterKey TEXT NOT NULL COLLATE NOCASE,
            InitiatorCharacterId TEXT NOT NULL COLLATE NOCASE,
            RecipientCharacterId TEXT NOT NULL COLLATE NOCASE,
            MemoryOccurrenceId INTEGER NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CompletedUtc TEXT NOT NULL,
            CHECK(InitiatorCharacterId <> RecipientCharacterId COLLATE NOCASE)
        );
        CREATE UNIQUE INDEX UX_SpecialEncounter_Pair
            ON SavedSpecialEncounterCompletions
                (EncounterKey, InitiatorCharacterId, RecipientCharacterId)
            WHERE MemoryOccurrenceId IS NULL;
        CREATE UNIQUE INDEX UX_SpecialEncounter_Occurrence
            ON SavedSpecialEncounterCompletions
                (EncounterKey, InitiatorCharacterId, RecipientCharacterId, MemoryOccurrenceId)
            WHERE MemoryOccurrenceId IS NOT NULL;
    
