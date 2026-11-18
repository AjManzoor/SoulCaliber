using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

/// <summary>
/// Interaction-first editor for the existing ConversationAvailability table.
/// Multiple availability rules may reference the same authored dialogue.
/// </summary>
public partial class ConversationAvailabilityWindow : Window
{
    private sealed record OptionValue(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly DialogueDesignerStore _store;
    private ConversationAvailabilityDefinition? _selected;
    private DialogueDesignerStore.Interaction? _dialogue;
    private bool _loading;
    private bool _dirty;

    public ConversationAvailabilityWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        var characters = store.Characters().ToArray();
        RecipientBox.ItemsSource = new[] { new OptionValue("", "Any recipient") }
            .Concat(characters.Select(id => new OptionValue(id, store.CharacterName(id)))).ToArray();
        InitiatorBox.ItemsSource = new[] { new OptionValue("", "Any initiator") }
            .Concat(characters.Select(id => new OptionValue(id, store.CharacterName(id)))).ToArray();

        TitleBox.TextChanged += (_, _) => MarkDirty();
        OrderBox.TextChanged += (_, _) => MarkDirty();
        RecipientBox.SelectionChanged += (_, _) => MarkDirty();
        InitiatorBox.SelectionChanged += (_, _) => MarkDirty();
        ActiveBox.Checked += (_, _) => MarkDirty();
        ActiveBox.Unchecked += (_, _) => MarkDirty();

        _loading = true;
        try
        {
            DialogueList.ItemsSource = store.Interactions();
            DialogueList.SelectedIndex = DialogueList.Items.Count > 0 ? 0 : -1;
            _dialogue = DialogueList.SelectedItem as DialogueDesignerStore.Interaction;
            RefreshRules();
        }
        finally { _loading = false; _dirty = false; }
    }

    private void MarkDirty() { if (!_loading) _dirty = true; }

    private static void SelectOption(ComboBox box, string key) =>
        box.SelectedItem = box.Items.OfType<OptionValue>()
            .FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));

    private static string OptionKey(ComboBox box) =>
        (box.SelectedItem as OptionValue)?.Key ?? "";

    private void RefreshRules(int? selectId = null)
    {
        bool oldLoading = _loading;
        _loading = true;
        try
        {
            var entries = _dialogue is null
                ? Array.Empty<ConversationAvailabilityDefinition>()
                : _store.ConversationAvailability()
                    .Where(x => x.InteractionId == _dialogue.Id).ToArray();
            TopicList.ItemsSource = entries;
            var selected = entries.FirstOrDefault(x => x.Id == (selectId ?? _selected?.Id))
                ?? entries.FirstOrDefault();
            TopicList.SelectedItem = selected;
            Populate(selected);
        }
        finally { _loading = oldLoading; _dirty = false; }
    }

    private void Populate(ConversationAvailabilityDefinition? item)
    {
        _selected = item;
        KeyBox.Text = item?.Key ?? "(generated when saved)";
        TitleBox.Text = item?.Title ?? _dialogue?.Title ?? "";
        LinkedDialogueText.Text = _dialogue is null
            ? "Select a dialogue interaction on the left."
            : $"{_dialogue.Title}  [{_dialogue.Key}]";
        SelectOption(RecipientBox, item?.RecipientCharacterId ?? "");
        SelectOption(InitiatorBox, item?.InitiatorCharacterId ?? "");
        OrderBox.Text = (item?.DisplayOrder ?? 100).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked = item?.IsActive ?? false;
        ConditionsButton.IsEnabled = item is not null;
        ConditionsButton.Content = item is not null && _store.ConversationAvailabilityHasConditions(item.Id)
            ? "Configure AND / OR / NOT conditions — Configured ✓"
            : "Configure AND / OR / NOT conditions — None";
    }

    private bool CanChange()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved availability-rule changes?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
        _dirty = false;
        return true;
    }

    private void DialogueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { DialogueList.SelectedItem = _dialogue; }
            finally { _loading = false; }
            return;
        }
        _dialogue = DialogueList.SelectedItem as DialogueDesignerStore.Interaction;
        _selected = null;
        RefreshRules();
        StatusText.Text = _dialogue is null
            ? "Select a dialogue to configure its availability."
            : $"Editing availability for {_dialogue.Title}. Existing dialogue content is unchanged.";
    }

    private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { TopicList.SelectedItem = _selected; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(TopicList.SelectedItem as ConversationAvailabilityDefinition); }
        finally { _loading = false; _dirty = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogue is null)
        {
            MessageBox.Show(this, "Select an existing dialogue first.", "Conversation availability");
            return;
        }
        if (!CanChange()) return;
        _loading = true;
        try
        {
            TopicList.SelectedItem = null;
            Populate(null);
            StatusText.Text = "New rule: adjust characters, title or order, then save. The rule starts inactive.";
        }
        finally { _loading = false; _dirty = false; }
    }

    private string GenerateKey(DialogueDesignerStore.Interaction dialogue)
    {
        string stem = Regex.Replace(dialogue.Key.ToUpperInvariant(), "[^A-Z0-9_]", "_").Trim('_');
        if (stem.Length == 0 || stem[0] is < 'A' or > 'Z') stem = "TOPIC_" + stem;
        stem = stem[..Math.Min(40, stem.Length)];
        var taken = _store.ConversationAvailability().Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(stem)) return stem;
        for (int i = 2; i < 1000000; i++)
        {
            string suffix = "_" + i.ToString(CultureInfo.InvariantCulture);
            string candidate = stem[..Math.Min(stem.Length, 48 - suffix.Length)] + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Could not generate a unique availability-rule key.");
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_dialogue is null) throw new InvalidOperationException("Select a dialogue first.");
        if (!int.TryParse(OrderBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
            throw new InvalidOperationException("Display order must be a whole number (0–10000).");
        string recipient = OptionKey(RecipientBox);
        string initiator = OptionKey(InitiatorBox);
        var item = new ConversationAvailabilityDefinition(
            _selected?.Id ?? 0, _selected?.Key ?? GenerateKey(_dialogue), TitleBox.Text, _dialogue.Id,
            string.IsNullOrEmpty(recipient) ? null : recipient,
            string.IsNullOrEmpty(initiator) ? null : initiator,
            order, ActiveBox.IsChecked == true);
        int id = _store.SaveConversationAvailability(item);
        RefreshRules(id);
        StatusText.Text = $"Saved availability for {_dialogue.Title}.";
    });

    private void EditConditions_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null) throw new InvalidOperationException("Save the availability rule first.");
        if (_dirty) throw new InvalidOperationException("Save or discard changes before editing conditions.");
        int id = _selected.Id;
        var editor = new MemoryRequirementsWindow(_store,
            () => _store.GetConversationAvailabilityConditions(id),
            tree => _store.SaveConversationAvailabilityConditions(id, tree),
            $"normal conversation {_selected.Key}") { Owner = this };
        editor.ShowDialog();
        RefreshRules(id);
    });

    private void Delete_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null || !CanChange()) return;
        if (MessageBox.Show(this,
            $"Delete availability rule {_selected.Key}? The dialogue will remain.",
            "Delete availability rule", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteConversationAvailability(_selected.Id);
        _selected = null;
        RefreshRules();
        StatusText.Text = "Availability rule deleted. The original dialogue was not changed.";
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
            MessageBox.Show(this, ex.Message, "Conversation availability", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
