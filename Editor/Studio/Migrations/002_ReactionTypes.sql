-- Phase 6E. Applied once to the authoring master, inside a transaction after SQLite backup.
-- Existing Godot clients are compatible: Options and ReactionImages retain their columns.
CREATE TABLE ReactionTypes (
    Key TEXT PRIMARY KEY COLLATE NOCASE
        CHECK(length(Key) > 0 AND Key = UPPER(Key) AND Key NOT GLOB '*[^A-Z0-9_]*'),
    DisplayName TEXT NOT NULL CHECK(length(trim(DisplayName)) > 0),
    Description TEXT NOT NULL DEFAULT '',
    SortOrder INTEGER NOT NULL DEFAULT 0 CHECK(SortOrder >= 0)
);

INSERT INTO ReactionTypes(Key, DisplayName, SortOrder) VALUES
    ('ACCEPTING', 'Accepting', 0),
    ('AMUSED', 'Amused', 10),
    ('ANGRY', 'Angry', 20),
    ('COLD', 'Cold', 30),
    ('CONSIDERING', 'Considering', 40),
    ('DISMISSIVE', 'Dismissive', 50),
    ('FLATTERED', 'Flattered', 60),
    ('IMPRESSED', 'Impressed', 70),
    ('INTERESTED', 'Interested', 80),
    ('INTIMIDATED', 'Intimidated', 90),
    ('OFFENDED', 'Offended', 100),
    ('PLEASED', 'Pleased', 110),
    ('RECEPTIVE', 'Receptive', 120),
    ('REFLECTIVE', 'Reflective', 130),
    ('RELIEVED', 'Relieved', 140),
    ('SUSPICIOUS', 'Suspicious', 150),
    ('UNIMPRESSED', 'Unimpressed', 160),
    ('UNINTERESTED', 'Uninterested', 170),
    ('UNMOVED', 'Unmoved', 180),
    ('WARM', 'Warm', 190),
    ('WARY', 'Wary', 200);

-- Keep any custom/emergent keys used in previous content. Import once, do not infer keys at runtime.
INSERT OR IGNORE INTO ReactionTypes(Key, DisplayName, Description, SortOrder)
SELECT UPPER(TRIM(ReactionKey)), UPPER(TRIM(ReactionKey)), 'Migrated from existing reaction artwork', 10000
FROM ReactionImages WHERE TRIM(ReactionKey) <> '';
INSERT OR IGNORE INTO ReactionTypes(Key, DisplayName, Description, SortOrder)
SELECT UPPER(TRIM(SuccessTargetReaction)), UPPER(TRIM(SuccessTargetReaction)), 'Migrated from existing success choice', 10000
FROM Options WHERE TRIM(SuccessTargetReaction) <> '';
INSERT OR IGNORE INTO ReactionTypes(Key, DisplayName, Description, SortOrder)
SELECT UPPER(TRIM(FailureTargetReaction)), UPPER(TRIM(FailureTargetReaction)), 'Migrated from existing failure choice', 10000
FROM Options WHERE TRIM(FailureTargetReaction) <> '';

-- Track authoring-schema upgrades so future changes have a clear history.
CREATE TABLE IF NOT EXISTS ContentSchemaMigrations (
    Version TEXT PRIMARY KEY,
    AppliedUtc TEXT NOT NULL
);
INSERT OR IGNORE INTO ContentSchemaMigrations(Version, AppliedUtc)
VALUES('002_ReactionTypes', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

-- Database-level referential enforcement without rebuilding production tables.
-- SQLite cannot ADD a foreign key constraint to an existing column without rebuilding the table.
CREATE TRIGGER reaction_options_insert BEFORE INSERT ON Options
BEGIN
    SELECT RAISE(ABORT, 'Unknown success reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.SuccessTargetReaction);
    SELECT RAISE(ABORT, 'Unknown failure reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.FailureTargetReaction);
END;
CREATE TRIGGER reaction_options_update BEFORE UPDATE OF SuccessTargetReaction,FailureTargetReaction ON Options
BEGIN
    SELECT RAISE(ABORT, 'Unknown success reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.SuccessTargetReaction);
    SELECT RAISE(ABORT, 'Unknown failure reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.FailureTargetReaction);
END;
CREATE TRIGGER reaction_images_insert BEFORE INSERT ON ReactionImages
BEGIN
    SELECT RAISE(ABORT, 'Unknown portrait reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.ReactionKey);
END;
CREATE TRIGGER reaction_images_update BEFORE UPDATE OF ReactionKey ON ReactionImages
BEGIN
    SELECT RAISE(ABORT, 'Unknown portrait reaction type')
    WHERE NOT EXISTS (SELECT 1 FROM ReactionTypes WHERE Key=NEW.ReactionKey);
END;
CREATE TRIGGER reaction_types_delete BEFORE DELETE ON ReactionTypes
BEGIN
    SELECT RAISE(ABORT, 'Reaction type is still in use') WHERE EXISTS (
        SELECT 1 FROM Options WHERE SuccessTargetReaction=OLD.Key OR FailureTargetReaction=OLD.Key
    ) OR EXISTS (
        SELECT 1 FROM ReactionImages WHERE ReactionKey=OLD.Key
    );
END;
