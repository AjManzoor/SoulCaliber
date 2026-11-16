-- Choice requirements normally render a visible, disabled choice.
-- HIDE removes unmet choices from the offered actions; the choice remains
-- authored and can reappear if its current requirements become satisfied.
ALTER TABLE Options ADD COLUMN UnmetRequirementBehaviour TEXT NOT NULL DEFAULT 'LOCK'
    CHECK(UnmetRequirementBehaviour IN ('LOCK', 'HIDE'));
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
VALUES('014_ChoiceRequirementVisibility',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
