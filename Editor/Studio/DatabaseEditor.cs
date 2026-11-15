using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;
using System.Text;

namespace SoulCaliberStudio;

// Uses the shipped SoulCaliber schema verbatim: no EnsureCreated(), migrations, or importer.
public sealed class DatabaseEditor(string databasePath) : IDisposable
{
    private readonly string _path = Path.GetFullPath(databasePath);
    private SqliteConnection? _connection;

    public static readonly string[] Tables =
    [
        "Interactions", "Nodes", "NodeIntroductions", "Options", "Lines", "Characters", "ReactionImages",
        "Personalities", "Requirements", "ChanceModifiers", "Effects", "SkillChecks"
    ];

    public void Open()
    {
        if (!File.Exists(_path)) throw new FileNotFoundException("Master content database does not exist.", _path);
        var builder = new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        using var cmd = CreateCommand("PRAGMA busy_timeout = 5000;");
        cmd.ExecuteNonQuery();
        foreach (var table in new[] { "Characters", "Interactions", "Nodes", "Options", "Lines" })
        {
            if (!TableExists(table)) throw new InvalidDataException($"Expected table '{table}' was not found. Wrong or incompatible database?");
        }
        using var check = CreateCommand("PRAGMA integrity_check;");
        if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("SQLite integrity check failed.");
    }

    private SqliteConnection Connection => _connection ?? throw new InvalidOperationException("Open the database first.");
    private SqliteCommand CreateCommand(string sql) => new(sql, Connection);
    private static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
    private static void CheckTable(string table)
    {
        if (!Tables.Contains(table, StringComparer.Ordinal)) throw new ArgumentException("Unrecognised editor table.", nameof(table));
    }
    private bool TableExists(string table)
    {
        using var cmd = CreateCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;");
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() != null;
    }

    public DataTable Load(string table)
    {
        CheckTable(table);
        using var cmd = CreateCommand($"SELECT * FROM {Q(table)};");
        using var reader = cmd.ExecuteReader();
        var result = new DataTable(table) { Locale = System.Globalization.CultureInfo.InvariantCulture };
        result.Load(reader);
        // DataTable.Load marks rows unchanged; the UI will use DataRow versions for UPDATE/DELETE.
        return result;
    }

