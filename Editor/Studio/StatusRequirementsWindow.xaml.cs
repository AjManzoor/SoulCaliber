using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class StatusRequirementsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly int _optionId;
    private DialogueStatusRequirementRow? _active;
    private bool _loading;
    private bool _dirty;

    private sealed record DirectionValue(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    public StatusRequirementsWindow(DialogueDesignerStore store, int optionId)
    {
        InitializeComponent();
        _store = store;
        _optionId = optionId;
        _loading = true;
        DirectionBox.ItemsSource = new[]
        {
            new DirectionValue("ACTOR_TO_PARTNER", "Initiator → Recipient"),
            new DirectionValue("PARTNER_TO_ACTOR", "Recipient → Initiator"),
            new DirectionValue("BOTH", "Both directions (both must match)")
        };
        StatusBox.ItemsSource = _store.StatusDefinitions(activeOnly: true);
        _loading = false;
        Reload();
    }

    private void Reload(int? selectedId = null)
    {
        _loading = true;
        try
        {
            var rows = _store.StatusRequirements(_optionId);
            RequirementList.ItemsSource = rows;
            RequirementList.SelectedItem = rows.FirstOrDefault(x => x.Id == selectedId);
            Populate(RequirementList.SelectedItem as DialogueStatusRequirementRow);
            _dirty = false;
        }
        finally { _loading = false; }
    }

    private void Populate(DialogueStatusRequirementRow? row)
    {
        _active = row;
        DirectionBox.SelectedItem = DirectionBox.Items.OfType<DirectionValue>()
            .FirstOrDefault(x => x.Key == (row?.Direction ?? "PARTNER_TO_ACTOR"));
        StatusBox.SelectedValue = row?.StatusKey ?? "ALLY";
    }

    private bool MayDiscard()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this, "Discard unsaved status-requirement edits?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _dirty = false;
        return true;
    }

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        StatusMessage.Text = "Unsaved changes. Save before switching or closing.";
    }

    private void RequirementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (!MayDiscard())
        {
            _loading = true;
            try { RequirementList.SelectedItem = _active; }
            finally { _loading = false; }
            return;
        }
        _loading = true;
        try { Populate(RequirementList.SelectedItem as DialogueStatusRequirementRow); }
        finally { _loading = false; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!MayDiscard()) return;
        _loading = true;
        try { RequirementList.SelectedItem = null; Populate(null); }
        finally { _loading = false; }
        StatusMessage.Text = "New requirement: select direction and required status, then save.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string direction = (DirectionBox.SelectedItem as DirectionValue)?.Key ?? "";
            int id = _store.SaveStatusRequirement(new DialogueStatusRequirementRow(
                _active?.Id ?? 0, _optionId, direction, StatusBox.SelectedValue as string ?? ""));
            Reload(id);
            StatusMessage.Text = "Status requirement saved. No automatic backup created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save status requirement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_active is null || !MayDiscard()) return;
        if (MessageBox.Show(this, "Delete this status requirement?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _store.DeleteStatusRequirement(_optionId, _active.Id);
            Reload();
            StatusMessage.Text = "Requirement deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete status requirement", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && MessageBox.Show(this,
            "Your unsaved edits in this simple requirement window will NOT be included in the advanced conditions. Continue?",
            "Unsaved simple requirement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            new MemoryRequirementsWindow(_store, _optionId) { Owner = this }.ShowDialog();
            StatusMessage.Text = "Advanced conditions updated. Simple status requirements also remain in effect (AND).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Advanced requirements", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!MayDiscard()) e.Cancel = true; }
}
