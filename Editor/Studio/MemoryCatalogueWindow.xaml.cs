using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoulCaliberStudio;

public partial class MemoryCatalogueWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly Dictionary<string, CheckBox> _traits = new(StringComparer.OrdinalIgnoreCase);
    private MemoryTypeDefinition? _selected;
    private bool _loading;
    private bool _dirty;

    public MemoryCatalogueWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        SeverityBox.Items.Add(new ComboBoxItem { Content = "Major", Tag = "MAJOR" });
        SeverityBox.Items.Add(new ComboBoxItem { Content = "Minor", Tag = "MINOR" });
        KnowledgeBox.Items.Add(new ComboBoxItem { Content = "Quiet — player and affected characters", Tag = "QUIET" });
        KnowledgeBox.Items.Add(new ComboBoxItem { Content = "Allies — both sides' allies", Tag = "ALLIES" });
        KnowledgeBox.Items.Add(new ComboBoxItem { Content = "Global — everyone", Tag = "GLOBAL" });

        foreach (var field in new[] { KeyBox, NameBox, DescriptionBox, SortBox })
            field.TextChanged += (_, _) => MarkDirty();
        SeverityBox.SelectionChanged += (_, _) => MarkDirty();
        KnowledgeBox.SelectionChanged += (_, _) => MarkDirty();
        ActiveBox.Checked += (_, _) => MarkDirty();
        ActiveBox.Unchecked += (_, _) => MarkDirty();
        Refresh();
    }

    private void MarkDirty()
    {
        if (!_loading) _dirty = true;
    }

    private void Refresh(string? selectKey = null)
    {
        _loading = true;
        try
        {
            var definitions = _store.MemoryTypeDefinitions().ToArray();
            MemoryList.ItemsSource = definitions;
            var selected = definitions.FirstOrDefault(x => x.Key.Equals(
                selectKey ?? _selected?.Key, StringComparison.OrdinalIgnoreCase)) ?? definitions.FirstOrDefault();
            MemoryList.SelectedItem = selected;
            Populate(selected);
        }
        finally { _loading = false; _dirty = false; }
    }

    private static void SetCombo(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(
            x => string.Equals(x.Tag as string, value, StringComparison.Ordinal));
    }

    private void Populate(MemoryTypeDefinition? item)
    {
        _selected = item;
        KeyBox.IsReadOnly = item is not null;
        KeyBox.Text = item?.Key ?? "";
        NameBox.Text = item?.Name ?? "";
        DescriptionBox.Text = item?.Description ?? "";
        SortBox.Text = (item?.SortOrder ?? 100).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked = item?.IsActive ?? true;
        SetCombo(SeverityBox, item?.DefaultSeverity ?? "MINOR");
        SetCombo(KnowledgeBox, item?.DefaultKnowledge ?? "QUIET");
        PopulateTraits(item?.Key);
    }

    private void PopulateTraits(string? memoryKey)
    {
        var linked = (memoryKey is null ? Array.Empty<string>() : _store.MemoryTypeRelevantTraits(memoryKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _traits.Clear();
        TraitPanel.Children.Clear();
        var definitions = _store.TraitDefinitions();
        if (definitions.Count == 0)
        {
            TraitPanel.Children.Add(new TextBlock
            {
                Text = "No personality traits defined yet. You can still save a memory type.",
                Foreground = Brushes.LightSteelBlue,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        foreach (var trait in definitions)
        {
            bool wasLinked = linked.Contains(trait.Key);
            var check = new CheckBox
            {
                Content = trait.Name + (trait.IsActive ? "" : " (inactive)"),
                ToolTip = $"{trait.Key}: {trait.Description}",
                IsChecked = wasLinked,
                IsEnabled = trait.IsActive || wasLinked,
                Foreground = trait.IsActive ? Brushes.White : Brushes.LightSteelBlue,
                Margin = new Thickness(0, 4, 20, 9),
                MinWidth = 150
            };
            check.Checked += (_, _) => MarkDirty();
            check.Unchecked += (_, _) => MarkDirty();
            _traits[trait.Key] = check;
            TraitPanel.Children.Add(check);
        }
    }

    private bool CanChange()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved memory catalogue changes?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void MemoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { MemoryList.SelectedItem = _selected; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(MemoryList.SelectedItem as MemoryTypeDefinition); }
        finally { _loading = false; _dirty = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        _loading = true;
        try
        {
            MemoryList.SelectedItem = null;
            Populate(null);
            StatusText.Text = "Enter a new permanent memory key, then save.";
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!int.TryParse(SortBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sortOrder))
            throw new InvalidOperationException("Sort order must be an integer.");
        string severity = (SeverityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        string knowledge = (KnowledgeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var definition = new MemoryTypeDefinition(KeyBox.Text, NameBox.Text, DescriptionBox.Text,
            severity, knowledge, sortOrder, ActiveBox.IsChecked == true);
        string[] traits = _traits.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        _store.SaveMemoryType(definition, _selected is null, traits);
        Refresh(definition.Key.Trim().ToUpperInvariant());
        StatusText.Text = $"Saved memory type {definition.Key.Trim().ToUpperInvariant()}.";
    });

    private void Delete_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null || !CanChange()) return;
        if (MessageBox.Show(this, $"Delete memory type {_selected.Key} and its trait relevance links?",
            "Delete memory type", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteMemoryType(_selected.Key);
        _selected = null;
        Refresh();
        StatusText.Text = "Memory type deleted.";
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
            MessageBox.Show(this, ex.Message, "Memory catalogue", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
