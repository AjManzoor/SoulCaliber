using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

public partial class SpecialEncountersWindow : Window
{
    private sealed record OptionValue(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly DialogueDesignerStore _store;
    private SpecialEncounterDefinition? _selected;
    private bool _loading;
    private bool _dirty;

    public SpecialEncountersWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        InteractionBox.ItemsSource = store.Interactions();
        var characters = store.Characters().ToArray();
        RecipientBox.ItemsSource = new[] { new OptionValue("", "Any recipient") }
            .Concat(characters.Select(id => new OptionValue(id, store.CharacterName(id)))).ToArray();
        InitiatorBox.ItemsSource = new[] { new OptionValue("", "Any initiator") }
            .Concat(characters.Select(id => new OptionValue(id, store.CharacterName(id)))).ToArray();
        LaunchBox.ItemsSource = new[]
        {
            new OptionValue("AUTOMATIC", "Automatic when eligible"),
            new OptionValue("OPTIONAL", "Optional conversation")
        };
        RepetitionBox.ItemsSource = new[]
        {
            new OptionValue("ONCE_PER_PAIR", "Once per character pair"),
            new OptionValue("ONCE_PER_OCCURRENCE", "Once per memory occurrence"),
            new OptionValue("REPEATABLE", "Repeatable while eligible")
        };
        AnchorMemoryBox.ItemsSource = new[] { new MemoryTypeDefinition("", "No memory anchor", "", "MINOR", "QUIET", 0, true) }
            .Concat(store.MemoryTypeDefinitions(activeOnly: true)).ToArray();
        AffectedStatusBox.ItemsSource = new[] { new OptionValue("", "No affected relationship requirement") }
            .Concat(store.StatusDefinitions(activeOnly: true)
                .Select(status => new OptionValue(status.Key, status.Name))).ToArray();
        var refs = new[]
        {
            new OptionValue("ANY", "Any"),
            new OptionValue("ACTOR", "Initiator"),
            new OptionValue("PARTNER", "Recipient")
        }.Concat(characters.Select(id => new OptionValue("CHAR:" + id, id))).ToArray();
        AnchorActorBox.ItemsSource = refs;
        AnchorAffectedBox.ItemsSource = refs;
        // "ANY" means the memory must exist, but no character knowledge check is required.
        AnchorKnowerBox.ItemsSource = refs.Select(option =>
            option.Key == "ANY" ? option with { Label = "Knowledge not required" } : option).ToArray();

        foreach (var field in new[] { KeyBox, TitleBox, PriorityBox })
            field.TextChanged += (_, _) => MarkDirty();
        foreach (var field in new[] { InteractionBox, RecipientBox, InitiatorBox, LaunchBox,
            RepetitionBox, AnchorMemoryBox, AnchorActorBox, AnchorAffectedBox, AnchorKnowerBox,
            AffectedStatusBox })
            field.SelectionChanged += (_, _) => MarkDirty();
        ActiveBox.Checked += (_, _) => MarkDirty();
        ActiveBox.Unchecked += (_, _) => MarkDirty();
        Refresh();
    }

    private void MarkDirty()
    {
        if (!_loading) _dirty = true;
    }

    private static void SelectOption(ComboBox box, string key) =>
        box.SelectedItem = box.Items.OfType<OptionValue>()
            .FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    private static string OptionKey(ComboBox box) =>
        (box.SelectedItem as OptionValue)?.Key ?? "";

