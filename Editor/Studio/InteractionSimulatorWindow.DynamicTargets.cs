using SoulCaliber.Dialogue;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

/// <summary>
/// Phase 13H-5: live target picker and sandboxed world-status preview. Selection
/// uses the exact pure DynamicTargetRules implementation linked into Godot.
/// All status updates remain in memory, never in the authoring DB or a player save.
/// </summary>
public partial class InteractionSimulatorWindow
{
    private readonly Dictionary<(string From, string To), string> _sandboxPairStatuses = [];

    private static (string From, string To) StatusPairKey(string from, string to) =>
        (from.ToUpperInvariant(), to.ToUpperInvariant());

    private void InitialiseThirdPartyStatusControls()
    {
        PairFromBox.ItemsSource = _data.Characters;
        PairToBox.ItemsSource = _data.Characters;
        PairStatusBox.ItemsSource = _data.Statuses;
        PairFromBox.SelectedIndex = 0;
        PairToBox.SelectedIndex = _data.Characters.Count > 1 ? 1 : 0;
        PairStatusBox.SelectedValue = "NEUTRAL";
        TargetPickerPanel.Visibility = Visibility.Collapsed;
        RefreshThirdPartyStatusSummary();
    }

    private void ResetThirdPartyStatusSandbox()
    {
        _sandboxPairStatuses.Clear();
        RefreshThirdPartyStatusSummary();
    }

    private void RefreshThirdPartyStatusSummary()
    {
        PairStatusOverridesList.ItemsSource = _sandboxPairStatuses
            .OrderBy(x => _data.Name(x.Key.From), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => _data.Name(x.Key.To), StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{_data.Name(x.Key.From)} → {_data.Name(x.Key.To)}: " +
                         $"{_data.StatusName(_data.StartingStatus(x.Key.From, x.Key.To))} → {_data.StatusName(x.Value)}")
            .ToArray();
    }

    private void SetPairStatus_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        RequirePair();
        if (_awaitingContinue)
            throw new InvalidOperationException("Finish the current choice outcome before editing statuses.");
        if (PairFromBox.SelectedItem is not SimCharacter from ||
            PairToBox.SelectedItem is not SimCharacter to ||
            PairStatusBox.SelectedValue is not string status)
            throw new InvalidOperationException("Select two characters and a valid status.");
        if (from.Id.Equals(to.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a self-relationship.");

        if (from.Id.Equals(_actor, StringComparison.OrdinalIgnoreCase) &&
            to.Id.Equals(_partner, StringComparison.OrdinalIgnoreCase))
            SetStatusPair(status, _partnerStatus);
        else if (from.Id.Equals(_partner, StringComparison.OrdinalIgnoreCase) &&
                 to.Id.Equals(_actor, StringComparison.OrdinalIgnoreCase))
            SetStatusPair(_actorStatus, status);
        else
            _sandboxPairStatuses[StatusPairKey(from.Id, to.Id)] = status;

        RefreshThirdPartyStatusSummary();
        if (_node is not null) RefreshChoiceAvailability();
        else RefreshEncounterDiagnostics();
    });

    private void ResetPairStatuses_Click(object sender, RoutedEventArgs e) => Safely(() =>
    {
        if (_awaitingContinue)
            throw new InvalidOperationException("Finish the current choice outcome before resetting statuses.");
        ResetThirdPartyStatusSandbox();
        if (_node is not null) RefreshChoiceAvailability();
        else RefreshEncounterDiagnostics();
    });

