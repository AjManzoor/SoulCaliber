using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Effective directional status = savegame override if valid, otherwise master-content default.
/// The master content database is opened read-only, never written here.
/// </summary>
public sealed class SqliteRelationshipStatusStore : IRelationshipStatusStore
{
    private readonly HashSet<string> _characterIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeStatusKeys = new(StringComparer.OrdinalIgnoreCase);
    // The authored names are presentation metadata, not runtime status values.
    private readonly Dictionary<string,string> _statusNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _statusIconPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _statusColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string From,string To),string> _defaults = new();

    public SqliteRelationshipStatusStore()
    {
        using (var content = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=DatabasePaths.Content,Mode=SqliteOpenMode.ReadOnly,ForeignKeys=true,Pooling=false
        }.ToString()))
        {
            content.Open();
            using var check = content.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('RelationshipStatuses','CharacterDefaultStatuses')";
            if (Convert.ToInt32((long)check.ExecuteScalar()!) != 2)
                throw new InvalidOperationException("Phase 12A/B relationship-status content is missing. Open the master database in updated Studio and republish.");
            using (var cmd=content.CreateCommand())
            {
                cmd.CommandText="SELECT Id FROM Characters";
                using var r=cmd.ExecuteReader();
                while(r.Read())_characterIds.Add(r.GetString(0));
            }
            // Older published content still works with built-in appearance defaults.
            bool hasAppearance;
            using (var checkAppearance=content.CreateCommand())
            {
                checkAppearance.CommandText="SELECT COUNT(*) FROM pragma_table_info('RelationshipStatuses') WHERE name IN ('IconPath','Color')";
                long columns=Convert.ToInt64(checkAppearance.ExecuteScalar());
                if (columns==1)
                    throw new InvalidOperationException("Published status appearance schema is incomplete. Republish with Studio.");
                hasAppearance=columns==2;
            }
            using (var cmd=content.CreateCommand())
            {
                cmd.CommandText=hasAppearance
                    ? "SELECT Key, Name, IconPath, Color FROM RelationshipStatuses WHERE IsActive=1"
                    : "SELECT Key, Name FROM RelationshipStatuses WHERE IsActive=1";
                using var r=cmd.ExecuteReader();
                while(r.Read())
                {
                    string key=Key(r.GetString(0));
                    _activeStatusKeys.Add(key);
                    _statusNames.Add(key,r.GetString(1));
                    _statusIconPaths.Add(key,hasAppearance ? r.GetString(2) : DefaultIcon(key));
                    string hex=hasAppearance ? r.GetString(3) : DefaultColour(key);
                    _statusColors.Add(key,Regex.IsMatch(hex,"^#[0-9a-fA-F]{6}$") ? hex : "#D4B8F5");
                }
            }
            if(!_activeStatusKeys.Contains("NEUTRAL"))
                throw new InvalidOperationException("Published status catalogue has no active NEUTRAL fallback.");
            using (var cmd=content.CreateCommand())
            {
                cmd.CommandText="SELECT SourceCharacterId,TargetCharacterId,StatusKey FROM CharacterDefaultStatuses";
                using var r=cmd.ExecuteReader();
                while(r.Read())
                {
                    string from=Key(r.GetString(0)),to=Key(r.GetString(1)),status=Key(r.GetString(2));
                    if (!_characterIds.Contains(from) || !_characterIds.Contains(to) ||
                        from==to || !_activeStatusKeys.Contains(status))
                        throw new InvalidOperationException($"Invalid starting relationship status {from} -> {to}: {status}.");
                    _defaults.Add((from,to),status);
                }
            }
        }
        using var db = new SaveDbContext();
        db.Database.EnsureCreated(); // Existing saves require the CREATE TABLE below.
        using var save=OpenSave();
        using var cmdCreate=save.CreateCommand();
        cmdCreate.CommandText="""
            CREATE TABLE IF NOT EXISTS SavedCharacterStatusOverrides (
                FromCharacterId TEXT NOT NULL COLLATE NOCASE,
                ToCharacterId TEXT NOT NULL COLLATE NOCASE,
                StatusKey TEXT NOT NULL COLLATE NOCASE,
                PRIMARY KEY(FromCharacterId,ToCharacterId),
                CHECK(FromCharacterId <> ToCharacterId COLLATE NOCASE)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS SavedInteractionStatusChanges (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SavedInteractionId INTEGER NOT NULL REFERENCES InteractionHistory(Id) ON DELETE CASCADE,
                FromCharacterId TEXT NOT NULL,
                ToCharacterId TEXT NOT NULL,
                Before TEXT NOT NULL,
                After TEXT NOT NULL
            );
            """;
        cmdCreate.ExecuteNonQuery();
    }

    private static string Key(string value) => value.Trim().ToUpperInvariant();
    private static SqliteConnection OpenSave()
    {
        var cn=new SqliteConnection(DatabasePaths.ConnectionString(DatabasePaths.Save));
        cn.Open();
        return cn;
    }

    private void CheckPair(string from,string to)
    {
        if (!_characterIds.Contains(from)||!_characterIds.Contains(to)||from==to)
            throw new InvalidOperationException($"Invalid status relationship: {from} -> {to}.");
    }
    private string Starting(string from,string to) =>
        _defaults.TryGetValue((from,to),out var status)?status:"NEUTRAL";

    private string GetOnConnection(SqliteConnection db,SqliteTransaction? tx,string from,string to)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT StatusKey FROM SavedCharacterStatusOverrides WHERE FromCharacterId=$from AND ToCharacterId=$to";
        cmd.Parameters.AddWithValue("$from",from);
        cmd.Parameters.AddWithValue("$to",to);
        var stored=cmd.ExecuteScalar() as string;
        // A save from older published content may reference an inactive/removed status.
        // Ignore the obsolete override rather than corrupting the authored master.
        return stored is not null && _activeStatusKeys.Contains(stored) ? Key(stored) : Starting(from,to);
    }
    /// <summary>Display the authored catalogue name without changing the stored status key.</summary>
    public string GetDisplayName(string statusKey)
    {
        string key=Key(statusKey);
        return _statusNames.TryGetValue(key,out string? name) ? name : key;
    }

    private static string DefaultIcon(string key) => key switch
    {
        "ALLY" => "res://Assets/StatusIcons/ally.svg",
        "NEUTRAL" => "res://Assets/StatusIcons/neutral.svg",
        "ENEMY" => "res://Assets/StatusIcons/enemy.svg",
        _ => ""
    };

    private static string DefaultColour(string key) => key switch
    {
        "ALLY" => "#8AE6A8",
        "NEUTRAL" => "#BDB8CC",
        "ENEMY" => "#FF9994",
        _ => "#D4B8F5"
    };

    /// <summary>Godot resource path authored in Studio; blank means use the coloured dot fallback.</summary>
    public string GetIconPath(string statusKey)
    {
        string key=Key(statusKey);
        return _statusIconPaths.TryGetValue(key, out string? path) ? path : "";
    }

    /// <summary>Authored colour with a safe purple fallback for old/unknown statuses.</summary>
    public string GetColorHex(string statusKey)
    {
        string key=Key(statusKey);
        return _statusColors.TryGetValue(key, out string? color) ? color : "#D4B8F5";
    }

    public string Get(string fromCharacterId,string toCharacterId)
    {
        string from=Key(fromCharacterId),to=Key(toCharacterId);
        CheckPair(from,to);
        using var db=OpenSave();
        return GetOnConnection(db,null,from,to);
    }

    /// <summary>Read pending ally/status changes inside a caller-owned unit of work.</summary>
    internal string GetInTransaction(SqliteConnection db, SqliteTransaction tx,
        string fromCharacterId, string toCharacterId)
    {
        string from=Key(fromCharacterId),to=Key(toCharacterId);
        CheckPair(from,to);
        return GetOnConnection(db,tx,from,to);
    }

    /// <summary>Use for scripted gameplay events until the event engine exists.</summary>
    public AppliedRelationshipStatusChange? SetStatus(string fromCharacterId,string toCharacterId,string statusKey)
    {
        string from=Key(fromCharacterId),to=Key(toCharacterId),status=Key(statusKey);
        CheckPair(from,to);
        if(!_activeStatusKeys.Contains(status))
            throw new InvalidOperationException($"Unknown or inactive relationship status: {status}.");
        using var db=OpenSave();
        using var tx=db.BeginTransaction();
        var change=SetOnConnection(db,tx,from,to,status);
        tx.Commit();
        return change;
    }

    public IReadOnlyList<AppliedRelationshipStatusChange> Apply(string actorId, string partnerId,
        IReadOnlyList<RelationshipStatusEffect> effects)
    {
        if (effects.Count == 0) return Array.Empty<AppliedRelationshipStatusChange>();
        using var db = OpenSave();
        using var tx = db.BeginTransaction();
        var changes = ApplyInTransaction(db, tx, actorId, partnerId, effects);
        tx.Commit();
        return changes;
    }

    /// <summary>
    /// Applies status overrides within a caller-owned SQLite transaction. Does not commit.
    /// Both effect families use this same connection and transaction in dialogue outcomes.
    /// </summary>
    internal IReadOnlyList<AppliedRelationshipStatusChange> ApplyInTransaction(
        SqliteConnection db, SqliteTransaction tx, string actorId, string partnerId,
        IReadOnlyList<RelationshipStatusEffect> effects)
    {
        string actor = Key(actorId), partner = Key(partnerId);
        CheckPair(actor, partner);
        var targets = new List<(string From, string To, string Status)>();
        foreach (var effect in effects)
        {
            string status = Key(effect.StatusKey);
            if (!_activeStatusKeys.Contains(status))
                throw new InvalidOperationException($"Unknown or inactive relationship status: {status}.");
            switch (effect.Direction.ToUpperInvariant())
            {
                case "ACTOR_TO_PARTNER": targets.Add((actor, partner, status)); break;
                case "PARTNER_TO_ACTOR": targets.Add((partner, actor, status)); break;
                case "BOTH":
                    targets.Add((actor, partner, status));
                    targets.Add((partner, actor, status));
                    break;
                default:
                    throw new InvalidOperationException($"Invalid status effect direction '{effect.Direction}'.");
            }
        }
        if (targets.Select(x => (x.From, x.To)).Distinct().Count() != targets.Count)
            throw new InvalidOperationException("Conflicting status effects for the same outcome and direction.");

        var changes = new List<AppliedRelationshipStatusChange>();
        foreach (var (from, to, status) in targets)
        {
            var change = SetOnConnection(db, tx, from, to, status);
            if (change is not null) changes.Add(change);
        }
        return changes;
    }

    private AppliedRelationshipStatusChange? SetOnConnection(SqliteConnection db,SqliteTransaction tx,
        string from,string to,string status)
    {
        string before=GetOnConnection(db,tx,from,to);
        // Even if the effective value already equals the requested default,
        // clear any redundant/stale override so future content updates behave cleanly.
        if(before==status && status!=Starting(from,to))return null;
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        if(status==Starting(from,to))
        {
            cmd.CommandText="DELETE FROM SavedCharacterStatusOverrides WHERE FromCharacterId=$from AND ToCharacterId=$to";
        }
        else
        {
            cmd.CommandText="""
                INSERT INTO SavedCharacterStatusOverrides(FromCharacterId,ToCharacterId,StatusKey)
                VALUES($from,$to,$status)
                ON CONFLICT(FromCharacterId,ToCharacterId) DO UPDATE SET StatusKey=excluded.StatusKey
                """;
            cmd.Parameters.AddWithValue("$status",status);
        }
        cmd.Parameters.AddWithValue("$from",from);
        cmd.Parameters.AddWithValue("$to",to);
        cmd.ExecuteNonQuery();
        return before==status ? null : new AppliedRelationshipStatusChange(from,to,before,status);
    }

    /// <summary>Start NEW GAME: remove all runtime status overrides. Existing saves only change if called.</summary>
    public void ResetToDefaults()
    {
        using var db=OpenSave();
        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="DELETE FROM SavedCharacterStatusOverrides";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
}