    private void Refresh(int? selectId = null)
    {
        _loading = true;
        try
        {
            var encounters = _store.SpecialEncounters().ToArray();
            EncounterList.ItemsSource = encounters;
            var selected = encounters.FirstOrDefault(x => x.Id == (selectId ?? _selected?.Id))
                ?? encounters.FirstOrDefault();
            EncounterList.SelectedItem = selected;
            Populate(selected);
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Populate(SpecialEncounterDefinition? item)
    {
        _selected = item;
        KeyBox.IsReadOnly = item is not null;
        KeyBox.Text = item?.Key ?? "";
        TitleBox.Text = item?.Title ?? "";
        InteractionBox.SelectedItem = InteractionBox.Items.OfType<DialogueDesignerStore.Interaction>()
            .FirstOrDefault(x => x.Id == item?.InteractionId);
        SelectOption(RecipientBox, item?.RecipientCharacterId ?? "");
        SelectOption(InitiatorBox, item?.InitiatorCharacterId ?? "");
        SelectOption(LaunchBox, item?.LaunchMode ?? "AUTOMATIC");
        SelectOption(RepetitionBox, item?.RepetitionMode ?? "ONCE_PER_PAIR");
        PriorityBox.Text = (item?.Priority ?? 50).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked = item?.IsActive ?? false;
        AnchorMemoryBox.SelectedItem = AnchorMemoryBox.Items.OfType<MemoryTypeDefinition>()
            .FirstOrDefault(x => x.Key.Equals(item?.AnchorMemoryKey, StringComparison.OrdinalIgnoreCase));
        SelectOption(AnchorActorBox, item?.AnchorActorRef ?? "ANY");
        SelectOption(AnchorAffectedBox, item?.AnchorAffectedRef ?? "ANY");
        SelectOption(AnchorKnowerBox, item?.AnchorKnowerRef ?? "ANY");
        SelectOption(AffectedStatusBox, item?.AffectedRelationshipStatusKey ?? "");
        UpdateAnchorState();
        ConditionsButton.IsEnabled = item is not null;
        ConditionsButton.Content = item is not null && _store.SpecialEncounterHasConditions(item.Id)
            ? "Configure AND / OR / NOT conditions — Configured ✓"
            : "Configure AND / OR / NOT conditions — None";
    }

    private void UpdateAnchorState()
    {
        bool anchored = AnchorMemoryBox.SelectedItem is MemoryTypeDefinition memory &&
            !string.IsNullOrWhiteSpace(memory.Key);
        // Optional memory binding works for all repetition modes; mandatory for per-occurrence.
        AnchorMemoryBox.IsEnabled = true;
        foreach (var combo in new[] { AnchorActorBox, AnchorAffectedBox, AnchorKnowerBox, AffectedStatusBox })
            combo.IsEnabled = anchored;
    }

    private void RepetitionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAnchorState();
    }

    private void AnchorMemoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAnchorState();
    }

    private bool CanChange()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved special encounter changes?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void EncounterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { EncounterList.SelectedItem = _selected; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(EncounterList.SelectedItem as SpecialEncounterDefinition); }
        finally { _loading = false; _dirty = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        _loading = true;
        try
        {
            EncounterList.SelectedItem = null;
            Populate(null);
            StatusText.Text = "Enter a stable key and title; save as inactive draft before adding conditions.";
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!int.TryParse(PriorityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority))
            throw new InvalidOperationException("Priority must be a whole number between 0 and 1000.");
        if (InteractionBox.SelectedItem is not DialogueDesignerStore.Interaction interaction)
            throw new InvalidOperationException("Choose an existing dialogue interaction.");
        string repetition = OptionKey(RepetitionBox);
        string recipient = OptionKey(RecipientBox);
        string initiator = OptionKey(InitiatorBox);
        var data = new SpecialEncounterDefinition(
            _selected?.Id ?? 0, KeyBox.Text, TitleBox.Text,
            interaction.Id, string.IsNullOrEmpty(recipient) ? null : recipient,
            string.IsNullOrEmpty(initiator) ? null : initiator,
            OptionKey(LaunchBox), repetition, priority, ActiveBox.IsChecked == true,
            (AnchorMemoryBox.SelectedItem as MemoryTypeDefinition)?.Key is string memoryKey &&
                !string.IsNullOrWhiteSpace(memoryKey) ? memoryKey : null,
            OptionKey(AnchorActorBox), OptionKey(AnchorAffectedBox),
            OptionKey(AnchorKnowerBox),
            string.IsNullOrWhiteSpace(OptionKey(AffectedStatusBox)) ? null : OptionKey(AffectedStatusBox));
        int id = _store.SaveSpecialEncounter(data);
        Refresh(id);
        StatusText.Text = $"Saved encounter {data.Key.Trim().ToUpperInvariant()}.";
    });

    private void EditConditions_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null)
            throw new InvalidOperationException("Save the encounter draft before configuring conditions.");
        if (_dirty)
            throw new InvalidOperationException("Save or discard changes to the encounter fields before editing conditions.");
        int encounterId = _selected.Id;
        var editor = new MemoryRequirementsWindow(_store,
            () => _store.GetSpecialEncounterConditions(encounterId),
            tree => _store.SaveSpecialEncounterConditions(encounterId, tree),
            $"special encounter {_selected.Key}") { Owner = this };
        editor.ShowDialog();
        Refresh(encounterId);
    });

    private void Delete_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null || !CanChange()) return;
        if (MessageBox.Show(this, $"Delete special encounter {_selected.Key}? The linked dialogue is not deleted.",
            "Delete special encounter", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteSpecialEncounter(_selected.Id);
        _selected = null;
        Refresh();
        StatusText.Text = "Special encounter removed. Its original dialogue remains unchanged.";
    });

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!CanChange()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Special encounters", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
