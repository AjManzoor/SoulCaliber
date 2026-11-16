-- Phase 9: dynamic, game-defined relationship attributes. Stable keys are the source of truth.
-- Applies to the MASTER content database only; the emptied savegame is rebuilt separately in Godot.
CREATE TABLE RelationshipAttributes (
    Key TEXT PRIMARY KEY COLLATE NOCASE NOT NULL
        CHECK(length(Key) BETWEEN 1 AND 48 AND Key=upper(Key)),
    Name TEXT NOT NULL CHECK(length(trim(Name)) > 0),
    Description TEXT NOT NULL DEFAULT '',
    MinValue INTEGER NOT NULL,
    MaxValue INTEGER NOT NULL,
    DefaultValue INTEGER NOT NULL DEFAULT 0,
    IconPath TEXT NOT NULL DEFAULT '',
    Color TEXT NOT NULL DEFAULT '#D6DAE7',
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1)),
    CHECK(MinValue >= -1000 AND MaxValue <= 1000 AND MinValue < MaxValue),
    CHECK(DefaultValue BETWEEN MinValue AND MaxValue)
);
INSERT INTO RelationshipAttributes
  (Key,Name,Description,MinValue,MaxValue,DefaultValue,IconPath,Color,SortOrder)
VALUES
 ('AFFECTION','Affection','Fondness and emotional attachment.',-100,100,0,'res://Assets/RelationshipIcons/affection.svg','#F0789A',10),
 ('ATTRACTION','Attraction','Romantic or physical interest.',0,100,0,'res://Assets/RelationshipIcons/attraction.svg','#E3A2D2',20),
 ('TRUST','Trust','Belief in someone’s reliability and honesty.',-100,100,0,'res://Assets/RelationshipIcons/trust.svg','#70BCE9',30),
 ('RESPECT','Respect','Admiration for ability, character, or status.',-100,100,0,'res://Assets/RelationshipIcons/respect.svg','#E4C374',40),
 ('FEAR','Fear','Perceived threat or intimidation.',0,100,0,'res://Assets/RelationshipIcons/fear.svg','#BA97E1',50),
 ('LOYALTY','Loyalty','Willingness to support someone even at a cost.',-100,100,0,'res://Assets/RelationshipIcons/loyalty.svg','#79D7AE',60),
 ('RESENTMENT','Resentment','Lingering bitterness and perceived grievances.',0,100,0,'res://Assets/RelationshipIcons/resentment.svg','#EA8E72',70),
 ('RIVALRY','Rivalry','Desire to challenge or surpass someone.',0,100,0,'res://Assets/RelationshipIcons/rivalry.svg','#9DB4E8',80),
 ('ENVY','Envy','Desire for what someone else possesses.',0,100,0,'res://Assets/RelationshipIcons/envy.svg','#98CA87',90),
 ('DEPENDENCE','Dependence','Perceived need for another character.',0,100,0,'res://Assets/RelationshipIcons/dependence.svg','#D0AD88',100);

-- Both legacy tables restricted attribute names to six fixed literals. Rebuild them so
-- *all future* attributes may be referenced without a new migration. Preserve content.
CREATE TABLE CheckProfileModifiers_V6 (
  ProfileKey TEXT NOT NULL COLLATE NOCASE REFERENCES CheckProfiles(Key) ON DELETE CASCADE,
  Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR')),
  Attribute TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipAttributes(Key) ON DELETE RESTRICT,
  Factor REAL NOT NULL CHECK(Factor BETWEEN -10 AND 10),
  PRIMARY KEY(ProfileKey,Direction,Attribute)
);
INSERT INTO CheckProfileModifiers_V6(ProfileKey,Direction,Attribute,Factor)
 SELECT ProfileKey,Direction,
  CASE upper(Attribute) WHEN 'LOVE' THEN 'AFFECTION' WHEN 'ROMANCE' THEN 'ATTRACTION'
   WHEN 'ALLYSHIP' THEN 'LOYALTY' ELSE upper(Attribute) END,Factor
 FROM CheckProfileModifiers;
DROP TABLE CheckProfileModifiers;
ALTER TABLE CheckProfileModifiers_V6 RENAME TO CheckProfileModifiers;

CREATE TABLE ConditionalDialogueConditions_V6 (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 VariantId INTEGER NOT NULL REFERENCES ConditionalDialogueVariants(Id) ON DELETE CASCADE,
 Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR')),
 Attribute TEXT NOT NULL COLLATE NOCASE REFERENCES RelationshipAttributes(Key) ON DELETE RESTRICT,
 Minimum INTEGER NULL, Maximum INTEGER NULL,
 CHECK(Minimum IS NOT NULL OR Maximum IS NOT NULL),
 CHECK(Minimum IS NULL OR Minimum BETWEEN -1000 AND 1000),
 CHECK(Maximum IS NULL OR Maximum BETWEEN -1000 AND 1000),
 CHECK(Minimum IS NULL OR Maximum IS NULL OR Minimum <= Maximum),
 UNIQUE(VariantId,Direction,Attribute)
);
INSERT INTO ConditionalDialogueConditions_V6(Id,VariantId,Direction,Attribute,Minimum,Maximum)
 SELECT Id,VariantId,Direction,
  CASE upper(Attribute) WHEN 'LOVE' THEN 'AFFECTION' WHEN 'ROMANCE' THEN 'ATTRACTION'
   WHEN 'ALLYSHIP' THEN 'LOYALTY' ELSE upper(Attribute) END,Minimum,Maximum
 FROM ConditionalDialogueConditions;
DROP TABLE ConditionalDialogueConditions;
ALTER TABLE ConditionalDialogueConditions_V6 RENAME TO ConditionalDialogueConditions;
CREATE INDEX IX_ConditionalConditions_Variant ON ConditionalDialogueConditions(VariantId);

-- These tables already use free-form TEXT but their old keys must be canonicalised.
UPDATE Requirements SET Attribute = CASE upper(Attribute)
 WHEN 'LOVE' THEN 'AFFECTION' WHEN 'ROMANCE' THEN 'ATTRACTION'
 WHEN 'ALLYSHIP' THEN 'LOYALTY' ELSE upper(Attribute) END;
UPDATE ChanceModifiers SET Attribute = CASE upper(Attribute)
 WHEN 'LOVE' THEN 'AFFECTION' WHEN 'ROMANCE' THEN 'ATTRACTION'
 WHEN 'ALLYSHIP' THEN 'LOYALTY' ELSE upper(Attribute) END;
UPDATE Effects SET Attribute = CASE upper(Attribute)
 WHEN 'LOVE' THEN 'AFFECTION' WHEN 'ROMANCE' THEN 'ATTRACTION'
 WHEN 'ALLYSHIP' THEN 'LOYALTY' ELSE upper(Attribute) END;
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
 VALUES('006_DynamicRelationships',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
