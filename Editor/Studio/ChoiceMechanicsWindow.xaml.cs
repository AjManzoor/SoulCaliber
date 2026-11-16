using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class ChoiceMechanicsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly Option _option;
    private RelationshipEffectRow? _activeEffect;
    private RelationshipRequirementRow? _activeRequirement;
    private ChanceModifierRow? _activeModifier;
    private bool _loading;
    private bool _dirty;
    private bool _revertingTab;
    private int _previousTab;

    public ChoiceMechanicsWindow(DialogueDesignerStore store, Interaction interaction, Node node, Option option)
    {
        InitializeComponent();
        _store = store;
        _option = option;
        ChoiceHeading.Text = $"{interaction.Title} → {node.Key} → {option.Label}  [{option.Key}]";

        _loading = true;
        try
        {
            EffectOutcomeBox.ItemsSource = new[] { "SUCCESS", "FAILURE" };
            foreach (var combo in new[] { EffectDirectionBox, RequirementDirectionBox, ModifierDirectionBox })
            {
                combo.ItemsSource = Directions.Select(DirectionDisplay).ToList();
            }
            foreach (var combo in new[] { EffectAttributeBox, RequirementAttributeBox, ModifierAttributeBox })
                combo.ItemsSource = _store.RelationshipAttributes;
            SkillActorBox.ItemsSource = _store.SkillKeys;
            SkillDefenderBox.ItemsSource = _store.SkillKeys;
            SkillModeBox.ItemsSource = _store.CheckProfileKeys();
        }
        finally { _loading = false; }

        ReloadEffects();
        ReloadRequirements();
        ReloadModifiers();
        ReloadSkill();
    }

    // Display descriptions without changing the exact string tokens Godot expects.
    private sealed record DirectionChoice(string Key, string Display)
    {
        public override string ToString() => Display;
    }
    private static DirectionChoice DirectionDisplay(string key) => new(key, key == "PARTNER_TO_ACTOR"
        ? "Recipient → Initiator" : "Initiator → Recipient");

    private static string? SelectedDirection(ComboBox combo) =>
        (combo.SelectedItem as DirectionChoice)?.Key;

    private static void SetDirection(ComboBox combo, string direction) =>
        combo.SelectedItem = combo.Items.OfType<DirectionChoice>().FirstOrDefault(d => d.Key == direction);

    private void ReloadEffects(int? selectedId = null)
    {
        _loading = true;
        try
        {
            var rows = _store.RelationshipEffects(_option.Id);
            EffectList.ItemsSource = rows;
            var selected = selectedId.HasValue
                ? rows.FirstOrDefault(x => x.Id == selectedId.Value) : rows.FirstOrDefault();
            EffectList.SelectedItem = selected;
            PopulateEffect(selected);
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void PopulateEffect(RelationshipEffectRow? row)
    {
        _activeEffect = row;
        EffectOutcomeBox.SelectedItem = row?.Outcome ?? "SUCCESS";
        SetDirection(EffectDirectionBox, row?.Direction ?? "PARTNER_TO_ACTOR");
        EffectAttributeBox.SelectedItem = row?.Attribute ?? "TRUST";
        EffectDeltaBox.Text = (row?.Delta ?? 5).ToString(CultureInfo.CurrentCulture);
    }

    private void ReloadRequirements(int? selectedId = null)
    {
        _loading = true;
        try
        {
            var rows = _store.RelationshipRequirements(_option.Id);
            RequirementList.ItemsSource = rows;
            var selected = selectedId.HasValue
                ? rows.FirstOrDefault(x => x.Id == selectedId.Value) : rows.FirstOrDefault();
            RequirementList.SelectedItem = selected;
            PopulateRequirement(selected);
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void PopulateRequirement(RelationshipRequirementRow? row)
    {
        _activeRequirement = row;
        SetDirection(RequirementDirectionBox, row?.Direction ?? "PARTNER_TO_ACTOR");
        RequirementAttributeBox.SelectedItem = row?.Attribute ?? "TRUST";
        RequirementMinBox.Text = row?.Minimum?.ToString(CultureInfo.CurrentCulture) ?? "";
        RequirementMaxBox.Text = row?.Maximum?.ToString(CultureInfo.CurrentCulture) ?? "";
    }

    private void ReloadModifiers(int? selectedId = null)
    {
        _loading = true;
        try
        {
            var rows = _store.ChanceModifiers(_option.Id);
            ModifierList.ItemsSource = rows;
            var selected = selectedId.HasValue
                ? rows.FirstOrDefault(x => x.Id == selectedId.Value) : rows.FirstOrDefault();
            ModifierList.SelectedItem = selected;
            PopulateModifier(selected);
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void PopulateModifier(ChanceModifierRow? row)
    {
        _activeModifier = row;
        SetDirection(ModifierDirectionBox, row?.Direction ?? "PARTNER_TO_ACTOR");
        ModifierAttributeBox.SelectedItem = row?.Attribute ?? "TRUST";
        ModifierFactorBox.Text = (row?.Factor ?? 0.10).ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void ReloadSkill()
    {
        _loading = true;
        try
        {
            var row = _store.GetSkillCheck(_option.Id);
            SkillActorBox.SelectedItem = row?.ActorSkill ?? _store.SkillKeys.FirstOrDefault();
            SkillDefenderBox.SelectedItem = row?.DefenderSkill ?? _store.SkillKeys.LastOrDefault();
            SkillModeBox.SelectedItem = _store.GetOptionProfile(_option.Id);
            SkillActorFactorBox.Text = (row?.ActorFactor ?? 0.5).ToString("0.###", CultureInfo.CurrentCulture);
            SkillDefenderFactorBox.Text = (row?.DefenderFactor ?? 0.5).ToString("0.###", CultureInfo.CurrentCulture);
            RefreshProfileFormula();
            SkillStateText.Text = row is null ? "No skill check configured for this choice (STANDARD profile still applies)."
                : $"Current: {row.ActorSkill} vs {row.DefenderSkill} ({row.CheckMode}).";
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void RefreshProfileFormula()
    {
        if (SkillProfileFormula is null || _store is null) return;
        string key = SkillModeBox?.SelectedItem as string ?? "STANDARD";
        SkillProfileFormula.Text = $"{key}: {_store.CheckProfileFormula(key)}";
    }

    private void SkillMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshProfileFormula();
        Editor_Changed(sender, e);
    }

    private void SaveOptionProfile_Click(object sender, RoutedEventArgs e) => RunMechanicsAction(() =>
    {
        _store.SaveOptionProfile(_option.Id, SkillModeBox.SelectedItem as string ?? "STANDARD");
        ReloadSkill();
    }, "Relationship profile saved for this choice. It applies even without a skill check.");

    private void EditCheckProfiles_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        string chosen = SkillModeBox.SelectedItem as string ?? "STANDARD";
        new CheckProfilesWindow(_store) { Owner = this }.ShowDialog();
        _loading = true;
        try
        {
            SkillModeBox.ItemsSource = _store.CheckProfileKeys();
            SkillModeBox.SelectedItem = _store.CheckProfileKeys().Contains(chosen) ? chosen : "STANDARD";
            RefreshProfileFormula();
        }
        finally { _loading = false; }
    }

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        StatusText.Text = "Unsaved mechanics changes. Save before selecting another record or tab.";
    }

    private bool CanSwitch()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved mechanics edits?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
        _dirty = false;
        return true;
    }

    private void ResetTab(int index)
    {
        _loading = true;
        try
        {
            switch (index)
            {
                case 0: PopulateEffect(_activeEffect); break;
                case 1: ReloadSkill(); break;
                case 2: PopulateRequirement(_activeRequirement); break;
                case 3: PopulateModifier(_activeModifier); break;
            }
        }
        finally { _loading = false; }
    }

    private void MechanicTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _revertingTab || _store is null || e.OriginalSource != MechanicTabs) return;
        int next = MechanicTabs.SelectedIndex;
        if (next == _previousTab) return;
        if (!CanSwitch())
        {
            _revertingTab = true;
            try { MechanicTabs.SelectedIndex = _previousTab; }
            finally { _revertingTab = false; }
            return;
        }
        ResetTab(_previousTab);
        _previousTab = next;
    }

    private void EffectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanSwitch())
        {
            _loading = true;
            try { EffectList.SelectedItem = _activeEffect; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { PopulateEffect(EffectList.SelectedItem as RelationshipEffectRow); }
        finally { _loading = false; }
    }

    private void RequirementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanSwitch())
        {
            _loading = true;
            try { RequirementList.SelectedItem = _activeRequirement; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { PopulateRequirement(RequirementList.SelectedItem as RelationshipRequirementRow); }
        finally { _loading = false; }
    }

    private void ModifierList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanSwitch())
        {
            _loading = true;
            try { ModifierList.SelectedItem = _activeModifier; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { PopulateModifier(ModifierList.SelectedItem as ChanceModifierRow); }
        finally { _loading = false; }
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int result))
            throw new InvalidOperationException($"{name} must be a whole number.");
        return result;
    }

    private static int? ParseNullableInt(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseInt(value, name);

    private static double ParseDouble(string value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double result) &&
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"{name} must be a finite decimal value, such as 0.5 or -0.1.");
        return result;
    }

    private void RunMechanicsAction(Action save, string success)
    {
        try
        {
            save();
            StatusText.Text = success;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save mechanics", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "No change saved; the transaction was rolled back.";
        }
    }

    private bool ConfirmDelete(string label) =>
        MessageBox.Show(this, $"Delete the selected {label}? No automatic backup will be created.",
            "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void NewEffect_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        _loading = true;
        try { EffectList.SelectedItem = null; PopulateEffect(null); }
        finally { _loading = false; }
        StatusText.Text = "New effect: select outcome, direction, attribute and amount, then Save.";
    }

    private void SaveEffect_Click(object sender, RoutedEventArgs e) => RunMechanicsAction(() =>
    {
        int id = _store.SaveRelationshipEffect(new(_activeEffect?.Id ?? 0, _option.Id,
            EffectOutcomeBox.SelectedItem as string ?? "", SelectedDirection(EffectDirectionBox) ?? "",
            EffectAttributeBox.SelectedItem as string ?? "", ParseInt(EffectDeltaBox.Text, "Change amount")));
        ReloadEffects(id);
    }, "Relationship effect saved.");

    private void DeleteEffect_Click(object sender, RoutedEventArgs e)
    {
        if (_activeEffect is null || !CanSwitch() || !ConfirmDelete("relationship effect")) return;
        RunMechanicsAction(() => { _store.DeleteRelationshipEffect(_activeEffect.Id, _option.Id); ReloadEffects(); }, "Relationship effect deleted.");
    }

    private void NewRequirement_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        _loading = true;
        try { RequirementList.SelectedItem = null; PopulateRequirement(null); }
        finally { _loading = false; }
        StatusText.Text = "New requirement: specify a minimum, maximum or both, then Save.";
    }

    private void SaveRequirement_Click(object sender, RoutedEventArgs e) => RunMechanicsAction(() =>
    {
        int id = _store.SaveRelationshipRequirement(new(_activeRequirement?.Id ?? 0, _option.Id,
            SelectedDirection(RequirementDirectionBox) ?? "", RequirementAttributeBox.SelectedItem as string ?? "",
            ParseNullableInt(RequirementMinBox.Text, "Minimum"), ParseNullableInt(RequirementMaxBox.Text, "Maximum")));
        ReloadRequirements(id);
    }, "Relationship requirement saved.");

    private void DeleteRequirement_Click(object sender, RoutedEventArgs e)
    {
        if (_activeRequirement is null || !CanSwitch() || !ConfirmDelete("requirement")) return;
        RunMechanicsAction(() => { _store.DeleteRelationshipRequirement(_activeRequirement.Id, _option.Id); ReloadRequirements(); }, "Requirement deleted.");
    }

    private void NewModifier_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        _loading = true;
        try { ModifierList.SelectedItem = null; PopulateModifier(null); }
        finally { _loading = false; }
        StatusText.Text = "New probability modifier: enter a factor (for example 0.1) and save.";
    }

    private void SaveModifier_Click(object sender, RoutedEventArgs e) => RunMechanicsAction(() =>
    {
        int id = _store.SaveChanceModifier(new(_activeModifier?.Id ?? 0, _option.Id,
            SelectedDirection(ModifierDirectionBox) ?? "", ModifierAttributeBox.SelectedItem as string ?? "",
            ParseDouble(ModifierFactorBox.Text, "Factor")));
        ReloadModifiers(id);
    }, "Chance modifier saved.");

    private void DeleteModifier_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModifier is null || !CanSwitch() || !ConfirmDelete("chance modifier")) return;
        RunMechanicsAction(() => { _store.DeleteChanceModifier(_activeModifier.Id, _option.Id); ReloadModifiers(); }, "Chance modifier deleted.");
    }

    private void SaveSkill_Click(object sender, RoutedEventArgs e) => RunMechanicsAction(() =>
    {
        _store.SaveSkillCheck(new(_option.Id, SkillActorBox.SelectedItem as string ?? "",
            SkillDefenderBox.SelectedItem as string ?? "", SkillModeBox.SelectedItem as string ?? "",
            ParseDouble(SkillActorFactorBox.Text, "Initiator factor"),
            ParseDouble(SkillDefenderFactorBox.Text, "Recipient factor")));
        _store.SaveOptionProfile(_option.Id, SkillModeBox.SelectedItem as string ?? "STANDARD");
        ReloadSkill();
    }, "Skill check saved.");

    private void DeleteSkill_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        if (_store.GetSkillCheck(_option.Id) is null)
        {
            StatusText.Text = "This choice has no skill check to remove.";
            return;
        }
        if (!ConfirmDelete("skill check")) return;
        RunMechanicsAction(() => { _store.DeleteSkillCheck(_option.Id); ReloadSkill(); }, "Skill check removed.");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!CanSwitch()) e.Cancel = true;
    }
}
