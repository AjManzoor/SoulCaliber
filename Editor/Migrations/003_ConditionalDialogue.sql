-- Phase 7: Optional variations on existing introductions/phrases.
-- Base NodeIntroductions and Lines remain untouched as fallbacks.
CREATE TABLE IF NOT EXISTS ConditionalDialogueVariants (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NodeId INTEGER NULL REFERENCES Nodes(Id) ON DELETE CASCADE,
    OptionId INTEGER NULL REFERENCES Options(Id) ON DELETE CASCADE,
    CharacterId TEXT NOT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    VisitorId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    Outcome TEXT NULL CHECK (Outcome IN ('SUCCESS','FAILURE')),
    Priority INTEGER NOT NULL DEFAULT 0 CHECK (Priority BETWEEN 0 AND 1000),
    Text TEXT NOT NULL CHECK (length(trim(Text)) > 0),
    ImagePath TEXT NOT NULL DEFAULT '',
    CHECK ((NodeId IS NOT NULL AND OptionId IS NULL AND Outcome IS NULL AND ImagePath = '') OR
           (NodeId IS NULL AND OptionId IS NOT NULL)),
    CHECK (VisitorId IS NULL OR VisitorId <> CharacterId)
);
CREATE INDEX IF NOT EXISTS IX_ConditionalVariants_Node ON ConditionalDialogueVariants(NodeId, CharacterId, VisitorId);
CREATE INDEX IF NOT EXISTS IX_ConditionalVariants_Option ON ConditionalDialogueVariants(OptionId, CharacterId, VisitorId, Outcome);

CREATE TABLE IF NOT EXISTS ConditionalDialogueConditions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    VariantId INTEGER NOT NULL REFERENCES ConditionalDialogueVariants(Id) ON DELETE CASCADE,
    Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR')),
    Attribute TEXT NOT NULL CHECK(Attribute IN ('Love','Fear','Allyship','Respect','Trust','Romance')),
    Minimum INTEGER NULL,
    Maximum INTEGER NULL,
    CHECK(Minimum IS NOT NULL OR Maximum IS NOT NULL),
    CHECK(Minimum IS NULL OR Minimum BETWEEN -100 AND 100),
    CHECK(Maximum IS NULL OR Maximum BETWEEN -100 AND 100),
    CHECK(Minimum IS NULL OR Maximum IS NULL OR Minimum <= Maximum),
    UNIQUE(VariantId,Direction,Attribute)
);
CREATE INDEX IF NOT EXISTS IX_ConditionalConditions_Variant ON ConditionalDialogueConditions(VariantId);
