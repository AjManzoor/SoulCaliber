using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

/// <summary>
/// Phase 13F in-memory gameplay history and encounter testing.
/// Uses the Godot-identical MemoryConditionRules evaluator through SimulationMemoryRuntime.
/// </summary>
public partial class InteractionSimulatorWindow
{
    private SimulationMemoryRuntime _memoryRuntime = null!;
    private SimulationConversationEntry? _activeConversation;

    private void InitialiseMemoryAndEncounterControls()
    {
        _memoryRuntime = new SimulationMemoryRuntime(_data);
        _memoryRuntime.TraitCheckOverride = (characterId, traitKey) =>
        {
            var rows = string.Equals(characterId, _actor, StringComparison.OrdinalIgnoreCase)
                ? _actorTraits
                : string.Equals(characterId, _partner, StringComparison.OrdinalIgnoreCase)
                    ? _partnerTraits : null;
            return rows is null || rows.Count == 0 ? _data.AssignedTraits(characterId).Contains(traitKey)
                : rows.Any(t => t.IsSelected && string.Equals(t.Key, traitKey, StringComparison.OrdinalIgnoreCase));
        };
        MemoryTypeBox.ItemsSource = _data.Memories;
        MemoryTypeBox.SelectedIndex = _data.Memories.Count > 0 ? 0 : -1;
        MemoryAffectedBox.ItemsSource = new[] { new { Key = "PARTNER", Label = "Recipient" }, new { Key = "ACTOR", Label = "Initiator" }, new { Key = "BOTH", Label = "Both" } };
        MemoryAffectedBox.DisplayMemberPath = "Label";
        MemoryAffectedBox.SelectedValuePath = "Key";
        MemoryAffectedBox.SelectedIndex = 0;
        MemorySeverityBox.ItemsSource = new[] { "Catalogue default", "MAJOR", "MINOR" };
        MemorySeverityBox.SelectedIndex = 0;
        MemoryKnowledgeBox.ItemsSource = new[] { "Catalogue default", "QUIET", "ALLIES", "GLOBAL" };
        MemoryKnowledgeBox.SelectedIndex = 0;
        MemoryGrantBox.ItemsSource = _data.Characters;
        MemoryGrantBox.SelectedIndex = _data.Characters.Count > 0 ? 0 : -1;
        MemoryDetailsText.Text = "No simulated occurrences. Start an interaction, then record one manually or through a dialogue outcome.";
        EncounterAvailabilityText.Text = "Start an interaction to evaluate normal and special conversations for the selected character pair.";
    }

    // All directional statuses, including third-party changes from dialogue choices,
    // use one temporary world sandbox. The two visible participant selectors remain
    // the source of truth for the current speaking pair.
    private string CurrentSimulatedStatus(string from, string to)
    {
        if (string.Equals(from, _actor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(to, _partner, StringComparison.OrdinalIgnoreCase)) return _actorStatus;
        if (string.Equals(from, _partner, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(to, _actor, StringComparison.OrdinalIgnoreCase)) return _partnerStatus;
        return _sandboxPairStatuses.TryGetValue(StatusPairKey(from, to), out var status)
            ? status : _data.StartingStatus(from, to);
    }

    private void AddMemory_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        RequirePair();
        if (MemoryTypeBox.SelectedItem is not SimMemoryDefinition definition)
            throw new InvalidOperationException("Select an active memory type.");
        var affected = AffectedFor(MemoryAffectedBox.SelectedValue as string ?? "PARTNER");
        var memory = _memoryRuntime.Record(definition.Key, _actor, _actor, affected,
            Override(MemorySeverityBox), Override(MemoryKnowledgeBox), CurrentSimulatedStatus);
        RefreshMemoryList(memory.Id);
        RefreshAfterMemoryChange();
    });

