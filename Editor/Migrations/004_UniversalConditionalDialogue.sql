-- Phase 7B: allow universal relationship-conditional dialogue.
-- Blank speaking character/other character are stored as NULL and mean ANY.
-- Runs only against the master database, automatically from Studio after backup.
-- Migration runner disables foreign_keys OUTSIDE its transaction, then checks and restores them.
CREATE TABLE ConditionalDialogueVariants_New (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NodeId INTEGER NULL REFERENCES Nodes(Id) ON DELETE CASCADE,
    OptionId INTEGER NULL REFERENCES Options(Id) ON DELETE CASCADE,
    CharacterId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    VisitorId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    Outcome TEXT NULL CHECK (Outcome IN ('SUCCESS','FAILURE')),
    Priority INTEGER NOT NULL DEFAULT 0 CHECK (Priority BETWEEN 0 AND 1000),
    Text TEXT NOT NULL CHECK (length(trim(Text)) > 0),
    ImagePath TEXT NOT NULL DEFAULT '',
    CHECK ((NodeId IS NOT NULL AND OptionId IS NULL AND Outcome IS NULL AND ImagePath = '') OR
           (NodeId IS NULL AND OptionId IS NOT NULL)),
    CHECK (VisitorId IS NULL OR CharacterId IS NULL OR VisitorId <> CharacterId)
);
INSERT INTO ConditionalDialogueVariants_New
    (Id,NodeId,OptionId,CharacterId,VisitorId,Outcome,Priority,Text,ImagePath)
SELECT Id,NodeId,OptionId,CharacterId,VisitorId,Outcome,Priority,Text,ImagePath
FROM ConditionalDialogueVariants;
DROP TABLE ConditionalDialogueVariants;
ALTER TABLE ConditionalDialogueVariants_New RENAME TO ConditionalDialogueVariants;
CREATE INDEX IX_ConditionalVariants_Node
    ON ConditionalDialogueVariants(NodeId, CharacterId, VisitorId);
CREATE INDEX IX_ConditionalVariants_Option
    ON ConditionalDialogueVariants(OptionId, CharacterId, VisitorId, Outcome);
CREATE TABLE IF NOT EXISTS ContentSchemaMigrations (
    Version TEXT PRIMARY KEY, AppliedUtc TEXT NOT NULL
);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version, AppliedUtc)
VALUES ('004_UniversalConditionalDialogue', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
