using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SoulCaliber.Dialogue;

// Tests the real production startup and schema-manager code with explicitly
// named temporary database paths. Never touches Godot's user:// save.
int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}

string root = Path.Combine(Path.GetTempPath(), "soulcaliber-startup-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string PathFor(string name) => Path.Combine(root, name);
    void SetupDb(string path, string sql)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    void AssertUnchanged(string path, byte[] original)
    {
        if (!original.SequenceEqual(File.ReadAllBytes(path)))
            throw new Exception("The existing savegame bytes changed on a failed startup.");
    }

    string baselineSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "baseline.sql"));

    Test("current version-1 save loads without resetting progress", () =>
    {
        string path = PathFor("current.db");
        SetupDb(path, baselineSql + "PRAGMA user_version=1; " +
            "INSERT INTO SavedRelationshipValues VALUES ('LAW','YOURI','TRUST',77);");
        var result = SaveGameStartup.TryInitialize(path);
        if (!result.Success || result.CreatedNewSave)
            throw new Exception("Expected existing save to load: " + result);
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM SavedRelationshipValues WHERE FromCharacterId='LAW' AND ToCharacterId='YOURI'";
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 77)
            throw new Exception("Existing relationship progress was lost.");
    });

    Test("complete unversioned legacy save is adopted with rows preserved", () =>
    {
        string path = PathFor("legacy.db");
        SetupDb(path, baselineSql +
            "INSERT INTO SavedRelationshipValues VALUES ('LAW','YOURI','TRUST',61);" +
            "INSERT INTO SavedMemoryOccurrences (MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc) " +
            "VALUES ('DEFEAT','LAW','LAW','MAJOR','QUIET','2026-11-17');");
        var result = SaveGameStartup.TryInitialize(path);
        if (!result.Success || result.CreatedNewSave)
            throw new Exception("Expected legacy save adoption: " + result);
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        using var version = conn.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(version.ExecuteScalar()) != 1)
            throw new Exception("Legacy schema not adopted to version 1.");
        using var count = conn.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM SavedMemoryOccurrences";
        if (Convert.ToInt64(count.ExecuteScalar()) != 1)
            throw new Exception("Legacy memory was deleted.");
    });

    Test("newer schema is blocked without overwriting save", () =>
    {
        string path = PathFor("newer.db");
        SetupDb(path, "CREATE TABLE Preserved (Id INTEGER PRIMARY KEY); INSERT INTO Preserved VALUES (5); PRAGMA user_version=999;");
        byte[] before = File.ReadAllBytes(path);
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || result.Problem != SaveGameStartupProblem.NewerVersion)
            throw new Exception("Expected newer-version failure, got " + result);
        AssertUnchanged(path, before);
    });

    Test("corrupt non-SQLite save is rejected unchanged", () =>
    {
        string path = PathFor("corrupt.db");
        byte[] before = System.Text.Encoding.UTF8.GetBytes("this is not a sqlite database");
        File.WriteAllBytes(path, before);
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || result.Problem != SaveGameStartupProblem.CorruptDatabase)
            throw new Exception("Expected corrupt-database failure, got " + result);
        AssertUnchanged(path, before);
    });

    Test("pre-existing empty file cannot become new game", () =>
    {
        string path = PathFor("empty.db");
        File.WriteAllBytes(path, Array.Empty<byte>());
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || result.Problem != SaveGameStartupProblem.IncompatibleSchema ||
            result.CreatedNewSave || new FileInfo(path).Length != 0)
            throw new Exception("Existing blank file must be preserved and rejected: " + result);
    });

    Test("incomplete legacy save is not adopted or reset", () =>
    {
        string path = PathFor("legacy-incomplete.db");
        SetupDb(path, "CREATE TABLE OldSaveData (Value TEXT); INSERT INTO OldSaveData VALUES ('preserve me');");
        byte[] before = File.ReadAllBytes(path);
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || result.Problem != SaveGameStartupProblem.IncompatibleSchema)
            throw new Exception("Expected incompatible-schema failure, got " + result);
        AssertUnchanged(path, before);
    });

    Test("unwritable/missing parent directory does not create save", () =>
    {
        string path = Path.Combine(root, "folder-does-not-exist", "save.db");
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || result.Problem != SaveGameStartupProblem.StorageUnavailable || File.Exists(path))
            throw new Exception("Missing parent directory must cause a safe access failure: " + result);
    });

    Test("passing a directory as a save path does not replace it", () =>
    {
        string path = PathFor("save-is-directory.db");
        Directory.CreateDirectory(path);
        var result = SaveGameStartup.TryInitialize(path);
        if (result.Success || !Directory.Exists(path))
            throw new Exception("Directory cannot be used as a save file: " + result);
    });
}
finally
{
    try { Directory.Delete(root, true); }
    catch { /* Test fixtures only; never operate on the real save. */ }
}
Console.WriteLine($"{passed} passed; {failed} failed");
return failed == 0 ? 0 : 1;
