using System.Windows;
using System.Windows.Controls;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class ReactionTypesWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private ReactionType? _selected;
    private bool _loading;
    private bool _dirty;

    public ReactionTypesWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        foreach (var box in new[] { KeyBox, NameBox, DescriptionBox, OrderBox })
            box.TextChanged += (_, _) => { if (!_loading) _dirty = true; };
        Refresh();
    }

    private void Refresh(string? key = null)
    {
        _loading = true;
        try
        {
            var items = _store.ReactionTypes();
            ReactionList.ItemsSource = items;
            var selected = items.FirstOrDefault(x => x.Key == (key ?? _selected?.Key)) ?? items.FirstOrDefault();
            ReactionList.SelectedItem = selected;
            Populate(selected);
        }
        finally { _loading = false; _dirty = false; }
    }

    private void Populate(ReactionType? item)
    {
        _selected = item;
        KeyBox.IsReadOnly = item is not null;
        KeyBox.Text = item?.Key ?? "";
        NameBox.Text = item?.DisplayName ?? "";
        DescriptionBox.Text = item?.Description ?? "";
        OrderBox.Text = (item?.SortOrder ?? 100).ToString();
        if (item is null) UsageText.Text = "New global reaction type";
        else
        {
            var usage = _store.ReactionTypeUsage(item.Key);
            UsageText.Text = $"Used by {usage.OptionCount} choice(s) and {usage.PortraitCount} portrait mapping(s).";
        }
    }

    private bool CanChange()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved reaction type edits?", "Unsaved edits",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void ReactionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanChange())
        {
            _loading = true;
            try { ReactionList.SelectedItem = _selected; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(ReactionList.SelectedItem as ReactionType); }
        finally { _loading = false; _dirty = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChange()) return;
        _loading = true;
        try { ReactionList.SelectedItem = null; Populate(null); StatusText.Text = "Enter a new permanent key and click Save."; }
        finally { _loading = false; _dirty = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(OrderBox.Text, out int order)) throw new InvalidOperationException("Sort order must be an integer.");
            string key = KeyBox.Text.Trim().ToUpperInvariant();
            _store.SaveReactionType(new ReactionType(key, NameBox.Text, DescriptionBox.Text, order), _selected?.Key);
            Refresh(key);
            StatusText.Text = $"Reaction type {key} saved.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot save reaction type", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !CanChange()) return;
        if (MessageBox.Show(this, $"Delete reaction type {_selected.Key}? No automatic backup will be created.",
            "Delete reaction type", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _store.DeleteReactionType(_selected.Key);
            _selected = null;
            Refresh();
            StatusText.Text = "Reaction type deleted.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot delete reaction type", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!CanChange()) e.Cancel = true;
        base.OnClosing(e);
    }
}
