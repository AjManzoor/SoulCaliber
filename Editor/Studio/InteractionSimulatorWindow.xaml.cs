using SoulCaliber.Shared;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class InteractionSimulatorWindow : Window
{
    private readonly SimulationData _data;
    private readonly List<SimulationStatRow> _stats;
    private readonly Random _random = new();
    private SimInteraction? _interaction;
    private SimNode? _node;
    private string? _nextNode;
    private string _actor = "";
    private string _partner = "";
    private bool _awaitingContinue;
    private bool _finished;
    private readonly InteractionAuditService _auditService;
    private readonly List<SimulationTraitRow> _actorTraits = [];
    private readonly List<SimulationTraitRow> _partnerTraits = [];
    private string? _lastAuditSnapshot;
    private bool _loadingTraits;
    private string? _selectedTargetId;
    private string? _pendingTargetId;
    private int? _pendingTargetChoiceId;
    private bool _loadingTargetPicker;

    public InteractionSimulatorWindow(string databasePath)
    {
        InitializeComponent();
        _data = new SimulationData(databasePath);
        _auditService = new InteractionAuditService(_data);
        _stats = _data.Attributes.Where(a => a.IsActive).Select(a => new SimulationStatRow(a)).ToList();
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
        MechanicsText.Text = "Start an interaction, select a choice, then inspect all six stages.\n" +
            "Use the Personality and Status sandbox tabs for temporary tests.";
        InitialiseStatusControls();
        InitialiseThirdPartyStatusControls();
        InitialiseMemoryAndEncounterControls();
        UpdateButtons();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Safely(() => StartSimulation(null));

    private void StartSimulation(SimulationConversationEntry? selectedConversation)
    {
        CommitStats();
        ValidateStats();
        if (ActorBox.SelectedItem is not SimCharacter actor ||
            PartnerBox.SelectedItem is not SimCharacter partner ||
            InteractionBox.SelectedItem is not SimInteraction interaction)
            throw new InvalidOperationException("Select an initiator, a recipient, and an interaction.");
        if (actor.Id.Equals(partner.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose two different characters.");
        bool pairChanged = !_actor.Equals(actor.Id, StringComparison.OrdinalIgnoreCase) ||
                           !_partner.Equals(partner.Id, StringComparison.OrdinalIgnoreCase);
        _actor = actor.Id;
        _partner = partner.Id;
        if (pairChanged) ResetThirdPartyStatusSandbox();
        _selectedTargetId = null; // Restart clears the selected target like Godot DialogueEngine.Restart().
        _pendingTargetId = null;
        _pendingTargetChoiceId = null;
        if (pairChanged || _actorTraits.Count == 0) RestoreOriginalTraits();
        InitialiseStatusPair(pairChanged);
        _interaction = interaction;
        _activeConversation = selectedConversation;
        _node = interaction.Nodes.FirstOrDefault(n => n.Key.Equals(interaction.StartNodeKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Starting node '{interaction.StartNodeKey}' does not exist.");
        _awaitingContinue = false;
        _finished = false;
        _nextNode = null;
        _lastAuditSnapshot = null;
        OutcomeText.Text = "New test started. No real savegame was read or changed.";
        RenderNode();
        RefreshEncounterDiagnostics();
    }

    private void ResetValues_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        CommitStats();
        foreach (var stat in _stats) { stat.ActorToPartner = stat.Default; stat.PartnerToActor = stat.Default; }
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

    // A focused TabItem may request that the outer workspace ScrollViewer
    // scroll its entire 840px panel into view. Prevent that automatic jump when
    // switching tabs, while retaining normal user-driven scrolling.
    private void PreventTabAutoScroll_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void ChoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_awaitingContinue) Safely(UpdateTargetPicker);
        UpdateButtons();
        if (!_awaitingContinue) Safely(RefreshMechanicsAudit);
    }

    private void CopyAudit_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (!string.IsNullOrWhiteSpace(MechanicsText.Text)) Clipboard.SetText(MechanicsText.Text);
    });

    private void RestoreTraits_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_partner))
            throw new InvalidOperationException("Start an interaction before editing simulated personality traits.");
        RestoreOriginalTraits();
        RefreshMechanicsAudit();
    });

    private void RestoreOriginalTraits()
    {
        _loadingTraits = true;
        try
        {
            _actorTraits.Clear();
            _partnerTraits.Clear();
            var actorAssigned = _data.AssignedTraits(_actor);
            var partnerAssigned = _data.AssignedTraits(_partner);
            foreach (var trait in _data.Traits)
            {
                _actorTraits.Add(new SimulationTraitRow(trait.Key, trait.Name, actorAssigned.Contains(trait.Key)));
                _partnerTraits.Add(new SimulationTraitRow(trait.Key, trait.Name, partnerAssigned.Contains(trait.Key)));
            }
            ActorTraitsList.ItemsSource = null;
            PartnerTraitsList.ItemsSource = null;
            ActorTraitsList.ItemsSource = _actorTraits;
            PartnerTraitsList.ItemsSource = _partnerTraits;
            ActorTraitLabel.Text = _data.Name(_actor) + " — initiator";
            PartnerTraitLabel.Text = _data.Name(_partner) + " — recipient";
        }
        finally { _loadingTraits = false; }
    }

    private void TraitSelection_Changed(object sender, RoutedEventArgs e)
    {
        // Explicitly sync the row before calculating: WPF Checked/Unchecked can
        // arrive before a TwoWay binding has finished updating its source.
        if (sender is CheckBox box && box.DataContext is SimulationTraitRow trait)
            trait.IsSelected = box.IsChecked == true;
        if (!_loadingTraits && _node is not null && !_awaitingContinue)
            Safely(RefreshMechanicsAudit);
    }

    private PersonalityEffectRules SimulatedTraitRules() => _data.CreateSandboxTraitRules(
        _actorTraits.Where(t => t.IsSelected).Select(t => (_actor, t.Key))
        .Concat(_partnerTraits.Where(t => t.IsSelected).Select(t => (_partner, t.Key))));

    private InteractionChoiceAudit BuildAudit(SimChoice choice) =>
        _auditService.Build(choice, _actor, _partner, Values(), SimulatedTraitRules());

    private void RefreshMechanicsAudit()
    {
        if (_awaitingContinue && _lastAuditSnapshot is not null)
        {
            MechanicsText.Text = _lastAuditSnapshot;
            return;
        }
        if (_node is null || ChoiceList.SelectedItem is not SimulationChoiceDisplay selected)
        {
            MechanicsText.Text = _finished ? "Interaction complete. Restart to test another choice." :
                "Start an interaction and select a dialogue choice to see its complete mechanics.";
            return;
        }
        // Refresh from current in-memory relationship and trait inputs, not from any game save.
        var current = ChoiceStatus(selected.Choice);
        MechanicsText.Text = current.Audit.Describe(current.LockReason) + FormatStatusPreview(selected.Choice)
            + FormatPairStatusPreview(selected.Choice) + MemoryEffectPreview(selected.Choice);
    }
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
        // Resolve the target and pair effects against PRE-EFFECT state, as Godot does.
        string? selectedTarget = _data.DynamicTarget(choice.Id) is null ? null : _pendingTargetId;
        string? effectiveTarget = selectedTarget ?? _selectedTargetId;
        var pairChanges = PreviewPairStatusChanges(choice, succeeded, effectiveTarget);
        var pairAudit = FormatPairStatusPreview(choice);
        var before = Values();
        // Both dialogue lines are resolved against PRE-EFFECT values, like Godot's DialogueEngine.Choose().
        var actorLine = _data.Line(choice, _actor, _partner, succeeded, before, choice.GenericActorLine);
        var partnerLine = _data.Line(choice, _partner, _actor, succeeded, before,
            succeeded ? choice.GenericHostSuccess : choice.GenericHostFailure);
        // Use the same audit projection for both preview and actual application.
        // It was calculated against pre-effect relationships, exactly as Godot does.
        var audit = selected.Audit;
        var projected = succeeded ? audit.Success : audit.Failure;
        // Validate all discrete effects before modifying the sandbox, then
        // apply them after numeric effects just like Godot does.
        var simulatedStatusChanges = PreviewStatusChanges(choice, succeeded);
        var statusAudit = FormatStatusPreview(choice);
        var changes = new List<string>();
        foreach (var effect in projected.Effects)
        {
            var row = _stats.First(s => s.Attribute.Equals(effect.Attribute, StringComparison.OrdinalIgnoreCase));
            if (effect.Direction.Equals("ACTOR_TO_PARTNER", StringComparison.OrdinalIgnoreCase))
                row.ActorToPartner = effect.After;
            else row.PartnerToActor = effect.After;
            changes.Add($"{_data.Name(effect.ReceiverId)}'s {row.Attribute}: " +
                $"{effect.Before} → {effect.After} ({effect.AppliedDelta:+#;-#;0})" +
                $" | authored {effect.BaseDelta:+#;-#;0}, traits ×{effect.Multiplier:0.##}" +
                $" → {effect.AdjustedDelta:+#;-#;0} ({effect.TraitExplanation})" +
                (effect.WasClamped ? " [range limit]" : ""));
        }
        ApplySimulatedStatusChanges(simulatedStatusChanges);
        ApplySimulatedPairStatusChanges(pairChanges);
        if (selectedTarget is not null) _selectedTargetId = selectedTarget;
        var createdMemories = ApplySimulatedMemoryEffects(choice, succeeded);
        _lastAuditSnapshot = audit.Describe(null, succeeded, roll, testMode)
            + statusAudit + pairAudit + MemoryEffectPreview(choice);
        MechanicsText.Text = _lastAuditSnapshot;
        StatsGrid.Items.Refresh();
        _nextNode = succeeded ? choice.SuccessNext : choice.FailureNext;
        _awaitingContinue = true;
        _finished = false;
        var reaction = succeeded ? choice.SuccessReaction : choice.FailureReaction;
        string pairChangesText = pairChanges.Count == 0 ? "No third-party changes."
            : string.Join("\n", pairChanges.Select(c =>
                $"{_data.Name(c.From)} → {_data.Name(c.To)}: " +
                $"{_data.StatusName(c.Before)} → {_data.StatusName(c.After)}"));
        string statusChangesText = simulatedStatusChanges.Count == 0
            ? "No status changes."
            : string.Join("\n", simulatedStatusChanges.Select(c =>
                $"{DirectionLabel(c.Direction)}: {_data.StatusName(c.Before)} → {_data.StatusName(c.After)}"));
        OutcomeText.Text = $"{testMode} | {choice.Label}\n" +
            $"Chance: {selected.Chance}% | Test roll: {roll}/100 | {(succeeded ? "SUCCESS" : "FAILURE")}\n" +
            $"Target reaction: {reaction}\n\n" +
            $"{_data.Name(_actor)}: {Expand(actorLine.Text, effectiveTarget)}\nSource: {actorLine.Source}\n\n" +
            $"{_data.Name(_partner)}: {Expand(partnerLine.Text, effectiveTarget)}\nSource: {partnerLine.Source}\n\n" +
            $"RELATIONSHIP EFFECTS\n{(changes.Count == 0 ? "No effects configured." : string.Join("\n", changes))}\n\n" +
            $"STATUS CHANGES\n{statusChangesText}\n\n" +
            $"SELECTED TARGET: {(effectiveTarget is null ? "None" : _data.Name(effectiveTarget))}\n" +
            $"THIRD-PARTY STATUS CHANGES\n{pairChangesText}\n\n" +
            $"MEMORIES CREATED\n{(createdMemories.Count == 0 ? "None" : string.Join("\n", createdMemories.Select(m => m.ToString())))}\n\n" +
            $"Next node: {(string.IsNullOrWhiteSpace(_nextNode) ? "END" : _nextNode)}";
        _pendingTargetChoiceId = null;
        _pendingTargetId = null;
        InspectorText.Text = $"INITIATOR PHRASE\n{actorLine.Inspector}\n\nRECIPIENT PHRASE\n{partnerLine.Inspector}\n\n" +
                             "Skill/chance calculation:\n" + selected.Breakdown;
        UpdateTargetPicker();
        UpdateButtons();
    }

    private void Continue_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (!_awaitingContinue || _interaction is null) return;
        _awaitingContinue = false;
        if (string.IsNullOrWhiteSpace(_nextNode))
        {
            _finished = true;
            if (_activeConversation?.Encounter is not null)
                _memoryRuntime.Complete(_activeConversation, _actor, _partner);
            _activeConversation = null;
            RefreshEncounterDiagnostics();
            _node = null;
            ChoiceList.ItemsSource = null;
            UpdateTargetPicker();
            ChoiceAvailabilityText.Text = "Conversation complete. Restart to test further choices with the current sandbox state.";
            NodeLabel.Text = "Conversation complete";
            IntroductionText.Text = "Start another test to replay the interaction with the current simulated relationship values.";
            IntroductionSource.Text = "The previous outcome is shown on the right.";
            UpdateButtons();
            RefreshMechanicsAudit();
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
        _pendingTargetId = null;
        _pendingTargetChoiceId = null;
        var intro = _data.Intro(_node, _partner, _actor, Values());
        NodeLabel.Text = $"{_interaction?.Title} · Round {_node.Round} · {_node.Key}";
        IntroductionSource.Text = $"{_data.Name(_partner)} · {intro.Source}";
        IntroductionText.Text = Expand(intro.Text);
        InspectorText.Text = $"INTRODUCTION\n{intro.Inspector}\n\n" +
                             "Conditional lines are resolved using CURRENT in-memory stats.";
        RefreshChoiceAvailability();
    }

    private SimulationChoiceDisplay ChoiceStatus(SimChoice choice)
    {
        var checks = GetRequirementChecks(choice);
        var lockReason = checks.FirstOrDefault(x => !x.Passed)?.Description;
        var audit = BuildAudit(choice);
        return new SimulationChoiceDisplay(choice, audit.Chance.FinalChance, lockReason,
            audit.ChanceBreakdown, audit, _data.HideWhenUnmet(choice.Id), checks);
    }

    private SimulationChoiceDisplay SelectedChoice()
    {
        if (_awaitingContinue || _finished || _node is null || ChoiceList.SelectedItem is not SimulationChoiceDisplay chosen)
            throw new InvalidOperationException("Start an interaction and choose an available option.");
        if (chosen.IsLocked) throw new InvalidOperationException($"This choice is locked: {chosen.LockReason}");
        var current = ChoiceStatus(chosen.Choice);
        if (current.IsLocked) throw new InvalidOperationException(
            $"This choice is {(current.IsHidden ? "hidden" : "locked")}: {current.LockReason}");
        if (_data.DynamicTarget(chosen.Choice.Id) is not null &&
            (_pendingTargetChoiceId != chosen.Choice.Id ||
             _pendingTargetId is null ||
             !EligibleTargets(chosen.Choice).Any(x =>
                 x.Id.Equals(_pendingTargetId, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Choose an eligible target character before resolving this option.");
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
            if (stat.ActorToPartner < stat.Minimum || stat.ActorToPartner > stat.Maximum ||
                stat.PartnerToActor < stat.Minimum || stat.PartnerToActor > stat.Maximum)
                throw new InvalidOperationException($"{stat.Attribute} must be in the range {stat.Minimum} to {stat.Maximum}.");
        }
    }
    private string Expand(string text, string? chosenTarget = null)
    {
        string expanded = text.Replace("{actor}", _data.Name(_actor), StringComparison.OrdinalIgnoreCase)
            .Replace("{partner}", _data.Name(_partner), StringComparison.OrdinalIgnoreCase)
            .Replace("{initiator}", _data.Name(_actor), StringComparison.OrdinalIgnoreCase)
            .Replace("{recipient}", _data.Name(_partner), StringComparison.OrdinalIgnoreCase);
        string? name = chosenTarget ?? _selectedTargetId;
        return SoulCaliber.Dialogue.DynamicTargetRules.FormatTarget(
            expanded, name is null ? null : _data.Name(name));
    }

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
                     ChoiceList.SelectedItem is SimulationChoiceDisplay { IsLocked: false } available &&
                     (_data.DynamicTarget(available.Choice.Id) is null ||
                      (_pendingTargetChoiceId == available.Choice.Id && _pendingTargetId is not null));
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class SimulationStatRow(RelationshipAttributeDefinition definition)
{
    public string Attribute { get; } = definition.Key;
    public string Name { get; } = definition.Name;
    public int Minimum { get; } = definition.Minimum;
    public int Maximum { get; } = definition.Maximum;
    public int Default { get; } = definition.Default;
    public int ActorToPartner { get; set; } = definition.Default;
    public int PartnerToActor { get; set; } = definition.Default;
}
public sealed record SimulationChoiceDisplay(SimChoice Choice, int Chance, string? LockReason,
    string Breakdown, InteractionChoiceAudit Audit, bool HideWhenUnmet,
    IReadOnlyList<SimulationRequirementCheck> RequirementChecks)
{
    public bool IsLocked => LockReason is not null;
    public bool IsHidden => IsLocked && HideWhenUnmet;
    public override string ToString() => IsLocked ? $"{Choice.Label} [LOCKED: {LockReason}]" :
        $"{Choice.Label} ({Chance}% success)";
}

public sealed class SimulationTraitRow(string key, string name, bool isSelected)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public bool IsSelected { get; set; } = isSelected;
}
