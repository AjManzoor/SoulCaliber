using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

/// <summary>One content-defined relationship dimension; Key is permanent across published versions.</summary>
public sealed record RelationshipAttributeDefinition(
	string Key, string Name, string Description, int Minimum, int Maximum, int Default,
	string IconPath, string Color, int SortOrder, bool IsActive);

/// <summary>Read-only catalogue from content.db. No relationship names or ranges are compiled into gameplay.</summary>
public sealed class RelationshipCatalogue
{
	private readonly Dictionary<string, RelationshipAttributeDefinition> _attributes;
	public IReadOnlyList<RelationshipAttributeDefinition> All { get; }
	public IReadOnlyList<RelationshipAttributeDefinition> Active { get; }

	private RelationshipCatalogue(List<RelationshipAttributeDefinition> attributes)
	{
		All = attributes.AsReadOnly();
		Active = attributes.Where(a => a.IsActive).ToList().AsReadOnly();
		_attributes = attributes.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
		if (Active.Count == 0) throw new InvalidOperationException("Content database has no active relationship attributes.");
	}
	public RelationshipAttributeDefinition Get(string key) =>
		_attributes.TryGetValue(key ?? "", out var item) ? item :
			throw new InvalidOperationException($"Unknown relationship attribute '{key}' in content database.");

	public static RelationshipCatalogue Load()
	{
		using var cn = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly, Pooling = false
		}.ToString());
		cn.Open();
		using var cmd = cn.CreateCommand();
		cmd.CommandText = @"SELECT Key,Name,Description,MinValue,MaxValue,DefaultValue,IconPath,Color,SortOrder,IsActive
			FROM RelationshipAttributes ORDER BY SortOrder,Name,Key";
		var items = new List<RelationshipAttributeDefinition>();
		using var r = cmd.ExecuteReader();
		while (r.Read())
			items.Add(new RelationshipAttributeDefinition(r.GetString(0), r.GetString(1), r.GetString(2),
				r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetString(6),r.GetString(7),
				r.GetInt32(8),r.GetInt32(9) == 1));
		return new RelationshipCatalogue(items);
	}
}

/// <summary>A directional pair's values. Missing rows use catalogue-defined defaults.</summary>
public sealed class RelationshipStats
{
    private readonly Dictionary<string,int> _values = new(StringComparer.OrdinalIgnoreCase);
    public RelationshipCatalogue Catalogue { get; }
    public RelationshipStats(RelationshipCatalogue catalogue) => Catalogue = catalogue;
    public int Get(string attribute) => _values.TryGetValue(Catalogue.Get(attribute).Key, out int value)
        ? value : Catalogue.Get(attribute).Default;
    public void Set(string attribute, int value)
    {
        var rule = Catalogue.Get(attribute);
        _values[rule.Key] = Math.Clamp(value,rule.Minimum,rule.Maximum);
    }
    public RelationshipStats Clone()
    {
        var copy = new RelationshipStats(Catalogue);
        foreach (var (key,value) in _values) copy._values[key] = value;
        return copy;
    }
    public IReadOnlyDictionary<string,int> StoredValues => _values;
}

public static class RelationshipDirection
{
    public const string ActorToPartner = "ACTOR_TO_PARTNER";
    public const string PartnerToActor = "PARTNER_TO_ACTOR";
    public static (string From, string To) Resolve(string direction, string actorId, string partnerId) =>
        direction.ToUpperInvariant() switch
        {
            ActorToPartner => (actorId, partnerId),
            PartnerToActor => (partnerId, actorId),
			_ => throw new InvalidOperationException($"Unknown relationship direction '{direction}'.")
        };
}

public sealed class RelationshipRequirement
{
    public string Direction { get; set; } = RelationshipDirection.PartnerToActor;
    public string Attribute { get; set; } = "TRUST";
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
}
public sealed class RelationshipEffect
{
    public string Direction { get; set; } = RelationshipDirection.PartnerToActor;
    public string Attribute { get; set; } = "TRUST";
    public int Delta { get; set; }
}
public sealed class ChanceModifier
{
    public string Direction { get; set; } = RelationshipDirection.PartnerToActor;
    public string Attribute { get; set; } = "TRUST";
    public double Factor { get; set; }
}
public sealed record AppliedRelationshipChange(string FromId, string ToId,
    string Attribute, int Before, int After)
{
    public int Delta => After - Before;
}
public interface IRelationshipStore
{
    RelationshipStats Get(string fromCharacterId, string toCharacterId);
    IReadOnlyList<AppliedRelationshipChange> Apply(string actorId,string partnerId,
        IReadOnlyList<RelationshipEffect> effects);
}
