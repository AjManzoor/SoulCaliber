using System.Windows;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private void LoadReactionTypeChoices()
    {
        if (_designer is null) return;
        var keys = _designer.ReactionTypes().Select(x => x.Key).ToArray();
        string? success = OptionSuccessReactionBox.SelectedItem as string;
        string? failure = OptionFailureReactionBox.SelectedItem as string;
        string? art = ReactionKeyBox.SelectedItem as string;
        bool wasLoading = _designerLoading;
        bool wasCharacterLoading = _characterLoading;
        _designerLoading = _characterLoading = true;
        try
        {
            OptionSuccessReactionBox.ItemsSource = keys;
            OptionFailureReactionBox.ItemsSource = keys;
            ReactionKeyBox.ItemsSource = keys;
            OptionSuccessReactionBox.SelectedItem = success ?? "RECEPTIVE";
            OptionFailureReactionBox.SelectedItem = failure ?? "DISMISSIVE";
            ReactionKeyBox.SelectedItem = art;
        }
        finally { _designerLoading = wasLoading; _characterLoading = wasCharacterLoading; }
    }

    private void ManageReactionTypes_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null) return;
        if (_designerDirty || _characterDirty || _reactionDirty)
        {
            MessageBox.Show(this, "Save your unsaved dialogue or character edits before editing reaction types.",
                "Unsaved edits", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var editor = new ReactionTypesWindow(_designer) { Owner = this };
        editor.ShowDialog();
        LoadReactionTypeChoices();
        // Ensure selection uses the latest database values.
        bool wasLoading = _designerLoading;
        _designerLoading = true;
        try { PopulateOption(_activeOption); }
        finally { _designerLoading = wasLoading; }
    }
}
