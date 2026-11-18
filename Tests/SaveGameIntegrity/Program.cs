using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using SoulCaliber.Dialogue;

var passed = 0;
var failed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); failed++; }
}

var root = Path.Combine(Path.GetTempPath(), "soulcaliber-integrity-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var savePath = Path.Combine(root, "save.db");
    var contentPath = Path.Combine(root, "content.db");

    SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }
    void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    void CreateFixtures()
    {
        using (var save = Open(savePath))
        {
            Exec(save, @"
                CREATE TABLE SavedRelationshipValues (FromCharacterId TEXT, ToCharacterId TEXT, AttributeKey TEXT, Value INTEGER);
                CREATE TABLE InteractionHistory (Id INTEGER PRIMARY KEY, OccurredUtc TEXT, InteractionKey TEXT,
                    NodeKey TEXT, OptionKey TEXT, ActorId TEXT, PartnerId TEXT, Succeeded INTEGER, Chance INTEGER, Roll INTEGER, TargetReaction TEXT);
                CREATE TABLE RelationshipChanges (Id INTEGER PRIMARY KEY, SavedInteractionId INTEGER REFERENCES InteractionHistory(Id),
                    FromCharacterId TEXT, ToCharacterId TEXT, Attribute TEXT, Before INTEGER, After INTEGER);
                CREATE TABLE SavedInteractionStatusChanges (Id INTEGER PRIMARY KEY, SavedInteractionId INTEGER REFERENCES InteractionHistory(Id),
                    FromCharacterId TEXT, ToCharacterId TEXT, Before TEXT, After TEXT);
                CREATE TABLE SavedCharacterStatusOverrides (FromCharacterId TEXT, ToCharacterId TEXT, StatusKey TEXT);
                CREATE TABLE SavedMemoryOccurrences (Id INTEGER PRIMARY KEY, MemoryKey TEXT, ActorCharacterId TEXT,
                    PlayerCharacterId TEXT, Severity TEXT, KnowledgeLevel TEXT, OccurredUtc TEXT);
                CREATE TABLE SavedMemoryAffectedCharacters (MemoryId INTEGER REFERENCES SavedMemoryOccurrences(Id), CharacterId TEXT);
                CREATE TABLE SavedMemoryKnowledge (MemoryId INTEGER REFERENCES SavedMemoryOccurrences(Id), CharacterId TEXT);
                CREATE TABLE SavedSpecialEncounterCompletions (Id INTEGER PRIMARY KEY, EncounterKey TEXT,
                    InitiatorCharacterId TEXT, RecipientCharacterId TEXT,
                    MemoryOccurrenceId INTEGER REFERENCES SavedMemoryOccurrences(Id), CompletedUtc TEXT);
                PRAGMA user_version=1;
                INSERT INTO SavedRelationshipValues VALUES ('LAW','YOURI','TRUST',60);
                INSERT INTO SavedMemoryOccurrences VALUES (10, 'DEFEAT','LAW','LAW','MAJOR','GLOBAL','2026-11-17');
                INSERT INTO SavedMemoryAffectedCharacters VALUES (10,'YOURI');
                INSERT INTO SavedSpecialEncounterCompletions VALUES (1,'DEFEATED','LAW','YOURI',10,'2026-11-17');
            ");
        }
        using (var content = Open(contentPath))
        {
            Exec(content, @"
                CREATE TABLE Characters (Id TEXT PRIMARY KEY, Name TEXT);
                CREATE TABLE MemoryTypes (Key TEXT PRIMARY KEY);
                CREATE TABLE RelationshipAttributes (Key TEXT PRIMARY KEY);
                CREATE TABLE RelationshipStatuses (Key TEXT PRIMARY KEY);
                CREATE TABLE Interactions (Key TEXT PRIMARY KEY);
                CREATE TABLE SpecialEncounters (EncounterKey TEXT PRIMARY KEY);
                INSERT INTO Characters VALUES ('LAW','Law'), ('YOURI','Youri');
                INSERT INTO MemoryTypes VALUES ('DEFEAT');
                INSERT INTO RelationshipAttributes VALUES ('TRUST');
                INSERT INTO RelationshipStatuses VALUES ('ALLY');
                INSERT INTO Interactions VALUES ('TEST');
                INSERT INTO SpecialEncounters VALUES ('DEFEATED');
            ");
        }
    }

    Test("missing save is not created", () =>
    {
        var missing = Path.Combine(root, "missing.db");
        var report = SaveGameIntegrityChecker.Inspect(missing, contentPath);
        if (!report.HasErrors || File.Exists(missing)) throw new Exception("Missing save must be reported without creating a file");
    });
    CreateFixtures();
    Test("valid save and global knowledge without knowledge rows", () =>
    {
        var report = SaveGameIntegrityChecker.Inspect(savePath, contentPath);
        if (report.Errors != 0 || report.Warnings != 0) throw new Exception(report.ToText());
    });
    Test("read-only check preserves save and content file bytes", () =>
    {
        var beforeSave = File.ReadAllBytes(savePath);
        var beforeContent = File.ReadAllBytes(contentPath);
        SaveGameIntegrityChecker.Inspect(savePath, contentPath);
        if (!beforeSave.SequenceEqual(File.ReadAllBytes(savePath)) ||
            !beforeContent.SequenceEqual(File.ReadAllBytes(contentPath)))
            throw new Exception("Diagnostic modified an existing database");
    });
    Test("missing authored character is a warning, not deletion", () =>
    {
        using (var insertConnection = Open(savePath)) Exec(insertConnection, "INSERT INTO SavedMemoryKnowledge VALUES (10, 'REMOVED_CHARACTER');");
        var report = SaveGameIntegrityChecker.Inspect(savePath, contentPath);
        if (report.Errors != 0 || report.Warnings == 0 || !report.ToText().Contains("REMOVED_CHARACTER"))
            throw new Exception(report.ToText());
        using var save = Open(savePath);
        using var count = save.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM SavedMemoryKnowledge";
        if (Convert.ToInt64(count.ExecuteScalar()) != 1) throw new Exception("Data was modified");
    });
    Test("orphan foreign key is detected", () =>
    {
        using (var save = Open(savePath)) Exec(save, "PRAGMA foreign_keys=OFF; INSERT INTO SavedMemoryKnowledge VALUES (999, 'LAW');");
        var report = SaveGameIntegrityChecker.Inspect(savePath, contentPath);
        if (!report.HasErrors || !report.ToText().Contains("Foreign keys")) throw new Exception(report.ToText());
    });
    Test("newer save version is rejected diagnostically without changing it", () =>
    {
        using (var versionConnection = Open(savePath)) Exec(versionConnection, "PRAGMA user_version=999;");
        var report = SaveGameIntegrityChecker.Inspect(savePath, contentPath);
        if (!report.HasErrors || report.SchemaVersion != 999) throw new Exception(report.ToText());
        using var save = Open(savePath);
        using var cmd = save.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 999) throw new Exception("The checker changed schema version");
    });
    Test("missing content file is only a warning and is not created", () =>
    {
        var missingContent = Path.Combine(root, "no-content.db");
        var report = SaveGameIntegrityChecker.Inspect(savePath, missingContent);
        if (report.Warnings == 0 || File.Exists(missingContent)) throw new Exception(report.ToText());
    });
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { /* temp data only */ }
}
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
