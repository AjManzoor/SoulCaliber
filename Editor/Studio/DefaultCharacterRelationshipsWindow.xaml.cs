using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

/// <summary>Master-content editor for all directional pair defaults.</summary>
public partial class DefaultCharacterRelationshipsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly ObservableCollection<StartingRelationshipRow> _rows = new();
    private StatusCharacter? _selectedCharacter;
    private bool _loading;
    private bool _synchronising;
    private bool _dirty;

    // Public for the DataGrid cell templates: no view-model or database coupling.
    public IReadOnlyList<RelationshipStatusDefinition> AvailableStatuses { get; }

    public DefaultCharacterRelationshipsWindow(DialogueDesignerStore store, string? initialCharacterId = null)
    {
        _store = store;
        AvailableStatuses = store.StatusDefinitions(activeOnly: true);
        if (!AvailableStatuses.Any(s => s.Key.Equals("NEUTRAL", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The active NEUTRAL fallback is missing from the relationship catalogue.");
        _loading = true;
        InitializeComponent();
        PairGrid.ItemsSource = _rows;
        var roster = store.StatusCharacters();
        SelectedCharacterBox.ItemsSource = roster;
        SelectedCharacterBox.SelectedItem = roster.FirstOrDefault(c =>
            string.Equals(c.Id, initialCharacterId, StringComparison.OrdinalIgnoreCase)) ?? roster.FirstOrDefault();
        _loading = false;
        LoadSelected();
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            foreach (var row in _rows) row.PropertyChanged -= Row_PropertyChanged;
            _rows.Clear();
            _selectedCharacter = SelectedCharacterBox.SelectedItem as StatusCharacter;
            if (_selectedCharacter is not null)
            {
                foreach (var pair in _store.StartingStatusPairs(_selectedCharacter.Id))
                {
                    var row = new StartingRelationshipRow(pair);
                    row.PropertyChanged += Row_PropertyChanged;
                    _rows.Add(row);
                }
                StatusText.Text = $"{_rows.Count} character pair(s) — changes not saved until you click Save.";
            }
            else StatusText.Text = "Add characters before assigning their starting statuses.";
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || sender is not StartingRelationshipRow row) return;
        if (!_synchronising && MirrorChangesBox.IsChecked == true)
        {
            _synchronising = true;
            try
            {
                if (e.PropertyName == nameof(StartingRelationshipRow.OutgoingKey))
                    row.IncomingKey = row.OutgoingKey;
                else if (e.PropertyName == nameof(StartingRelationshipRow.IncomingKey))
                    row.OutgoingKey = row.IncomingKey;
            }
            finally { _synchronising = false; }
        }
        _dirty = true;
        StatusText.Text = "Unsaved changes — click Save starting relationships.";
    }

    private void SetBoth_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StartingRelationshipRow row })
            row.IncomingKey = row.OutgoingKey;
    }

    private void SelectedCharacter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!CanDiscard())
        {
            _loading = true;
            try { SelectedCharacterBox.SelectedItem = _selectedCharacter; }
            finally { _loading = false; }
            return;
        }
        try { LoadSelected(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private bool CanDiscard() => !_dirty || MessageBox.Show(this,
        "Discard unsaved starting relationship changes?", "Unsaved relationships",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null) return;
        try
        {
            PairGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            PairGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _store.SaveStartingStatusPairs(_selectedCharacter.Id,
                _rows.Select(r => new CharacterStartingStatusPair(
                    r.OtherId, r.OtherName, r.OutgoingKey, r.IncomingKey)).ToList());
            _dirty = false;
            StatusText.Text = $"Saved starting statuses for {_selectedCharacter.Name} in both directions. Gameplay data unchanged.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!CanDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message,
        "Starting relationships", MessageBoxButton.OK, MessageBoxImage.Error);
}

public sealed class StartingRelationshipRow : INotifyPropertyChanged
{
    private string _outgoingKey;
    private string _incomingKey;
    public string OtherId { get; }
    public string OtherName { get; }
    public string DisplayName => $"{OtherName}  [{OtherId}]";
    public event PropertyChangedEventHandler? PropertyChanged;

    public StartingRelationshipRow(CharacterStartingStatusPair pair)
    {
        OtherId = pair.OtherCharacterId;
        OtherName = pair.OtherCharacterName;
        _outgoingKey = pair.OutgoingStatusKey;
        _incomingKey = pair.IncomingStatusKey;
    }

    public string OutgoingKey
    {
        get => _outgoingKey;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(_outgoingKey, value, StringComparison.OrdinalIgnoreCase)) return;
            _outgoingKey = value;
            PropertyChanged?.Invoke(this, new(nameof(OutgoingKey)));
        }
    }
    public string IncomingKey
    {
        get => _incomingKey;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(_incomingKey, value, StringComparison.OrdinalIgnoreCase)) return;
            _incomingKey = value;
            PropertyChanged?.Invoke(this, new(nameof(IncomingKey)));
        }
    }
}