    public List<ColumnInfo> Columns(string table)
    {
        CheckTable(table);
        using var cmd = CreateCommand($"PRAGMA table_info({Q(table)});");
        using var reader = cmd.ExecuteReader();
        var result = new List<ColumnInfo>();
        while (reader.Read())
        {
            result.Add(new ColumnInfo(
                reader.GetString(1), reader.GetString(2), reader.GetInt64(5) > 0,
                reader.GetInt64(3) > 0, reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return result;
    }

    public string Backup()
    {
        string folder = Path.Combine(Path.GetDirectoryName(_path)!, "StudioBackups");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"soulcaliber_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.db");
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        target.Open();
        Connection.BackupDatabase(target);
        return file;
    }

    public void SaveChanges(DataTable data)
    {
        string table = data.TableName;
        CheckTable(table);
        List<ColumnInfo> cols = Columns(table);
        var primaryKeys = cols.Where(c => c.IsPrimaryKey).ToList();
        if (primaryKeys.Count == 0) throw new InvalidOperationException($"{table} has no primary key; editing is not supported.");
        var dirty = data.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Unchanged && r.RowState != DataRowState.Detached).ToList();
        if (dirty.Count == 0) return;

        // Make a consistent, recoverable snapshot BEFORE changing anything.
        Backup();
        using var transaction = Connection.BeginTransaction();
        try
        {
            foreach (DataRow row in dirty)
            {
                if (row.RowState == DataRowState.Deleted)
                {
                    string where = string.Join(" AND ", primaryKeys.Select((c,i) => $"{Q(c.Name)}=$pk{i}"));
                    using var cmd = new SqliteCommand($"DELETE FROM {Q(table)} WHERE {where};", Connection, transaction);
                    for (int i = 0; i < primaryKeys.Count; i++)
                        cmd.Parameters.AddWithValue($"$pk{i}", row[primaryKeys[i].Name, DataRowVersion.Original]);
                    if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException($"{table}: record was changed or removed by another process.");
                }
                else if (row.RowState == DataRowState.Modified)
                {
                    // PK values are deliberately immutable in the editor.
                    foreach (var key in primaryKeys)
                        if (!Equals(row[key.Name], row[key.Name, DataRowVersion.Original]))
                            throw new InvalidOperationException("Primary keys cannot be modified. Create a new record instead.");
                    var editable = cols.Where(c => !c.IsPrimaryKey && !Equals(row[c.Name], row[c.Name, DataRowVersion.Original])).ToList();
                    if (editable.Count == 0) continue;
                    string assignments = string.Join(", ", editable.Select((c,i) => $"{Q(c.Name)}=$v{i}"));
                    string where = string.Join(" AND ", primaryKeys.Select((c,i) => $"{Q(c.Name)}=$pk{i}"));
                    using var cmd = new SqliteCommand($"UPDATE {Q(table)} SET {assignments} WHERE {where};", Connection, transaction);
                    for (int i = 0; i < editable.Count; i++) cmd.Parameters.AddWithValue($"$v{i}", Value(row[editable[i].Name]));
                    for (int i = 0; i < primaryKeys.Count; i++) cmd.Parameters.AddWithValue($"$pk{i}", row[primaryKeys[i].Name, DataRowVersion.Original]);
                    if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException($"{table}: record was changed or removed by another process.");
                }
                else if (row.RowState == DataRowState.Added)
                {
                    // Autogenerated integer PKs are omitted; manually populated string/composite PKs are included.
                    bool AutoIntegerPk(ColumnInfo c) => c.IsPrimaryKey && primaryKeys.Count == 1 && c.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
                    var insert = cols.Where(c => !(AutoIntegerPk(c) && row.IsNull(c.Name)) && !row.IsNull(c.Name)).ToList();
                    string sql = insert.Count == 0 ? $"INSERT INTO {Q(table)} DEFAULT VALUES;" :
                        $"INSERT INTO {Q(table)} ({string.Join(",", insert.Select(c => Q(c.Name)))}) VALUES ({string.Join(",", insert.Select((_,i) => "$v" + i))});";
                    using var cmd = new SqliteCommand(sql, Connection, transaction);
                    for (int i = 0; i < insert.Count; i++) cmd.Parameters.AddWithValue($"$v{i}", Value(row[insert[i].Name]));
                    if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Could not insert into {table}.");
                }
            }
            transaction.Commit();
            // Reload to receive newly generated PK values and DB defaults.
        }
        catch { transaction.Rollback(); throw; }
    }

    public string VerifyForeignKeys()
    {
        using var cmd = CreateCommand("PRAGMA foreign_key_check;");
        using var reader = cmd.ExecuteReader();
        var problems = new List<string>();
        while (reader.Read()) problems.Add($"{reader.GetString(0)} row {reader.GetInt64(1)} references {reader.GetString(2)}");
        return problems.Count == 0 ? "All foreign keys valid." : string.Join(Environment.NewLine, problems);
    }

    public bool HasDependents(string table, DataRow row)
    {
        // Deletion safeguards beyond FK rules, even for cascading relationships.
        if (table == "Interactions") return Exists("SELECT 1 FROM Nodes WHERE InteractionId=$id LIMIT 1", row["Id"]);
        if (table == "Nodes") return Exists("SELECT 1 FROM Options WHERE NodeId=$id LIMIT 1", row["Id"]) ||
            Exists("SELECT 1 FROM NodeIntroductions WHERE NodeId=$id LIMIT 1", row["Id"]);
        if (table == "Options")
            return new[] { "Lines", "Requirements", "ChanceModifiers", "Effects", "SkillChecks" }
                .Any(t => Exists($"SELECT 1 FROM {Q(t)} WHERE OptionId=$id LIMIT 1", row["Id"]));
        if (table == "Characters")
            return Exists("SELECT 1 FROM Lines WHERE CharacterId=$id OR VisitorId=$id LIMIT 1", row["Id"]) ||
                   Exists("SELECT 1 FROM NodeIntroductions WHERE CharacterId=$id OR VisitorId=$id LIMIT 1", row["Id"]) ||
                   Exists("SELECT 1 FROM Interactions WHERE HostCharacterId=$id LIMIT 1", row["Id"]) ||
                   Exists("SELECT 1 FROM ReactionImages WHERE CharacterId=$id LIMIT 1", row["Id"]) ||
                   Exists("SELECT 1 FROM Personalities WHERE CharacterId=$id LIMIT 1", row["Id"]);
        return false;
    }
    private bool Exists(string sql, object value)
    {
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("$id", value);
        return cmd.ExecuteScalar() != null;
    }
    private static object Value(object value) => value == DBNull.Value ? DBNull.Value : value;
    public void Dispose() => _connection?.Dispose();
}

public sealed record ColumnInfo(string Name, string Type, bool IsPrimaryKey, bool IsRequired, string? DefaultValue);
