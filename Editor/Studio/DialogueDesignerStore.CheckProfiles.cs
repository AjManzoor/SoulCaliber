using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;
using System.IO;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    public sealed record CheckProfileRow(string Key, string Name, string Description)
    {
        public override string ToString() => $"{Name} [{Key}]";
    }
    public sealed record CheckProfileTermRow(string ProfileKey, string Direction, string Attribute, double Factor)
    {
        public override string ToString() => $"{DirectionLabel(Direction)}: {Attribute} × {Factor:+0.###;-0.###;0}";
    }

    private void EnsureCheckProfilesSchema()
    {
        using var check = Command("SELECT 1 FROM sqlite_master WHERE type='table' AND name='CheckProfiles'");
        if (check.ExecuteScalar() is not null) return;
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.005_CheckProfiles.sql")
            ?? throw new InvalidOperationException("Embedded 005_CheckProfiles.sql is missing.");
        using var text = new StreamReader(stream);
        using var transaction = _db.BeginTransaction();
        try
        {
            using var create = Command(text.ReadToEnd(), transaction);
            create.ExecuteNonQuery();
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public string GetOptionProfile(int optionId)
    {
        using var cmd = Command("SELECT ProfileKey FROM OptionCheckProfiles WHERE OptionId=$option");
        Add(cmd,"$option",optionId);
        return cmd.ExecuteScalar() as string ?? "STANDARD";
    }

    public void SaveOptionProfile(int optionId, string profileKey)
    {
        CheckOptionId(optionId);
        if (!CheckProfileKeys().Contains(profileKey, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a valid check profile.");
        Write(tx =>
        {
            using var cmd = Command(@"INSERT INTO OptionCheckProfiles(OptionId,ProfileKey)
                VALUES($option,$profile)
                ON CONFLICT(OptionId) DO UPDATE SET ProfileKey=excluded.ProfileKey",tx);
            Add(cmd,"$option",optionId); Add(cmd,"$profile",profileKey); cmd.ExecuteNonQuery();
            // Keep the legacy CheckMode in sync for old tools/content diagnostics.
            using var legacy = Command("UPDATE SkillChecks SET CheckMode=$profile WHERE OptionId=$option",tx);
            Add(legacy,"$option",optionId); Add(legacy,"$profile",profileKey); legacy.ExecuteNonQuery();
        });
    }

    public List<CheckProfileRow> CheckProfiles()
    {
        using var cmd = Command("SELECT Key, Name, Description FROM CheckProfiles ORDER BY Key");
        using var r = cmd.ExecuteReader();
        var rows = new List<CheckProfileRow>();
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2)));
        return rows;
    }
    public List<string> CheckProfileKeys() => CheckProfiles().Select(p => p.Key).ToList();
    public List<CheckProfileTermRow> CheckProfileTerms(string profileKey)
    {
        using var cmd = Command("SELECT ProfileKey,Direction,Attribute,Factor FROM CheckProfileModifiers WHERE ProfileKey=$key ORDER BY Direction,Attribute");
        Add(cmd,"$key",profileKey);
        using var r = cmd.ExecuteReader();
        var rows = new List<CheckProfileTermRow>();
        while(r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetDouble(3)));
        return rows;
    }
    public string CheckProfileFormula(string profileKey) => ChanceFormula.Describe(CheckProfileTerms(profileKey)
        .Select(t=>new ChanceFactor(t.Direction,t.Attribute,t.Factor)));

    public void SaveCheckProfile(CheckProfileRow item)
    {
        string key = Key(item.Key,"Profile key").ToUpperInvariant();
        string name = Required(item.Name,"Profile name");
        if (key.Length > 48) throw new InvalidOperationException("Profile key is too long (maximum 48 characters).");
        Write(tx =>
        {
            using var cmd = Command(@"INSERT INTO CheckProfiles(Key,Name,Description) VALUES($key,$name,$description)
                ON CONFLICT(Key) DO UPDATE SET Name=excluded.Name, Description=excluded.Description",tx);
            Add(cmd,"$key",key); Add(cmd,"$name",name); Add(cmd,"$description",item.Description ?? "");
            cmd.ExecuteNonQuery();
        });
    }
    public void DeleteCheckProfile(string profileKey)
    {
        if (profileKey.Equals("STANDARD",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("STANDARD cannot be deleted: choices without a skill check use it.");
        if (Exists(_db,"SELECT 1 FROM OptionCheckProfiles WHERE ProfileKey=$key",("$key",profileKey)) ||
            Exists(_db,"SELECT 1 FROM SkillChecks WHERE CheckMode=$key",("$key",profileKey)))
            throw new InvalidOperationException("This profile is used by a choice. Assign a different profile before deleting it.");
        Write(tx =>
        {
            using var cmd=Command("DELETE FROM CheckProfiles WHERE Key=$key",tx);
            Add(cmd,"$key",profileKey); Changed(cmd,true);
        });
    }
    public void SaveCheckProfileTerm(CheckProfileTermRow item, CheckProfileTermRow? previous = null)
    {
        string direction=CheckDirection(item.Direction);
        string attribute=CheckAttribute(item.Attribute);
        CheckNumber(item.Factor,"Formula factor");
        if (item.Factor < -10 || item.Factor > 10)
            throw new InvalidOperationException("Formula factors must be between -10 and 10.");
        if (!CheckProfileKeys().Contains(item.ProfileKey,StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save the profile first.");
        Write(tx =>
        {
            // Moving a selected term to a different attribute/direction is atomic:
            // never delete the old coefficient unless the replacement succeeds.
            if (previous is not null &&
                (!previous.Direction.Equals(direction, StringComparison.Ordinal) ||
                 !previous.Attribute.Equals(attribute, StringComparison.Ordinal)))
            {
                using var remove = Command(@"DELETE FROM CheckProfileModifiers WHERE ProfileKey=$profile
                    AND Direction=$direction AND Attribute=$attribute", tx);
                Add(remove, "$profile", previous.ProfileKey); Add(remove, "$direction", previous.Direction);
                Add(remove, "$attribute", previous.Attribute); Changed(remove, true);
            }
            using var cmd=Command(@"INSERT INTO CheckProfileModifiers(ProfileKey,Direction,Attribute,Factor)
                VALUES($profile,$direction,$attribute,$factor)
                ON CONFLICT(ProfileKey,Direction,Attribute) DO UPDATE SET Factor=excluded.Factor",tx);
            Add(cmd,"$profile",item.ProfileKey);Add(cmd,"$direction",direction);
            Add(cmd,"$attribute",attribute);Add(cmd,"$factor",item.Factor);
            cmd.ExecuteNonQuery();
        });
    }
    public void DeleteCheckProfileTerm(CheckProfileTermRow item)
    {
        Write(tx =>
        {
            using var cmd=Command(@"DELETE FROM CheckProfileModifiers WHERE ProfileKey=$profile
                AND Direction=$direction AND Attribute=$attribute",tx);
            Add(cmd,"$profile",item.ProfileKey); Add(cmd,"$direction",item.Direction);Add(cmd,"$attribute",item.Attribute);
            Changed(cmd,true);
        });
    }
}
