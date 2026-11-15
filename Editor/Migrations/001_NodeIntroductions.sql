-- Phase 5 master-content migration. The WPF editor applies this automatically
-- (after creating a consistent database backup) when it first opens a Phase 4 DB.
-- DO NOT apply this directly to a published content.db or player save database.
CREATE TABLE IF NOT EXISTS NodeIntroductions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NodeId INTEGER NOT NULL REFERENCES Nodes(Id) ON DELETE CASCADE,
    CharacterId TEXT NOT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    VisitorId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
    Text TEXT NOT NULL CHECK(length(trim(Text)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS UX_NodeIntro_Character
    ON NodeIntroductions(NodeId, CharacterId COLLATE NOCASE)
    WHERE VisitorId IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UX_NodeIntro_Pair
    ON NodeIntroductions(NodeId, CharacterId COLLATE NOCASE, VisitorId COLLATE NOCASE)
    WHERE VisitorId IS NOT NULL;
