-- Phase 9: named, reusable success-chance relationship profiles.
-- Keep SkillChecks.CheckMode as the string profile key for backwards compatibility.
-- Old STANDARD / INTIMIDATION calculations become editable table data.
CREATE TABLE CheckProfiles (
    Key TEXT PRIMARY KEY COLLATE NOCASE NOT NULL
        CHECK(length(trim(Key)) > 0),
    Name TEXT NOT NULL CHECK(length(trim(Name)) > 0),
    Description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE CheckProfileModifiers (
    ProfileKey TEXT NOT NULL COLLATE NOCASE REFERENCES CheckProfiles(Key) ON DELETE CASCADE,
    Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR')),
    Attribute TEXT NOT NULL CHECK(Attribute IN ('Love','Fear','Allyship','Respect','Trust','Romance')),
    Factor REAL NOT NULL CHECK(Factor BETWEEN -10 AND 10),
    PRIMARY KEY (ProfileKey, Direction, Attribute)
);
INSERT INTO CheckProfiles(Key, Name, Description) VALUES
    ('STANDARD','Standard','Legacy default: the recipient’s trust and respect affect success.'),
    ('INTIMIDATION','Intimidation','Legacy threat: recipient fear increases success; trust decreases it.'),
    ('DECEPTIVE','Deceptive','Suggested default for lying: more trust makes a lie easier to believe.'),
    ('CHARMING','Charming','Suggested social profile; tune before balancing the game.'),
    ('COOPERATIVE','Cooperative','Suggested teamwork profile; tune before balancing the game.'),
    ('NEUTRAL','Neutral','No automatic relationship adjustments.');
INSERT INTO CheckProfileModifiers(ProfileKey, Direction, Attribute, Factor) VALUES
    ('STANDARD','PARTNER_TO_ACTOR','Trust',0.20),
    ('STANDARD','PARTNER_TO_ACTOR','Respect',0.10),
    ('INTIMIDATION','PARTNER_TO_ACTOR','Fear',0.10),
    ('INTIMIDATION','PARTNER_TO_ACTOR','Trust',-0.05),
    ('DECEPTIVE','PARTNER_TO_ACTOR','Trust',0.15),
    ('CHARMING','PARTNER_TO_ACTOR','Love',0.10),
    ('CHARMING','PARTNER_TO_ACTOR','Romance',0.10),
    ('COOPERATIVE','PARTNER_TO_ACTOR','Allyship',0.15),
    ('COOPERATIVE','PARTNER_TO_ACTOR','Respect',0.05);

-- Profile assignment is independent of whether an optional skill check exists.
CREATE TABLE OptionCheckProfiles (
    OptionId INTEGER PRIMARY KEY REFERENCES Options(Id) ON DELETE CASCADE,
    ProfileKey TEXT NOT NULL COLLATE NOCASE REFERENCES CheckProfiles(Key) ON DELETE RESTRICT
);
-- Preserve each existing choice's legacy CheckMode. Unassigned choices use STANDARD.
INSERT INTO OptionCheckProfiles(OptionId,ProfileKey)
    SELECT OptionId, CheckMode FROM SkillChecks;
