using System;
using System.Collections.Generic;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Social abilities are intrinsic to a character (0..100, 50 = average).
/// They are NOT the six directional relationship values.
/// Missing characters use 50 in each ability so older saves continue to work.
/// </summary>
public sealed class PersonalityStats
{
    public int Persuasion { get; set; } = 50;
    public int Intimidation { get; set; } = 50;
    public int Deception { get; set; } = 50;
    public int Charm { get; set; } = 50;
    public int Insight { get; set; } = 50;
    public int Resolve { get; set; } = 50;

    public int Get(string ability) => ability.ToUpperInvariant() switch
    {
        "PERSUASION" => Persuasion,
        "INTIMIDATION" => Intimidation,
        "DECEPTION" => Deception,
        "CHARM" => Charm,
        "INSIGHT" => Insight,
        "RESOLVE" => Resolve,
        _ => throw new InvalidOperationException($"Unknown personality ability '{ability}'.")
    };

    public static void ValidateAbility(string ability)
    {
        _ = new PersonalityStats().Get(ability);
    }

    public void Validate(string characterId)
    {
        foreach (var ability in new[] { nameof(Persuasion), nameof(Intimidation),
            nameof(Deception), nameof(Charm), nameof(Insight), nameof(Resolve) })
        {
            int value = Get(ability);
            if (value < 0 || value > 100)
                throw new InvalidOperationException(
                    $"Character '{characterId}' has {ability}={value}; abilities must be 0..100.");
        }
    }
}

public sealed class CharacterPersonality
{
    public string CharacterId { get; set; } = "";
    public PersonalityStats Stats { get; set; } = new();
}

public sealed class PersonalityFile
{
    public List<CharacterPersonality> Characters { get; set; } = new();
}

/// <summary>
/// Data-defined check for one option: actor's ability versus partner's resistance.
/// 50 is neutral. The engine scales deviations from 50 by the two factors.
/// This is opt-in: options without a check keep the original relationship-only odds.
/// </summary>
public sealed class OptionSkillCheck
{
    public string InteractionKey { get; set; } = "";
    public string NodeKey { get; set; } = "";
    public string OptionKey { get; set; } = "";
    public string ActorSkill { get; set; } = "";
    public string DefenderSkill { get; set; } = "";
    // STANDARD: use the option's normal base chance and ordinary relationship bonuses.
    // INTIMIDATION: defender Resolve is equally weighted with actor Intimidation;
    // the target's Fear helps while their Trust reduces susceptibility.
    public string CheckMode { get; set; } = "STANDARD";
    public double ActorFactor { get; set; } = 0.50;
    public double DefenderFactor { get; set; } = 0.50;
}

public sealed class OptionSkillsFile
{
    public List<OptionSkillCheck> Checks { get; set; } = new();
}
