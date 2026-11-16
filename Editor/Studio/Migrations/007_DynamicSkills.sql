-- Phase 10A: game-defined character skills only. Personality sensitivities follow in Phase 10B.
-- Does not modify Characters, artwork, relationship catalogue, or savegame database.
-- Old Personalities records are retained for reference but not read by the new engine.
CREATE TABLE CharacterSkills (
 Key TEXT PRIMARY KEY COLLATE NOCASE NOT NULL CHECK(Key = upper(Key) AND length(Key) BETWEEN 1 AND 48),
 Name TEXT NOT NULL CHECK(length(trim(Name)) > 0),
 Description TEXT NOT NULL DEFAULT '',
 MinValue INTEGER NOT NULL DEFAULT 0,
 MaxValue INTEGER NOT NULL DEFAULT 100,
 DefaultValue INTEGER NOT NULL DEFAULT 50,
 SortOrder INTEGER NOT NULL DEFAULT 0,
 IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
 CHECK(MinValue >= -1000 AND MaxValue <= 1000 AND MinValue < MaxValue),
 CHECK(DefaultValue BETWEEN MinValue AND MaxValue)
);
INSERT INTO CharacterSkills(Key,Name,Description,SortOrder) VALUES
 ('MIGHT','Might','Physical strength and raw power.',10),
 ('FINESSE','Finesse','Agility, precision, technique and coordination.',20),
 ('INTELLECT','Intellect','Knowledge, intelligence and reasoning.',30),
 ('CUNNING','Cunning','Deception, manipulation and tactical ingenuity.',40),
 ('CHARISMA','Charisma','Charm, persuasion and social influence.',50),
 ('WILLPOWER','Willpower','Mental fortitude, discipline and determination.',60);

CREATE TABLE CharacterSkillValues (
 CharacterId TEXT NOT NULL COLLATE NOCASE REFERENCES Characters(Id) ON DELETE CASCADE,
 SkillKey TEXT NOT NULL COLLATE NOCASE REFERENCES CharacterSkills(Key) ON DELETE RESTRICT,
 Value INTEGER NOT NULL,
 PRIMARY KEY(CharacterId,SkillKey)
);
-- The six old skills have different meanings; do not silently transfer their values.
-- New characters and existing characters alike use the catalogue default (50) until edited.
INSERT INTO CharacterSkillValues(CharacterId,SkillKey,Value)
 SELECT c.Id,s.Key,s.DefaultValue FROM Characters c CROSS JOIN CharacterSkills s;

INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
 VALUES('007_DynamicSkills',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
