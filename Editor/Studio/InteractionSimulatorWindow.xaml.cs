using SoulCaliber.Shared;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class InteractionSimulatorWindow : Window
{
    private readonly SimulationData _data;
    private readonly List<SimulationStatRow> _stats =
    [
        new("Love"), new("Fear"), new("Allyship"), new("Respect"), new("Trust"), new("Romance")
    ];
    private readonly Random _random = new();
    private SimInteraction? _interaction;
    private SimNode? _node;
    private string? _nextNode;
    private string _actor = "";
    private string _partner = "";
    private bool _awaitingContinue;
    private bool _finished;

    public InteractionSimulatorWindow(string databasePath)
    {
        InitializeComponent();
        _data = new SimulationData(databasePath);
        Closed += (_, _) => _data.Dispose();
        ActorBox.ItemsSource = _data.Characters;
        PartnerBox.ItemsSource = _data.Characters;
        InteractionBox.ItemsSource = _data.Interactions;
        ActorBox.SelectedIndex = 0;
        PartnerBox.SelectedIndex = _data.Characters.Count > 1 ? 1 : 0;
        InteractionBox.SelectedIndex = 0;
        StatsGrid.ItemsSource = _stats;
        OutcomeText.Text = "Choose two characters and an interaction, then click Start / restart.\n" +
            "You can edit relationship values directly in the left-hand table.";
        InspectorText.Text = "The inspector will show matched and rejected conditional dialogue candidates.";
        UpdateButtons();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        CommitStats();
        ValidateStats();
        if (ActorBox.SelectedItem is not SimCharacter actor ||
            PartnerBox.SelectedItem is not SimCharacter partner ||
            InteractionBox.SelectedItem is not SimInteraction interaction)
            throw new InvalidOperationException("Select an initiator, a recipient, and an interaction.");
        if (actor.Id.Equals(partner.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose two different characters.");
        _actor = actor.Id;
        _partner = partner.Id;
        _interaction = interaction;
        _node = interaction.Nodes.FirstOrDefault(n => n.Key.Equals(interaction.StartNodeKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Starting node '{interaction.StartNodeKey}' does not exist.");
        _awaitingContinue = false;
        _finished = false;
        _nextNode = null;
        OutcomeText.Text = "New test started. No real savegame was read or changed.";
        RenderNode();
    });

    private void ResetValues_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        CommitStats();
        foreach (var stat in _stats) { stat.ActorToPartner = 0; stat.PartnerToActor = 0; }
        StatsGrid.Items.Refresh();
        if (_node is not null && !_awaitingContinue) RenderNode();
    });

    private void RefreshRules_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        CommitStats();
        ValidateStats();
        if (_awaitingContinue)
            throw new InvalidOperationException("Continue to the next node before re-evaluating the dialogue. The current outcome has already been applied.");
        if (_node is not null) RenderNode();
    });

    private void ChoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void ForceSuccess_Click(object sender, RoutedEventArgs e) => Safely(() => ExecuteChoice(true, 1, "FORCED SUCCESS"));
    private void ForceFailure_Click(object sender, RoutedEventArgs e) => Safely(() => ExecuteChoice(false, 100, "FORCED FAILURE"));
    private void RandomRoll_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        CommitStats();
        ValidateStats();
        var chosen = SelectedChoice();
        int roll = _random.Next(1, 101);
        ExecuteChoice(roll <= chosen.Chance, roll, "RANDOM ROLL");
    });

    private void ExecuteChoice(bool succeeded, int roll, string testMode)
    {
        CommitStats();
        ValidateStats();
        var selected = SelectedChoice();
        if (_node is null) throw new InvalidOperationException("Start an interaction first.");
        var choice = selected.Choice;
        var before = Values();
        // Both dialogue lines are resolved against PRE-EFFECT values, like Godot's DialogueEngine.Choose().
        var actorLine = _data.Line(choice, _actor, _partner, succeeded, before, choice.GenericActorLine);
        var partnerLine = _data.Line(choice, _partner, _actor, succeeded, before,
            succeeded ? choice.GenericHostSuccess : choice.GenericHostFailure);
        var changes = new List<string>();
        foreach (var effect in _data.Effects(choice.Id, succeeded))
        {
            var row = _stats.FirstOrDefault(s => s.Attribute.Equals(effect.Attribute, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Unknown relationship attribute: {effect.Attribute}");
            bool actorToPartner = effect.Direction.Equals("ACTOR_TO_PARTNER", StringComparison.OrdinalIgnoreCase);
            if (!actorToPartner && !effect.Direction.Equals("PARTNER_TO_ACTOR", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unknown effect direction: {effect.Direction}");
            int original = actorToPartner ? row.ActorToPartner : row.PartnerToActor;
            int lower = row.Attribute is "Fear" or "Romance" ? 0 : -100;
            int updated = Math.Clamp(original + effect.Delta, lower, 100);
            if (actorToPartner) row.ActorToPartner = updated;
            else row.PartnerToActor = updated;
            changes.Add($"{(actorToPartner ? _data.Name(_actor) + " → " + _data.Name(_partner) : _data.Name(_partner) + " → " + _data.Name(_actor))} " +
                $"{row.Attribute}: {original} → {updated} ({updated - original:+#;-#;0})");
        }
        StatsGrid.Items.Refresh();
        _nextNode = succeeded ? choice.SuccessNext : choice.FailureNext;
        _awaitingContinue = true;
        _finished = false;
        var reaction = succeeded ? choice.SuccessReaction : choice.FailureReaction;
        OutcomeText.Text = $"{testMode} | {choice.Label}\n" +
            $"Chance: {selected.Chance}% | Test roll: {roll}/100 | {(succeeded ? "SUCCESS" : "FAILURE")}\n" +
            $"Target reaction: {reaction}\n\n" +
            $"{_data.Name(_actor)}: {actorLine.Text}\nSource: {actorLine.Source}\n\n" +
            $"{_data.Name(_partner)}: {partnerLine.Text}\nSource: {partnerLine.Source}\n\n" +
            $"RELATIONSHIP EFFECTS\n{(changes.Count == 0 ? "No effects configured." : string.Join("\n", changes))}\n\n" +
            $"Next node: {(string.IsNullOrWhiteSpace(_nextNode) ? "END" : _nextNode)}";
        InspectorText.Text = $"ACTOR PHRASE\n{actorLine.Inspector}\n\nRECIPIENT PHRASE\n{partnerLine.Inspector}\n\n" +
                             "Skill/chance calculation:\n" + selected.Breakdown;
        UpdateButtons();
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (!_awaitingContinue || _interaction is null) return;
        _awaitingContinue = false;
        if (string.IsNullOrWhiteSpace(_nextNode))
        {
            _finished = true;
            _node = null;
            ChoiceList.ItemsSource = null;
            NodeLabel.Text = "Conversation complete";
            IntroductionText.Text = "Start another test to replay the interaction with the current simulated relationship values.";
            IntroductionSource.Text = "The previous outcome is shown on the right.";
            UpdateButtons();
            return;
        }
        _node = _interaction.Nodes.FirstOrDefault(n => n.Key.Equals(_nextNode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Next node '{_nextNode}' is not present in this interaction.");
        _nextNode = null;
        RenderNode();
    });

    private void RenderNode()
    {
        if (_node is null) return;
        var intro = _data.Intro(_node, _partner, _actor, Values());
        NodeLabel.Text = $"{_interaction?.Title} · Round {_node.Round} · {_node.Key}";
        IntroductionSource.Text = $"{_data.Name(_partner)} · {intro.Source}";
        IntroductionText.Text = Expand(intro.Text);
        InspectorText.Text = $"INTRODUCTION\n{intro.Inspector}\n\n" +
                             "Conditional lines are resolved using CURRENT in-memory stats.";
        ChoiceList.ItemsSource = _node.Options.Select(ChoiceStatus).ToList();
        ChoiceList.SelectedIndex = _node.Options.Count > 0 ? 0 : -1;
        UpdateButtons();
    }

    private SimulationChoiceDisplay ChoiceStatus(SimChoice choice)
    {
        string? lockReason = null;
        foreach (var requirement in _data.Requirements(choice.Id))
        {
            int current = GetValue(requirement.Direction, requirement.Attribute);
            if (requirement.Minimum is int min && current < min)
            { lockReason = $"{requirement.Direction} {requirement.Attribute} ≥ {min}"; break; }
            if (requirement.Maximum is int max && current > max)
            { lockReason = $"{requirement.Direction} {requirement.Attribute} ≤ {max}"; break; }
        }
        var (chance, breakdown) = CalculateChance(choice);
        return new SimulationChoiceDisplay(choice, chance, lockReason, breakdown);
    }

    private (int chance, string breakdown) CalculateChance(SimChoice option)
    {
        // Mirrors the exact formula and Math.Round() / 5..95 clamping in Godot DialogueEngine.CalculateChance.
        var skill = _data.Skill(option.Id);
        var actorPersonality = _data.Personality(_actor);
        var partnerPersonality = _data.Personality(_partner);
        bool intimidating = skill is not null && skill.CheckMode.Equals("INTIMIDATION", StringComparison.OrdinalIgnoreCase);
        double relationship = intimidating
            ? GetValue("PARTNER_TO_ACTOR", "Fear") * .10 - GetValue("PARTNER_TO_ACTOR", "Trust") * .05
            : GetValue("PARTNER_TO_ACTOR", "Trust") * .20 + GetValue("PARTNER_TO_ACTOR", "Respect") * .10;
        var lines = new List<string> { $"Base: {option.BaseChance}%", $"Mode: {(intimidating ? "INTIMIDATION" : "STANDARD")}" };
        foreach (var modifier in _data.Modifiers(option.Id))
        {
            double adjustment = GetValue(modifier.Direction, modifier.Attribute) * modifier.Factor;
            relationship += adjustment;
            lines.Add($"Modifier {modifier.Direction} {modifier.Attribute} × {modifier.Factor:0.###} = {adjustment:+0.##;-0.##;0}");
        }
        double actorEffect = 0, defenderEffect = 0;
        if (skill is not null)
        {
            int a = actorPersonality.Get(skill.ActorSkill);
            int d = partnerPersonality.Get(skill.DefenderSkill);
            actorEffect = (a - 50) * skill.ActorFactor;
            defenderEffect = -(d - 50) * skill.DefenderFactor;
            lines.Add($"Actor {skill.ActorSkill} {a}/100 × {skill.ActorFactor:0.##} = {actorEffect:+0.##;-0.##;0}");
            lines.Add($"Defender {skill.DefenderSkill} {d}/100 × {skill.DefenderFactor:0.##} = {defenderEffect:+0.##;-0.##;0}");
        }
        double total = option.BaseChance + relationship + actorEffect + defenderEffect;
        int chance = Math.Clamp((int)Math.Round(total), 5, 95);
        lines.Insert(2, $"Relationship total: {relationship:+0.##;-0.##;0}");
        lines.Add($"Final = clamp(round({total:0.##}), 5, 95) = {chance}%");
        return (chance, string.Join("\n", lines));
    }

    private SimulationChoiceDisplay SelectedChoice()
    {
        if (_awaitingContinue || _finished || _node is null || ChoiceList.SelectedItem is not SimulationChoiceDisplay chosen)
            throw new InvalidOperationException("Start an interaction and choose an available option.");
        if (chosen.IsLocked) throw new InvalidOperationException($"This choice is locked: {chosen.LockReason}");
        var current = ChoiceStatus(chosen.Choice);
        if (current.IsLocked) throw new InvalidOperationException($"This choice is locked: {current.LockReason}");
        return current;
    }

    private IReadOnlyDictionary<string, int> Values()
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _stats)
        {
            values[ConditionalDialogueRules.StatKey("ACTOR_TO_PARTNER", row.Attribute)] = row.ActorToPartner;
            values[ConditionalDialogueRules.StatKey("PARTNER_TO_ACTOR", row.Attribute)] = row.PartnerToActor;
        }
        return values;
    }
    private int GetValue(string direction, string attribute) => Values().TryGetValue(
        ConditionalDialogueRules.StatKey(direction, attribute), out int value) ? value :
        throw new InvalidOperationException($"Invalid relationship direction or attribute: {direction}/{attribute}");
    private void CommitStats()
    {
        StatsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StatsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
    private void ValidateStats()
    {
        foreach (var stat in _stats)
        {
            int lower = stat.Attribute is "Fear" or "Romance" ? 0 : -100;
            if (stat.ActorToPartner < lower || stat.ActorToPartner > 100 ||
                stat.PartnerToActor < lower || stat.PartnerToActor > 100)
                throw new InvalidOperationException($"{stat.Attribute} must be in the range {lower} to 100.");
        }
    }
    private string Expand(string text) => text.Replace("{actor}", _data.Name(_actor), StringComparison.OrdinalIgnoreCase)
        .Replace("{partner}", _data.Name(_partner), StringComparison.OrdinalIgnoreCase);

    private void Validate_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (InteractionBox.SelectedItem is not SimInteraction interaction)
            throw new InvalidOperationException("Choose an interaction first.");
        InspectorText.Text = $"CONTENT STRUCTURE INSPECTION\n{_data.ValidateInteraction(interaction)}\n\n" +
            "This is a static reachability and condition-contradiction check, not proof that every path is playable under all relationship values.";
    });
    private void UpdateButtons()
    {
        bool ready = !_awaitingContinue && !_finished && _node is not null &&
                     ChoiceList.SelectedItem is SimulationChoiceDisplay { IsLocked: false };
        ForceSuccessButton.IsEnabled = ready;
        ForceFailureButton.IsEnabled = ready;
        RollButton.IsEnabled = ready;
        ContinueButton.IsEnabled = _awaitingContinue;
    }
    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Interaction Test Lab", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

public sealed class SimulationStatRow(string attribute)
{
    public string Attribute { get; } = attribute;
    public int ActorToPartner { get; set; }
    public int PartnerToActor { get; set; }
}
public sealed record SimulationChoiceDisplay(SimChoice Choice, int Chance, string? LockReason, string Breakdown)
{
    public bool IsLocked => LockReason is not null;
    public override string ToString() => IsLocked ? $"{Choice.Label} [LOCKED: {LockReason}]" :
        $"{Choice.Label} ({Chance}% success)";
}
