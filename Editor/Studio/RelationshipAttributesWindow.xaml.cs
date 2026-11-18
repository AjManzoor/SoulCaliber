using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class RelationshipAttributesWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private RelationshipAttributeDefinition? _selected;
    private bool _loading;
    public RelationshipAttributesWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh();
    }
    private void Refresh(string? selectKey = null)
    {
        _loading = true;
        try
        {
            var items = _store.AttributeDefinitions().ToList();
            AttributeList.ItemsSource = items;
            AttributeList.SelectedItem = items.FirstOrDefault(a => a.Key == selectKey) ?? items.FirstOrDefault();
            Populate(AttributeList.SelectedItem as RelationshipAttributeDefinition);
        }
        finally { _loading = false; }
    }
    private void Populate(RelationshipAttributeDefinition? a)
    {
        _selected=a;
        KeyBox.IsReadOnly=a is not null;
        KeyBox.Text=a?.Key ?? "";
        NameBox.Text=a?.Name ?? "";
        DescriptionBox.Text=a?.Description ?? "";
        MinimumBox.Text=(a?.Minimum ?? 0).ToString(CultureInfo.InvariantCulture);
        MaximumBox.Text=(a?.Maximum ?? 100).ToString(CultureInfo.InvariantCulture);
        DefaultBox.Text=(a?.Default ?? 0).ToString(CultureInfo.InvariantCulture);
        IconBox.Text=a?.IconPath ?? "";
        ColorBox.Text=a?.Color ?? "#D6DAE7";
        OrderBox.Text=(a?.SortOrder ?? 200).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked=a?.IsActive ?? true;
        Status.Text=a is null ? "New attribute: choose a permanent key and valid numeric bounds." :
            "Edit values and save. This key is permanent and is used by existing content and saved games.";
    }
    private void AttributeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) Populate(AttributeList.SelectedItem as RelationshipAttributeDefinition);
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        _loading=true;
        try { AttributeList.SelectedItem=null; Populate(null); }
        finally { _loading=false; }
    }
    private static int Parse(string input, string name) => int.TryParse(input, NumberStyles.Integer,
        CultureInfo.InvariantCulture,out int result) ? result : throw new InvalidOperationException($"{name} must be an integer.");
    private void Save_Click(object sender,RoutedEventArgs e) => Run(() =>
    {
        var row=new RelationshipAttributeDefinition(KeyBox.Text.Trim(),NameBox.Text.Trim(),DescriptionBox.Text,
            Parse(MinimumBox.Text,"Minimum"),Parse(MaximumBox.Text,"Maximum"),Parse(DefaultBox.Text,"Default"),
            IconBox.Text.Trim(),ColorBox.Text.Trim(),Parse(OrderBox.Text,"Sort order"),ActiveBox.IsChecked == true);
        _store.SaveAttribute(row,_selected is null);
        Refresh(row.Key.ToUpperInvariant());
        Status.Text="Attribute saved. Reopen other editor windows to refresh their attribute choices.";
    });
    private void Delete_Click(object sender,RoutedEventArgs e) => Run(() =>
    {
        if (_selected is null) return;
        if (MessageBox.Show(this,$"Delete {_selected.Name} [{_selected.Key}]?\n\nExisting savegames that used this key may no longer read correctly.",
                "Delete attribute",MessageBoxButton.YesNo,MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteAttribute(_selected.Key);
        Refresh();
        Status.Text="Deleted unused relationship attribute.";
    });
    private void Run(Action action)
    {
        try { action(); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Relationship attributes",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