    private static string? Override(ComboBox box) => box.SelectedIndex < 1 ? null : box.SelectedItem as string;
    private string[] AffectedFor(string affected) => affected switch
    {
        "ACTOR" => [_actor], "PARTNER" => [_partner], "BOTH" => [_actor, _partner],
        _ => throw new InvalidOperationException("Unknown affected character configuration.")
    };
    private void RequirePair()
    {
        if (string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_partner))
            throw new InvalidOperationException("Start an interaction with two different characters first.");
    }

    private void GrantMemoryKnowledge_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (MemoryHistoryBox.SelectedItem is not SimulatedMemory memory ||
            MemoryGrantBox.SelectedItem is not SimCharacter character)
            throw new InvalidOperationException("Select a memory and the character who learns about it.");
        _memoryRuntime.Grant(memory.Id, character.Id);
        UpdateMemoryDetails();
        RefreshAfterMemoryChange();
    });

    private void RemoveMemory_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (MemoryHistoryBox.SelectedItem is not SimulatedMemory memory)
            throw new InvalidOperationException("Select a simulated memory to remove.");
        _memoryRuntime.Remove(memory.Id);
        RefreshMemoryList();
        RefreshAfterMemoryChange();
    });

    private void ClearMemorySandbox_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        _memoryRuntime.ResetMemories();
        RefreshMemoryList();
        RefreshAfterMemoryChange();
    });

    private void MemoryHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_memoryRuntime is not null) UpdateMemoryDetails();
    }
    private void UpdateMemoryDetails()
    {
        if (MemoryHistoryBox.SelectedItem is not SimulatedMemory memory)
        {
            MemoryDetailsText.Text = _memoryRuntime is null || _memoryRuntime.History.Count == 0
                ? "No memories recorded yet." : "Select an occurrence to inspect its affected characters and knowers.";
            return;
        }
        var known = memory.Knowledge == "GLOBAL"
            ? "Everyone (including future characters)"
            : string.Join(", ", memory.Knowers.OrderBy(x => x).Select(_data.Name));
        MemoryDetailsText.Text = $"Memory #{memory.Id}: {memory.Key}\n" +
            $"Actor: {_data.Name(memory.Actor)} · Player: {_data.Name(memory.Player)}\n" +
            $"Affected: {string.Join(", ", memory.Affected.Select(_data.Name))}\n" +
            $"Severity: {memory.Severity} · Distribution: {memory.Knowledge}\n" +
            $"Knows: {known}";
    }
    private void RefreshMemoryList(long? selectId = null)
    {
        selectId ??= (MemoryHistoryBox.SelectedItem as SimulatedMemory)?.Id;
        MemoryHistoryBox.ItemsSource = null;
        MemoryHistoryBox.ItemsSource = _memoryRuntime.History.ToList();
        MemoryHistoryBox.SelectedItem = _memoryRuntime.History.FirstOrDefault(m => m.Id == selectId);
        UpdateMemoryDetails();
    }

    // Selection of an interaction, outcomes and subsequent rounds all share
    // this exact in-memory history; a new test with the same or another pair
    // does not inadvertently delete existing simulated world memories.
    private void RefreshAfterMemoryChange()
    {
        if (_node is not null && !_awaitingContinue) RefreshChoiceAvailability();
        RefreshEncounterDiagnostics();
    }

    private IReadOnlyList<SimulatedMemory> ApplySimulatedMemoryEffects(SimChoice choice, bool success)
    {
        var created = new List<SimulatedMemory>();
        foreach (var effect in _data.MemoryEffects(choice.Id))
        {
            bool fire = effect.Trigger switch
            {
                "SELECTED" or "BOTH" => true,
                "SUCCESS" => success,
                "FAILURE" => !success,
                _ => throw new InvalidOperationException("Unknown memory effect trigger: " + effect.Trigger)
            };
            if (!fire) continue;
            created.Add(_memoryRuntime.Record(effect.MemoryKey, _actor, _actor,
                AffectedFor(effect.Affected), effect.SeverityOverride,
                effect.KnowledgeOverride, CurrentSimulatedStatus, effect.CustomAudienceJson));
        }
        RefreshMemoryList(created.LastOrDefault()?.Id);
        RefreshEncounterDiagnostics();
        return created;
    }
    private string MemoryEffectPreview(SimChoice choice)
    {
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("MEMORY CREATION — AUTHORING PREVIEW");
        sb.AppendLine(new string('─', 56));
        foreach (bool success in new[] { true, false })
        {
            sb.AppendLine(success ? "On SUCCESS:" : "On FAILURE:");
            var effects = _data.MemoryEffects(choice.Id).Where(e =>
                (e.Trigger is "SELECTED" or "BOTH") || (success && e.Trigger == "SUCCESS") ||
                (!success && e.Trigger == "FAILURE")).ToList();
            if (effects.Count == 0) sb.AppendLine("  No memories recorded.");
            foreach (var effect in effects)
                sb.AppendLine($"  {effect.MemoryKey} · {effect.Affected} · " +
                    $"{effect.SeverityOverride ?? "catalogue severity"} / {(effect.CustomAudienceJson is not null ? "custom audiences" : effect.KnowledgeOverride ?? "catalogue knowledge")}");
        }
        return sb.ToString();
    }

    private void PreviewConversations_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (ActorBox.SelectedItem is not SimCharacter actor ||
            PartnerBox.SelectedItem is not SimCharacter recipient ||
            string.Equals(actor.Id, recipient.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select two different characters.");
        bool changed = !string.Equals(_actor, actor.Id, StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(_partner, recipient.Id, StringComparison.OrdinalIgnoreCase);
        _actor = actor.Id;
        _partner = recipient.Id;
        InitialiseStatusPair(changed);
        // Avoid leaving a running dialogue from the previous character pair.
        _interaction = null;
        _node = null;
        _awaitingContinue = false;
        _finished = false;
        _activeConversation = null;
        ChoiceList.ItemsSource = null;
        ChoiceAvailabilityText.Text = "Select a conversation to test its dialogue choices.";
        NodeLabel.Text = "Conversation preview";
        IntroductionText.Text = "Select a qualifying conversation from the Encounters tab, or start an interaction directly.";
        UpdateButtons();
        RefreshEncounterDiagnostics();
    });

    private void RefreshEncounters_Click(object sender, RoutedEventArgs e) => Safely(RefreshEncounterDiagnostics);
    private void RefreshEncounterDiagnostics()
    {
        if (string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_partner)) return;
        var previous = EncounterList.SelectedItem as SimulationConversationEntry;
        var rows = _memoryRuntime.Inspect(_actor, _partner, CurrentSimulatedStatus);
        EncounterList.ItemsSource = null;
        EncounterList.ItemsSource = rows;
        EncounterList.SelectedItem = rows.FirstOrDefault(r => r.Key == previous?.Key &&
            r.AnchorMemoryId == previous?.AnchorMemoryId);
        var sb = new StringBuilder();
        sb.AppendLine($"{_data.Name(_actor)} approaches {_data.Name(_partner)}");
        sb.AppendLine($"{rows.Count(r => r.Available)} selectable / " +
            $"{rows.Count(r => r.Status == "Suppressed")} suppressed / " +
            $"{rows.Count(r => r.Status == "Not eligible")} ineligible");
        sb.AppendLine($"Simulated memory occurrences: {_memoryRuntime.History.Count}; " +
            $"special completions: {_memoryRuntime.Completions.Count}");
        sb.AppendLine();
        foreach (var row in rows)
            sb.AppendLine($"{row.Status.ToUpperInvariant()} · {row.Category} · {row.Title} [{row.Key}]\n" +
                $"  Dialogue: {row.InteractionKey} · {row.Explanation}\n");
        if (rows.Count == 0) sb.AppendLine("No availability or special encounter rules authored in Studio.");
        EncounterAvailabilityText.Text = sb.ToString();
    }

    private void StartSelectedConversation_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        RequirePair();
        if (ActorBox.SelectedItem is not SimCharacter initiator ||
            PartnerBox.SelectedItem is not SimCharacter recipient ||
            !string.Equals(initiator.Id, _actor, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(recipient.Id, _partner, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The character selectors changed. Preview conversations first.");
        if (EncounterList.SelectedItem is not SimulationConversationEntry selected || !selected.Available)
            throw new InvalidOperationException("Select an eligible (AVAILABLE) conversation first.");
        var target = _data.Interactions.FirstOrDefault(i =>
            string.Equals(i.Key, selected.InteractionKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Linked dialogue interaction is missing: " + selected.InteractionKey);
        InteractionBox.SelectedItem = target;
        StartSimulation(selected);
    });

    private void CompleteSelectedEncounter_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        RequirePair();
        if (EncounterList.SelectedItem is not SimulationConversationEntry selected ||
            selected.Encounter is null)
            throw new InvalidOperationException("Select an eligible special encounter, not a normal conversation.");
        _memoryRuntime.Complete(selected, _actor, _partner);
        RefreshEncounterDiagnostics();
    });

    private void ClearEncounterCompletions_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        _memoryRuntime.ResetCompletions();
        RefreshEncounterDiagnostics();
    });
}
