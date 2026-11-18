-- Phase 13H-1: opt-in custom audience rules. Absence means preserve legacy
-- QUIET / ALLIES / GLOBAL semantics. Empty custom specs are valid secrets.
CREATE TABLE MemoryTypeKnowledgeAudiences (
    MemoryKey TEXT PRIMARY KEY REFERENCES MemoryTypes(Key) ON DELETE CASCADE,
    AudienceJson TEXT NOT NULL CHECK(length(AudienceJson) > 0)
);
CREATE TABLE OptionMemoryEffectKnowledgeAudiences (
    OptionMemoryEffectId INTEGER PRIMARY KEY REFERENCES OptionMemoryEffects(Id) ON DELETE CASCADE,
    AudienceJson TEXT NOT NULL CHECK(length(AudienceJson) > 0)
);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
    VALUES('021_KnowledgeAudiences',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
