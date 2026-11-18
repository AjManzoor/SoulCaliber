using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

/// <summary>Active authored traits from published content, used for dialogue availability.</summary>
internal sealed class PublishedTraitLookup
{
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _assignments = new(StringComparer.OrdinalIgnoreCase);

    private static string Pair(string character, string trait) => character + "\u001F" + trait;

    public static PublishedTraitLookup Load(SqliteConnection content)
    {
        var lookup = new PublishedTraitLookup();
        using (var cmd = content.CreateCommand())
        {
            cmd.CommandText = "SELECT Key FROM PersonalityTraits WHERE IsActive=1";
            using var rows = cmd.ExecuteReader();
            while (rows.Read()) lookup._active.Add(rows.GetString(0));
        }
        using (var cmd = content.CreateCommand())
        {
            cmd.CommandText = @"SELECT ct.CharacterId,ct.TraitKey FROM CharacterTraits ct
                JOIN PersonalityTraits t ON t.Key=ct.TraitKey WHERE t.IsActive=1";
            using var rows = cmd.ExecuteReader();
            while (rows.Read()) lookup._assignments.Add(Pair(rows.GetString(0), rows.GetString(1)));
        }
        return lookup;
    }

    public bool Active(string key) => _active.Contains(key);
    public bool Has(string character, string trait) => _assignments.Contains(Pair(character, trait));
}
