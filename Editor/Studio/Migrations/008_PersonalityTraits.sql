-- Phase 10B: reusable personality tags modify EFFECT DELTAS, not skill checks.
-- Safe to apply only after 006_DynamicRelationships and 007_DynamicSkills.
CREATE TABLE PersonalityTraits (
  Key TEXT PRIMARY KEY COLLATE NOCASE,
  Name TEXT NOT NULL CHECK (length(trim(Name)) > 0),
  Description TEXT NOT NULL DEFAULT '',
  SortOrder INTEGER NOT NULL DEFAULT 100,
  IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1))
);
CREATE TABLE CharacterTraits (
  CharacterId TEXT NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
  TraitKey TEXT NOT NULL REFERENCES PersonalityTraits(Key) ON DELETE RESTRICT,
  PRIMARY KEY(CharacterId,TraitKey)
);
CREATE INDEX IX_CharacterTraits_Trait ON CharacterTraits(TraitKey);
CREATE TABLE PersonalityTraitModifiers (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TraitKey TEXT NOT NULL REFERENCES PersonalityTraits(Key) ON DELETE CASCADE,
  AttributeKey TEXT NOT NULL REFERENCES RelationshipAttributes(Key) ON DELETE RESTRICT,
  Outcome TEXT NOT NULL DEFAULT 'BOTH' CHECK(Outcome IN ('BOTH','SUCCESS','FAILURE')),
  Change TEXT NOT NULL DEFAULT 'BOTH' CHECK(Change IN ('BOTH','GAIN','LOSS')),
  Factor REAL NOT NULL DEFAULT 1.0 CHECK(Factor >= 0 AND Factor <= 3),
  UNIQUE(TraitKey,AttributeKey,Outcome,Change)
);
INSERT INTO PersonalityTraits(Key,Name,Description,SortOrder) VALUES
 ('BRAVE','Brave','Resists increases in fear',10),
 ('TIMID','Timid','Becomes frightened more easily',20),
 ('VINDICTIVE','Vindictive','Tends to develop resentment',30),
 ('FORGIVING','Forgiving','Develops resentment less readily',40),
 ('SUSPICIOUS','Suspicious','Slow to trust; quicker to lose trust',50);
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'BRAVE','FEAR','BOTH','GAIN',0.5 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='FEAR');
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'TIMID','FEAR','BOTH','GAIN',1.75 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='FEAR');
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'VINDICTIVE','RESENTMENT','BOTH','GAIN',1.5 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='RESENTMENT');
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'FORGIVING','RESENTMENT','BOTH','GAIN',0.5 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='RESENTMENT');
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'SUSPICIOUS','TRUST','BOTH','GAIN',0.5 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='TRUST');
INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
 SELECT 'SUSPICIOUS','TRUST','BOTH','LOSS',1.5 WHERE EXISTS(SELECT 1 FROM RelationshipAttributes WHERE Key='TRUST');
INSERT OR IGNORE INTO ContentSchemaMigrations(Version,AppliedUtc)
 VALUES('008_PersonalityTraits',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
