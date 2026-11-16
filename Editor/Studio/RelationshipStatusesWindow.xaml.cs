using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

public partial class RelationshipStatusesWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private RelationshipStatusDefinition? _selected;
    private bool _loading;
    private bool _dirty;

    public RelationshipStatusesWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        foreach (var field in new[] { KeyBox, NameBox, DescriptionBox, OrderBox, IconBox, ColorBox })
            field.TextChanged += (_, _) => { if (!_loading) _dirty = true; };
        ColorBox.TextChanged += (_, _) => UpdateColourPreview();
        ActiveBox.Checked += (_, _) => { if (!_loading) _dirty = true; };
        ActiveBox.Unchecked += (_, _) => { if (!_loading) _dirty = true; };
        Refresh();
    }

    private void Refresh(string? selectKey = null)
    {
        _loading = true;
        try
        {
            var items = _store.StatusDefinitions().ToArray();
            StatusList.ItemsSource = items;
            var selected = items.FirstOrDefault(x => x.Key == (selectKey ?? _selected?.Key)) ?? items.FirstOrDefault();
            StatusList.SelectedItem = selected;
            Populate(selected);
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Populate(RelationshipStatusDefinition? item)
    {
        _selected = item;
        KeyBox.IsReadOnly = item is not null;
        KeyBox.Text = item?.Key ?? "";
        NameBox.Text = item?.Name ?? "";
        DescriptionBox.Text = item?.Description ?? "";
        IconBox.Text = item?.IconPath ?? "";
        ColorBox.Text = item?.Color ?? "#D4B8F5";
        UpdateColourPreview();
        OrderBox.Text = (item?.SortOrder ?? 100).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked = item?.IsActive ?? true;
        ActiveBox.IsEnabled = item?.IsDefault != true;
        DefaultDescription.Text = item?.IsDefault == true
            ? "This is the permanent NEUTRAL fallback. It cannot be disabled or deleted."
            : "Not the fallback. Existing status keys are immutable; only new entries may receive new keys.";
    }

    private bool CanChange()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved status changes?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void StatusList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { StatusList.SelectedItem = _selected; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(StatusList.SelectedItem as RelationshipStatusDefinition); }
        finally { _loading = false; _dirty = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        _loading = true;
        try
        {
            StatusList.SelectedItem = null;
            Populate(null);
            StatusText.Text = "Enter a permanent new status key, then save.";
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!int.TryParse(OrderBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
            throw new InvalidOperationException("Sort order must be an integer.");
        var item = new RelationshipStatusDefinition(KeyBox.Text.Trim().ToUpperInvariant(), NameBox.Text,
            DescriptionBox.Text, order, ActiveBox.IsChecked == true, _selected?.IsDefault == true,
            IconBox.Text, ColorBox.Text);
        _store.SaveStatus(item, _selected is null);
        Refresh(item.Key);
        StatusText.Text = $"Saved relationship status {item.Key}.";
    });

    private void ApplyIconPreset(string iconPath, string hexColour)
    {
        IconBox.Text = iconPath;
        ColorBox.Text = hexColour;
    }

    private void AllyIcon_Click(object sender, RoutedEventArgs e) =>
        ApplyIconPreset("res://Assets/StatusIcons/ally.svg", "#8AE6A8");
    private void NeutralIcon_Click(object sender, RoutedEventArgs e) =>
        ApplyIconPreset("res://Assets/StatusIcons/neutral.svg", "#BDB8CC");
    private void EnemyIcon_Click(object sender, RoutedEventArgs e) =>
        ApplyIconPreset("res://Assets/StatusIcons/enemy.svg", "#FF9994");

    private void UpdateColourPreview()
    {
        string hex = (ColorBox.Text ?? "").Trim();
        if (Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$"))
        {
            ColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            ColorPreviewText.Text = "Preview";
        }
        else
        {
            ColorPreview.Background = Brushes.Transparent;
            ColorPreviewText.Text = "Use #RRGGBB";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null || !CanChange()) return;
        if (_selected.IsDefault) throw new InvalidOperationException("The NEUTRAL default cannot be deleted.");
        if (MessageBox.Show(this, $"Delete relationship status {_selected.Key}? This cannot be undone.",
            "Delete relationship status", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteStatus(_selected.Key);
        _selected = null;
        Refresh();
        StatusText.Text = "Status deleted.";
    });

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!CanChange()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Relationship statuses", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
