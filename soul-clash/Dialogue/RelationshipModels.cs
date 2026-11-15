using System;
using System.Collections.Generic;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Directional attributes: a character's feelings TOWARDS another character.
/// Love, Allyship, Respect, Trust: -100..100 (neutral = 0).
/// Fear, Romance: 0..100 (none = 0).
/// Romance is interest, not an established relationship/status.
/// </summary>
public enum RelationshipAttribute { Love, Fear, Allyship, Respect, Trust, Romance }

public sealed class RelationshipStats
{
	public int Love { get; set; }
	public int Fear { get; set; }
	public int Allyship { get; set; }
	public int Respect { get; set; }
	public int Trust { get; set; }
	public int Romance { get; set; }

	public RelationshipStats Clone() => new()
	{
		Love = Love, Fear = Fear, Allyship = Allyship,
		Respect = Respect, Trust = Trust, Romance = Romance
	};

	public int Get(RelationshipAttribute attribute) => attribute switch
	{
		RelationshipAttribute.Love => Love,
		RelationshipAttribute.Fear => Fear,
		RelationshipAttribute.Allyship => Allyship,
		RelationshipAttribute.Respect => Respect,
		RelationshipAttribute.Trust => Trust,
		RelationshipAttribute.Romance => Romance,
		_ => throw new ArgumentOutOfRangeException(nameof(attribute))
	};

	public void Set(RelationshipAttribute attribute, int value)
	{
		value = Math.Clamp(value, Minimum(attribute), 100);
		switch (attribute)
		{
			case RelationshipAttribute.Love: Love = value; break;
			case RelationshipAttribute.Fear: Fear = value; break;
			case RelationshipAttribute.Allyship: Allyship = value; break;
			case RelationshipAttribute.Respect: Respect = value; break;
			case RelationshipAttribute.Trust: Trust = value; break;
			case RelationshipAttribute.Romance: Romance = value; break;
			default: throw new ArgumentOutOfRangeException(nameof(attribute));
		}
	}

	public static int Minimum(RelationshipAttribute attribute) =>
		attribute is RelationshipAttribute.Fear or RelationshipAttribute.Romance ? 0 : -100;

	public static RelationshipAttribute ParseAttribute(string name) =>
		Enum.TryParse(name, true, out RelationshipAttribute attribute)
			&& Enum.IsDefined(typeof(RelationshipAttribute), attribute)
				? attribute : throw new InvalidOperationException($"Unknown relationship attribute '{name}'.");
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
	public string Attribute { get; set; } = nameof(RelationshipAttribute.Trust);
	public int? Minimum { get; set; }
	public int? Maximum { get; set; }
}

public sealed class RelationshipEffect
{
	public string Direction { get; set; } = RelationshipDirection.PartnerToActor;
	public string Attribute { get; set; } = nameof(RelationshipAttribute.Trust);
	public int Delta { get; set; }
}

public sealed class ChanceModifier
{
	public string Direction { get; set; } = RelationshipDirection.PartnerToActor;
	public string Attribute { get; set; } = nameof(RelationshipAttribute.Trust);
	public double Factor { get; set; }
}

public sealed record AppliedRelationshipChange(
	string FromId,
	string ToId,
	RelationshipAttribute Attribute,
	int Before,
	int After)
{
	public int Delta => After - Before;
}

public interface IRelationshipStore
{
	RelationshipStats Get(string fromCharacterId, string toCharacterId);
	IReadOnlyList<AppliedRelationshipChange> Apply(
		string actorId, string partnerId, IReadOnlyList<RelationshipEffect> effects);
}
