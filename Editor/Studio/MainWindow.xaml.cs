using Microsoft.Win32;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SoulCaliberStudio;

public partial class MainWindow : Window
{
    private DatabaseEditor? _editor;
    private DataTable? _table;
    private List<ColumnInfo> _columns = [];
    private bool _changingTable;
    private string? _currentTableName;

    public MainWindow()
    {
        InitializeComponent();
        WireCharacterEditorEvents();
        TableSelector.ItemsSource = DatabaseEditor.Tables;
        HintText.Text = "Open the EXISTING soulcaliber_master.db, not soulcaliber_savegame.db. " +
                        "Select a table; edit cells and press Save. A backup is made before each saved change.";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) { }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!CanLeaveTable() || !DesignerCanSwitch() || !CharactersCanSwitch()) { e.Cancel = true; return; }
        _designer?.Dispose();
        _characterStore?.Dispose();
        _editor?.Dispose();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!CanLeaveTable() || !DesignerCanSwitch() || !CharactersCanSwitch()) return;
        var picker = new OpenFileDialog
        {
            Title = "Select soulcaliber_master.db",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        DatabaseEditor? next = null;
        try
        {
            next = new DatabaseEditor(picker.FileName);
            next.Open();
            _designer?.Dispose();
            _editor?.Dispose();
            _editor = next;
            _table = null;
            _currentTableName = null;
            PathText.Text = picker.FileName;
            _changingTable = true;
            TableSelector.SelectedIndex = 0;
            _changingTable = false;
            LoadTable(DatabaseEditor.Tables[0]);
            InitDesigner(picker.FileName);
            InitCharacters(picker.FileName);
            InitPublishing(picker.FileName);
            InitTesting(picker.FileName);
            StatusText.Text = "Master content database opened. Edits are not committed until Save changes.";
        }
        catch (Exception ex)
        {
            next?.Dispose();
            MessageBox.Show(this, ex.Message, "Unable to open database", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingTable || _editor == null || TableSelector.SelectedItem is not string name) return;
        if (!CanLeaveTable())
        {
            _changingTable = true;
            TableSelector.SelectedItem = _currentTableName;
            _changingTable = false;
            return;
        }
        LoadTable(name);
    }

    private void LoadTable(string name)
    {
        if (_editor == null) return;
        try
        {
            _columns = _editor.Columns(name);
            _table = _editor.Load(name);
            _currentTableName = name;
            ContentGrid.ItemsSource = _table.DefaultView;
            SearchBox.Clear();
            HintText.Text = name switch
            {
                "Interactions" => "Create interaction/confrontation types here. Key is the stable interaction identifier. StartNodeKey must match a node Key before playtesting.",
                "Nodes" => "Dialogue rounds belong to an InteractionId. Use the Interactions table to find the parent Id.",
                "Options" => "Choices belong to a NodeId. DefaultPlayerLine, DefaultHostSuccessLine and DefaultHostFailureLine are your generic dialogue phrases.",
                "Lines" => "Character-specific phrases. OptionId links a choice; CharacterId is required; VisitorId and Outcome may be blank for generic matching.",
                "Characters" => "Stable character IDs are referenced throughout the project. Avoid changing an existing Id.",
                "ReactionImages" => "Reaction image paths are Godot resource paths, e.g. res://Images/...",
                _ => "Edit records directly. Primary keys are locked for existing rows; related IDs must point to real records."
            };
            StatusText.Text = $"{name}: {_table.Rows.Count} records loaded. Editing requires Save changes.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContentGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var info = _columns.FirstOrDefault(c => c.Name == e.PropertyName);
        if (info == null) return;
        // Generated integer PKs are always assigned by SQLite; composite/string keys are supplied when adding a record.
        if (info.IsPrimaryKey && info.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase)) e.Column.IsReadOnly = true;
        e.Column.MinWidth = 100;
        if (e.PropertyName.EndsWith("Line", StringComparison.OrdinalIgnoreCase) || e.PropertyName is "Text" or "HostIntroduction")
            e.Column.Width = new DataGridLength(350);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_table == null) return;
        ContentGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DataRow row = _table.NewRow();
        // Do not guess or generate foreign keys; fill required values in the grid.
        _table.Rows.Add(row);
        SearchBox.Clear();
        ContentGrid.SelectedItem = _table.DefaultView[_table.Rows.Count - 1];
        ContentGrid.ScrollIntoView(ContentGrid.SelectedItem);
        StatusText.Text = "New unsaved record added. Fill required fields, then Save changes.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_table == null || _editor == null || ContentGrid.SelectedItem is not DataRowView view) return;
        try
        {
            if (view.Row.RowState != DataRowState.Added && _editor.HasDependents(_table.TableName, view.Row))
            {
                MessageBox.Show(this, "This record has dependent content. Delete or reassign its child records first. Nothing was deleted.",
                    "Deletion blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show(this, "Mark the selected record for deletion? It will be deleted only when you click Save changes.",
                    "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            view.Row.Delete();
            StatusText.Text = "Marked for deletion (not saved yet).";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Delete failed"); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null || _table == null) return;
        try
        {
            ContentGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ContentGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _editor.SaveChanges(_table);
            string name = _table.TableName;
            LoadTable(name);
            StatusText.Text = "Changes saved transactionally. A backup was created before the write.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message + "\n\nNothing was partially committed. Correct the record and try again.",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_table == null) return;
        if (IsDirty() && MessageBox.Show(this, "Discard ALL unsaved changes on this table?", "Discard changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        LoadTable(_table.TableName);
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null) return;
        try
        {
            string path = _editor.Backup();
            MessageBox.Show(this, $"Backup created:\n{path}", "Backup complete");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Backup failed"); }
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_editor == null) return;
        try { MessageBox.Show(this, _editor.VerifyForeignKeys(), "Foreign key check"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Validation failed"); }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_table == null) return;
        string value = SearchBox.Text.Trim();
        if (value.Length == 0) { _table.DefaultView.RowFilter = ""; return; }
        string safe = value.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("*", "[*]");
        var parts = _table.Columns.Cast<DataColumn>()
            .Select(c => $"CONVERT([{c.ColumnName.Replace("]", "\\]")}], 'System.String') LIKE '%{safe}%'");
        try { _table.DefaultView.RowFilter = string.Join(" OR ", parts); }
        catch { _table.DefaultView.RowFilter = ""; }
    }

    private bool IsDirty()
    {
        if (_table == null) return false;
        ContentGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ContentGrid.CommitEdit(DataGridEditingUnit.Row, true);
        return _table.GetChanges() is { Rows.Count: > 0 };
    }
    private bool CanLeaveTable()
    {
        if (!IsDirty()) return true;
        return MessageBox.Show(this, "You have unsaved changes. Discard them and continue?",
            "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
