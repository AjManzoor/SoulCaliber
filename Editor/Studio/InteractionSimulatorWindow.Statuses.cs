using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

/// <summary>
/// Phase 12E: in-memory directional status sandbox. This part of the Test Lab
/// never writes to the content database or to the game's savegame.
/// </summary>
public partial class InteractionSimulatorWindow
{
    private string _actorStatus = "NEUTRAL";
    private string _partnerStatus = "NEUTRAL";
    private bool _loadingStatuses;

    private void InitialiseStatusControls()
    {
        _loadingStatuses = true;
        try
        {
            ActorStatusBox.ItemsSource = _data.Statuses;
            PartnerStatusBox.ItemsSource = _data.Statuses;
            ActorStatusBox.IsEnabled = false;
            PartnerStatusBox.IsEnabled = false;
            StatusDefaultsText.Text = "Start an interaction to load authored starting statuses.";
            ChoiceAvailabilityText.Text = "Start an interaction to inspect locked and hidden choices.";
        }
        finally { _loadingStatuses = false; }
    }

    private void SetStatusPair(string actorStatus, string partnerStatus)
    {
        _loadingStatuses = true;
        try
        {
            _actorStatus = actorStatus;
            _partnerStatus = partnerStatus;
            ActorStatusBox.SelectedValue = _actorStatus;
            PartnerStatusBox.SelectedValue = _partnerStatus;
        }
        finally { _loadingStatuses = false; }
        UpdateStatusSummary();
    }

    private void InitialiseStatusPair(bool pairChanged)
    {
        if (pairChanged)
            SetStatusPair(_data.StartingStatus(_actor, _partner), _data.StartingStatus(_partner, _actor));
        else
            UpdateStatusSummary();
        ActorStatusBox.IsEnabled = true;
        PartnerStatusBox.IsEnabled = true;
    }

    private void UpdateStatusSummary()
    {
        if (string.IsNullOrEmpty(_actor) || string.IsNullOrEmpty(_partner)) return;
        var actorName = _data.Name(_actor);
        var partnerName = _data.Name(_partner);
        ActorStatusLabel.Text = $"{actorName} → {partnerName}";
        PartnerStatusLabel.Text = $"{partnerName} → {actorName}";
        StatusDefaultsText.Text = $"Authored defaults: {actorName} → {partnerName}: " +
            $"{_data.StatusName(_data.StartingStatus(_actor, _partner))}; " +
            $"{partnerName} → {actorName}: " +
            $"{_data.StatusName(_data.StartingStatus(_partner, _actor))}.";
    }