    private IReadOnlyList<SimCharacter> EligibleTargets(SimChoice choice)
    {
        var rule = _data.DynamicTarget(choice.Id);
        if (rule is null) return [];
        var ids = DynamicTargetRules.Find(rule, _actor, _partner,
            _data.Characters.Select(x => x.Id), CurrentSimulatedStatus);
        var allowed = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _data.Characters.Where(x => allowed.Contains(x.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void UpdateTargetPicker()
    {
        if (_data is null || TargetPickerBox is null) return;
        var choice = (ChoiceList.SelectedItem as SimulationChoiceDisplay)?.Choice;
        var rule = choice is null ? null : _data.DynamicTarget(choice.Id);
        _loadingTargetPicker = true;
        try
        {
            if (_awaitingContinue || rule is null)
            {
                TargetPickerBox.ItemsSource = null;
                TargetPickerPanel.Visibility = Visibility.Collapsed;
                _pendingTargetChoiceId = null;
                _pendingTargetId = null;
                return;
            }
            if (_pendingTargetChoiceId != choice!.Id)
            {
                _pendingTargetChoiceId = choice.Id;
                _pendingTargetId = null;
            }
            var candidates = EligibleTargets(choice);
            TargetPickerBox.ItemsSource = candidates;
            TargetPickerBox.SelectedItem = candidates.FirstOrDefault(x =>
                x.Id.Equals(_pendingTargetId, StringComparison.OrdinalIgnoreCase));
            if (TargetPickerBox.SelectedItem is null) _pendingTargetId = null;
            TargetPickerPanel.Visibility = Visibility.Visible;
            TargetPickerHelp.Text = candidates.Count == 0
                ? "No eligible third-party characters. Adjust relationships or the authored custom list."
                : rule.SelectionMode == "CUSTOM"
                    ? $"Custom list: {candidates.Count} eligible. Relationship status is ignored. Pick one before resolving."
                    : $"Relationship group: {rule.OwnerRef} · {rule.RelationshipDirection} · " +
                      $"{_data.StatusName(rule.RequiredStatusKey)} ({candidates.Count} eligible).";
        }
        finally { _loadingTargetPicker = false; }
    }

    private void TargetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTargetPicker) return;
        _pendingTargetId = (TargetPickerBox.SelectedItem as SimCharacter)?.Id;
        UpdateButtons();
        if (_node is not null && !_awaitingContinue) Safely(RefreshMechanicsAudit);
    }

    private string ResolveCharacterId(string characterId) =>
        _data.Characters.FirstOrDefault(c => c.Id.Equals(characterId, StringComparison.OrdinalIgnoreCase))?.Id
        ?? throw new InvalidOperationException($"Unknown third-party character: {characterId}");

    private IReadOnlyList<SimulatedPairStatusChange> PreviewPairStatusChanges(
        SimChoice choice, bool success, string? effectiveTarget)
    {
        var changes = new List<SimulatedPairStatusChange>();
        var effects = _data.PairStatusEffects(choice.Id, success);
        var resolved = DynamicTargetRules.ResolvePairEffects(effects, _actor, _partner,
            effectiveTarget, ResolveCharacterId);
        // Godot's SQLite status store rejects duplicate directional targets across
        // the direct and pair-effect families, even when the target status is unchanged.
        var occupied = new HashSet<(string From, string To)>();
        foreach (var direct in _data.StatusEffects(choice.Id, success))
        {
            switch (direct.Direction.ToUpperInvariant())
            {
                case "ACTOR_TO_PARTNER": occupied.Add(StatusPairKey(_actor, _partner)); break;
                case "PARTNER_TO_ACTOR": occupied.Add(StatusPairKey(_partner, _actor)); break;
                case "BOTH":
                    occupied.Add(StatusPairKey(_actor, _partner));
                    occupied.Add(StatusPairKey(_partner, _actor));
                    break;
                default: throw new InvalidOperationException("Unknown status direction: " + direct.Direction);
            }
        }
        var projected = new Dictionary<(string From, string To), string>();
        foreach (var effect in resolved)
        {
            if (!occupied.Add(StatusPairKey(effect.FromCharacterId, effect.ToCharacterId)))
                throw new InvalidOperationException("Conflicting status effects for the same outcome and direction.");
            if (!_data.Statuses.Any(x => x.Key.Equals(effect.StatusKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Status '{effect.StatusKey}' is missing or inactive.");
            var key = StatusPairKey(effect.FromCharacterId, effect.ToCharacterId);
            string before = projected.TryGetValue(key, out var staged)
                ? staged : CurrentSimulatedStatus(effect.FromCharacterId, effect.ToCharacterId);
            if (before.Equals(effect.StatusKey, StringComparison.OrdinalIgnoreCase)) continue;
            projected[key] = effect.StatusKey;
            changes.Add(new SimulatedPairStatusChange(effect.FromCharacterId,
                effect.ToCharacterId, before, effect.StatusKey));
        }
        return changes;
    }

    private void ApplySimulatedPairStatusChanges(IReadOnlyList<SimulatedPairStatusChange> changes)
    {
        string forward = _actorStatus, reverse = _partnerStatus;
        foreach (var change in changes)
        {
            if (change.From.Equals(_actor, StringComparison.OrdinalIgnoreCase) &&
                change.To.Equals(_partner, StringComparison.OrdinalIgnoreCase)) forward = change.After;
            else if (change.From.Equals(_partner, StringComparison.OrdinalIgnoreCase) &&
                     change.To.Equals(_actor, StringComparison.OrdinalIgnoreCase)) reverse = change.After;
            else _sandboxPairStatuses[StatusPairKey(change.From, change.To)] = change.After;
        }
        SetStatusPair(forward, reverse);
        RefreshThirdPartyStatusSummary();
    }

    private string FormatPairStatusPreview(SimChoice choice)
    {
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("DYNAMIC TARGET / THIRD-PARTY STATUS PREVIEW");
        sb.AppendLine(new string('─', 56));
        var rule = _data.DynamicTarget(choice.Id);
        string? target = _pendingTargetChoiceId == choice.Id ? _pendingTargetId : null;
        if (rule is not null)
        {
            var candidates = EligibleTargets(choice);
            sb.AppendLine($"Selection mode: {rule.SelectionMode}; eligible: {candidates.Count}");
            foreach (var c in candidates)
                sb.AppendLine($"  ELIGIBLE · {c.Name} [{c.Id}]");
            // Show why some other characters were rejected without flooding the
            // audit when a project contains hundreds of characters.
            var eligibleIds = candidates.Select(x => x.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excluded = _data.Characters.Where(c =>
                !eligibleIds.Contains(c.Id) &&
                !c.Id.Equals(_actor, StringComparison.OrdinalIgnoreCase) &&
                !c.Id.Equals(_partner, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var c in excluded.Take(12))
            {
                string reason;
                if (rule.SelectionMode == "CUSTOM")
                    reason = "not in authored custom list";
                else
                {
                    string owner = rule.OwnerRef == "INITIATOR" ? _actor : _partner;
                    string current = rule.RelationshipDirection == "OUTGOING"
                        ? CurrentSimulatedStatus(owner, c.Id)
                        : CurrentSimulatedStatus(c.Id, owner);
                    reason = $"status {_data.StatusName(current)}, required {_data.StatusName(rule.RequiredStatusKey)}";
                }
                sb.AppendLine($"  SKIPPED · {c.Name}: {reason}");
            }
            if (excluded.Count > 12) sb.AppendLine($"  ...and {excluded.Count - 12} more skipped characters.");
            sb.AppendLine(target is null ? "Choose a target in the picker to preview effects."
                : $"Selected for this choice: {_data.Name(target)}");
        }
        else if (_selectedTargetId is not null)
            sb.AppendLine($"Bound target from earlier node: {_data.Name(_selectedTargetId)}");
        string? effective = rule is null ? _selectedTargetId : target;
        foreach (var success in new[] { true, false })
        {
            sb.AppendLine(success ? "On SUCCESS:" : "On FAILURE:");
            if (_data.PairStatusEffects(choice.Id, success).Count == 0)
            {
                sb.AppendLine("  No third-party effects.");
                continue;
            }
            try
            {
                var changes = PreviewPairStatusChanges(choice, success, effective);
                if (changes.Count == 0) sb.AppendLine("  No changes (statuses already match).");
                foreach (var c in changes)
                    sb.AppendLine($"  {_data.Name(c.From)} → {_data.Name(c.To)}: " +
                        $"{_data.StatusName(c.Before)} → {_data.StatusName(c.After)}");
            }
            catch (InvalidOperationException ex) when (effective is null &&
                ex.Message.Contains("selected dynamic target", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("  Select a target to resolve these effects.");
            }
        }
        return sb.ToString();
    }
}

public sealed record SimulatedPairStatusChange(string From, string To, string Before, string After);
