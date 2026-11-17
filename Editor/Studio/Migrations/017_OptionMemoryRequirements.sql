-- Phase 13D: per-choice nested AND/OR/NOT memory condition tree.
-- The authored JSON is validated in Studio and on Godot load.
CREATE TABLE OptionMemoryRequirements (
    OptionId INTEGER PRIMARY KEY REFERENCES Options(Id) ON DELETE CASCADE,
    ExpressionJson TEXT NOT NULL CHECK(length(trim(ExpressionJson)) > 0)
);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('017_OptionMemoryRequirements',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
