using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class IntroOverridesWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly Node _node;
    private Introduction? _active;
    private bool _loading;
    private bool _dirty;

    public IntroOverridesWindow(DialogueDesignerStore store, Node node, string? defaultHostId)
    {
        InitializeComponent();
        _store = store;
        _node = node;
        HeadingText.Text = $"Introductions — {node.Key} (Round {node.Round})";
        var characters = store.Characters();
        IntroSpeakerBox.ItemsSource = characters;
        IntroVisitorBox.ItemsSource = new[] { "" }.Concat(characters).ToArray();
        PreviewSpeakerBox.ItemsSource = characters;
        PreviewVisitorBox.ItemsSource = characters;
        PreviewSpeakerBox.SelectedItem = characters.Contains(defaultHostId ?? "") ? defaultHostId : characters.FirstOrDefault();
        PreviewVisitorBox.SelectedItem = characters.FirstOrDefault(x => x != PreviewSpeakerBox.SelectedItem as string);
        Reload();
    }

    private bool CanSwitch()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved introduction edits?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void Reload(int? id = null)
    {
        _loading = true;
        try
        {
            var items = _store.Introductions(_node.Id);
            IntroductionList.ItemsSource = items;
            var selected = id is null ? items.FirstOrDefault() : items.FirstOrDefault(x => x.Id == id);
            IntroductionList.SelectedItem = selected;
            Populate(selected);
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void Populate(Introduction? item)
    {
        _active = item;
        IntroSpeakerBox.IsEnabled = item is null;
        IntroVisitorBox.IsEnabled = item is null;
        IntroSpeakerBox.SelectedItem = item?.CharacterId;
        IntroVisitorBox.SelectedItem = item?.VisitorId ?? "";
        IntroTextBox.Text = item?.Text ?? "";
        StatusText.Text = item is null ? "New entry: select the speaking character and save." :
            "Editing text only. Use New introduction for another character or pairing.";
    }

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        StatusText.Text = "Unsaved introduction changes.";
    }

    private void IntroductionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var next = IntroductionList.SelectedItem as Introduction;
        if (!CanSwitch())
        {
            _loading = true;
            try { IntroductionList.SelectedItem = _active; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(next); }
        finally { _loading = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSwitch()) return;
        _loading = true;
        try
        {
            IntroductionList.SelectedItem = null;
            Populate(null);
        }
        finally { _loading = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int id = _store.SaveIntroduction(new Introduction(_active?.Id ?? 0, _node.Id,
                IntroSpeakerBox.SelectedItem as string ?? "", IntroVisitorBox.SelectedItem as string,
                IntroTextBox.Text));
            Reload(id);
            StatusText.Text = "Saved to master database. A backup was created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save introduction", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null || !CanSwitch()) return;
        if (MessageBox.Show(this, "Delete this introduction override? The generic node introduction will remain.",
                "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _store.DeleteIntroduction(_active.Id);
            Reload();
            StatusText.Text = "Introduction deleted; a database backup was created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to delete introduction", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewSpeakerBox.SelectedItem is not string speaker ||
            PreviewVisitorBox.SelectedItem is not string visitor)
        {
            MessageBox.Show(this, "Select both characters to preview their introduction.", "Preview");
            return;
        }
        if (speaker == visitor)
        {
            MessageBox.Show(this, "Select two different characters.", "Preview");
            return;
        }
        try
        {
            var value = _store.ResolveIntroduction(_node.Id, speaker, visitor);
            string text = value.Text.Replace("{actor}", _store.CharacterName(visitor), StringComparison.OrdinalIgnoreCase)
                .Replace("{partner}", _store.CharacterName(speaker), StringComparison.OrdinalIgnoreCase);
            PreviewSourceText.Text = $"Source: {value.Source}";
            PreviewResultText.Text = text;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Preview failed"); }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!CanSwitch()) e.Cancel = true;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