    private void StatusSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStatuses || string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_partner)) return;
        if (ActorStatusBox.SelectedValue is not string actor || PartnerStatusBox.SelectedValue is not string partner) return;
        _actorStatus = actor;
        _partnerStatus = partner;
        if (_node is not null && !_awaitingContinue)
            Safely(RefreshChoiceAvailability);
        else Safely(RefreshEncounterDiagnostics);
    }

    private void RestoreStatuses_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_partner))
            throw new InvalidOperationException("Start an interaction before restoring statuses.");
        SetStatusPair(_data.StartingStatus(_actor, _partner), _data.StartingStatus(_partner, _actor));
        if (_node is not null && !_awaitingContinue) RefreshChoiceAvailability();
        else RefreshEncounterDiagnostics();
    });

    private IReadOnlyList<SimulationRequirementCheck> GetRequirementChecks(SimChoice choice)
    {
        var checks = new List<SimulationRequirementCheck>();
        foreach (var requirement in _data.Requirements(choice.Id))
        {
            int actual = GetValue(requirement.Direction, requirement.Attribute);
            string expected = string.Join(" and ", new[]
            {
                requirement.Minimum is int min ? $"≥ {min}" : null,
                requirement.Maximum is int max ? $"≤ {max}" : null
            }.Where(x => x is not null));
            bool passes = (requirement.Minimum is null || actual >= requirement.Minimum.Value) &&
                          (requirement.Maximum is null || actual <= requirement.Maximum.Value);
            checks.Add(new SimulationRequirementCheck(passes,
                $"{DirectionLabel(requirement.Direction)} · {requirement.Attribute} = {actual}, requires {expected}"));
        }
        foreach (var requirement in _data.StatusRequirements(choice.Id))
        {
            bool matchesActor = _actorStatus.Equals(requirement.StatusKey, StringComparison.OrdinalIgnoreCase);
            bool matchesPartner = _partnerStatus.Equals(requirement.StatusKey, StringComparison.OrdinalIgnoreCase);
            bool passes = requirement.Direction.ToUpperInvariant() switch
            {
                "ACTOR_TO_PARTNER" => matchesActor,
                "PARTNER_TO_ACTOR" => matchesPartner,
                "BOTH" => matchesActor && matchesPartner,
                _ => throw new InvalidOperationException($"Invalid status requirement direction: {requirement.Direction}")
            };
            string actual = requirement.Direction.ToUpperInvariant() switch
            {
                "ACTOR_TO_PARTNER" => _data.StatusName(_actorStatus),
                "PARTNER_TO_ACTOR" => _data.StatusName(_partnerStatus),
                "BOTH" => $"{_data.StatusName(_actorStatus)} / {_data.StatusName(_partnerStatus)}",
                _ => "unavailable"
            };
            checks.Add(new SimulationRequirementCheck(passes,
                $"{DirectionLabel(requirement.Direction)} · status = {actual}, requires {_data.StatusName(requirement.StatusKey)}"));
        }
        var advanced = _data.OptionConditions(choice.Id);
        if (advanced is not null)
        {
            string failure = _memoryRuntime.CheckChoiceConditions(advanced, _actor, _partner,
                CurrentSimulatedStatus);
            checks.Add(new SimulationRequirementCheck(failure.Length == 0,
                failure.Length == 0 ? "Advanced memory/status AND / OR / NOT: all passed" : failure));
        }
        return checks;
    }

    private static string DirectionLabel(string direction) => direction.ToUpperInvariant() switch
    {
        "ACTOR_TO_PARTNER" => "Initiator → Recipient",
        "PARTNER_TO_ACTOR" => "Recipient → Initiator",
        "BOTH" => "Both directions",
        _ => direction
    };

    private IReadOnlyList<SimulatedStatusChange> PreviewStatusChanges(SimChoice choice, bool success)
    {
        string nextActor = _actorStatus;
        string nextPartner = _partnerStatus;
        var changes = new List<SimulatedStatusChange>();
        var modifiedDirections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var effect in _data.StatusEffects(choice.Id, success))
        {
            if (!_data.Statuses.Any(x => x.Key.Equals(effect.StatusKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Status '{effect.StatusKey}' in a dialogue effect is missing or inactive.");
            switch (effect.Direction.ToUpperInvariant())
            {
                case "ACTOR_TO_PARTNER": Change(true, effect.StatusKey); break;
                case "PARTNER_TO_ACTOR": Change(false, effect.StatusKey); break;
                case "BOTH": Change(true, effect.StatusKey); Change(false, effect.StatusKey); break;
                default: throw new InvalidOperationException($"Invalid status effect direction: {effect.Direction}");
            }
        }
        return changes;

        void Change(bool fromActor, string target)
        {
            string direction = fromActor ? "ACTOR_TO_PARTNER" : "PARTNER_TO_ACTOR";
            if (!modifiedDirections.Add(direction))
                throw new InvalidOperationException("Conflicting status effects for the same outcome and direction.");
            string before = fromActor ? nextActor : nextPartner;
            if (before.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
            changes.Add(new SimulatedStatusChange(direction, before, target));
            if (fromActor) nextActor = target;
            else nextPartner = target;
        }
    }

    private void ApplySimulatedStatusChanges(IReadOnlyList<SimulatedStatusChange> changes)
    {
        string actor = _actorStatus;
        string partner = _partnerStatus;
        foreach (var change in changes)
        {
            if (change.Direction.Equals("ACTOR_TO_PARTNER", StringComparison.OrdinalIgnoreCase)) actor = change.After;
            else partner = change.After;
        }
        SetStatusPair(actor, partner);
    }

    private string FormatStatusPreview(SimChoice choice)
    {
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("DISCRETE RELATIONSHIP STATUSES — SUCCESS / FAILURE PREVIEWS");
        sb.AppendLine(new string('─', 56));
        sb.AppendLine($"Current: {_data.Name(_actor)} → {_data.Name(_partner)} = {_data.StatusName(_actorStatus)}; " +
                      $"{_data.Name(_partner)} → {_data.Name(_actor)} = {_data.StatusName(_partnerStatus)}");
        foreach (bool success in new[] { true, false })
        {
            var changes = PreviewStatusChanges(choice, success);
            sb.AppendLine($"On {(success ? "SUCCESS" : "FAILURE")}:");
            if (changes.Count == 0) sb.AppendLine("  No status changes.");
            foreach (var change in changes)
                sb.AppendLine($"  {DirectionLabel(change.Direction)}: " +
                    $"{_data.StatusName(change.Before)} → {_data.StatusName(change.After)}");
        }
        sb.AppendLine("Status changes do not alter the current roll or receive personality multipliers.");
        return sb.ToString();
    }

    private void RefreshChoiceAvailability()
    {
        if (_node is null) return;
        var previousChoiceId = (ChoiceList.SelectedItem as SimulationChoiceDisplay)?.Choice.Id;
        var all = _node.Options.Select(ChoiceStatus).ToList();
        var visible = all.Where(x => !x.IsHidden).ToList();
        ChoiceList.ItemsSource = visible;
        ChoiceList.SelectedItem = visible.FirstOrDefault(x => x.Choice.Id == previousChoiceId)
                                  ?? visible.FirstOrDefault();

        var sb = new StringBuilder();
        sb.AppendLine($"{_data.Name(_actor)} → {_data.Name(_partner)}: {_data.StatusName(_actorStatus)}");
        sb.AppendLine($"{_data.Name(_partner)} → {_data.Name(_actor)}: {_data.StatusName(_partnerStatus)}");
        sb.AppendLine($"{visible.Count} visible / {all.Count - visible.Count} hidden choices");
        sb.AppendLine();
        foreach (var display in all)
        {
            sb.AppendLine($"{(display.IsHidden ? "HIDDEN" : display.IsLocked ? "LOCKED" : "AVAILABLE")} · {display.Choice.Label} [{display.Choice.Key}]");
            if (display.RequirementChecks.Count == 0) sb.AppendLine("  No requirements.");
            foreach (var check in display.RequirementChecks)
                sb.AppendLine($"  {(check.Passed ? "PASS" : "FAIL")} · {check.Description}");
            if (display.IsHidden)
                sb.AppendLine("  Authored visibility: HIDE when unmet (not offered by Godot).");
            else if (display.IsLocked)
                sb.AppendLine("  Authored visibility: LOCK when unmet (visible, not selectable).");
            sb.AppendLine();
        }
        ChoiceAvailabilityText.Text = sb.ToString();
        UpdateButtons();
        RefreshMechanicsAudit();
        RefreshEncounterDiagnostics();
    }
}

public sealed record SimulationRequirementCheck(bool Passed, string Description);
public sealed record SimulatedStatusChange(string Direction, string Before, string After);
