-- Phase 13C: authored effects that create memories when dialogue resolves.
CREATE TABLE OptionMemoryEffects (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OptionId INTEGER NOT NULL REFERENCES Options(Id) ON DELETE CASCADE,
    MemoryKey TEXT NOT NULL REFERENCES MemoryTypes(Key) ON DELETE RESTRICT,
    Trigger TEXT NOT NULL CHECK(Trigger IN ('SELECTED','SUCCESS','FAILURE','BOTH')),
    Affected TEXT NOT NULL CHECK(Affected IN ('ACTOR','PARTNER','BOTH')),
    SeverityOverride TEXT NULL CHECK(SeverityOverride IS NULL OR SeverityOverride IN ('MAJOR','MINOR')),
    KnowledgeOverride TEXT NULL CHECK(KnowledgeOverride IS NULL OR KnowledgeOverride IN ('QUIET','ALLIES','GLOBAL'))
);
CREATE INDEX IX_OptionMemoryEffects_Option ON OptionMemoryEffects(OptionId);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
    VALUES('016_OptionMemoryEffects',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
