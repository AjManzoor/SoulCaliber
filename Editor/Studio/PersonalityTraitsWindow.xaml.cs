using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class PersonalityTraitsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private PersonalityTraitDefinition? _selected;
    private PersonalityTraitModifier? _selectedModifier;
    private bool _loading;
    public PersonalityTraitsWindow(DialogueDesignerStore store)
    {
        InitializeComponent();
        _store=store;
        OutcomeBox.ItemsSource=new[] { "BOTH", "SUCCESS", "FAILURE" };
        ChangeBox.ItemsSource=new[] { "BOTH", "GAIN", "LOSS" };
        AttributeBox.ItemsSource=store.AttributeDefinitions(true);
        Refresh();
    }
    private void Refresh(string? key=null)
    {
        _loading=true;
        try
        {
            var list=_store.TraitDefinitions().ToArray();
            TraitList.ItemsSource=list;
            TraitList.SelectedItem=list.FirstOrDefault(x=>x.Key==key) ?? list.FirstOrDefault();
            Populate(TraitList.SelectedItem as PersonalityTraitDefinition);
        }
        finally { _loading=false; }
    }
    private void Populate(PersonalityTraitDefinition? trait)
    {
        _selected=trait;
        KeyBox.IsReadOnly=trait is not null;
        KeyBox.Text=trait?.Key ?? "";
        NameBox.Text=trait?.Name ?? "";
        DescriptionBox.Text=trait?.Description ?? "";
        SortBox.Text=(trait?.SortOrder ?? 100).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked=trait?.IsActive ?? true;
        RefreshModifiers();
    }
    private void RefreshModifiers(int selectedId=0)
    {
        _loading=true;
        try
        {
            var list=_selected is null ? Array.Empty<PersonalityTraitModifier>() : _store.TraitModifiers(_selected.Key).ToArray();
            ModifierList.ItemsSource=list;
            ModifierList.SelectedItem=list.FirstOrDefault(x=>x.Id==selectedId) ?? list.FirstOrDefault();
            PopulateModifier(ModifierList.SelectedItem as PersonalityTraitModifier);
        }
        finally { _loading=false; }
    }
    private void PopulateModifier(PersonalityTraitModifier? item)
    {
        _selectedModifier=item;
        AttributeBox.SelectedValue=item?.AttributeKey;
        if (item is null && AttributeBox.Items.Count>0) AttributeBox.SelectedIndex=0;
        OutcomeBox.SelectedItem=item?.Outcome ?? "BOTH";
        ChangeBox.SelectedItem=item?.Change ?? "BOTH";
        FactorBox.Text=(item?.Factor ?? 1).ToString(CultureInfo.InvariantCulture);
    }
    private void TraitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading) Populate(TraitList.SelectedItem as PersonalityTraitDefinition); }
    private void ModifierList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading) PopulateModifier(ModifierList.SelectedItem as PersonalityTraitModifier); }
    private void NewTrait_Click(object sender,RoutedEventArgs e)
    {
        _loading=true;
        try { TraitList.SelectedItem=null;Populate(null); }
        finally { _loading=false; }
    }
    private void SaveTrait_Click(object sender,RoutedEventArgs e) => Run(()=>
    {
        if (!int.TryParse(SortBox.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int sort))
            throw new InvalidOperationException("Sort order must be an integer.");
        var item=new PersonalityTraitDefinition(KeyBox.Text.Trim().ToUpperInvariant(),NameBox.Text.Trim(),
            DescriptionBox.Text.Trim(),sort,ActiveBox.IsChecked==true);
        _store.SaveTrait(item,_selected is null);
        Refresh(item.Key);
        Status.Text="Trait saved. Reopen Character Manager to refresh tags if necessary.";
    });
    private void DeleteTrait_Click(object sender,RoutedEventArgs e)=>Run(()=>
    {
        if (_selected is null) return;
        if (MessageBox.Show(this,$"Delete {_selected.Name}? This also deletes its modifiers.",
            "Delete personality trait",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        _store.DeleteTrait(_selected.Key);
        Refresh();
        Status.Text="Unused trait deleted.";
    });
    private void NewModifier_Click(object sender,RoutedEventArgs e)
    {
        _loading=true;
        try { ModifierList.SelectedItem=null;PopulateModifier(null); }
        finally { _loading=false; }
    }
    private void SaveModifier_Click(object sender,RoutedEventArgs e)=>Run(()=>
    {
        if (_selected is null) throw new InvalidOperationException("Save a personality trait first.");
        if (AttributeBox.SelectedValue is not string attribute)
            throw new InvalidOperationException("Choose a relationship attribute.");
        if (!double.TryParse(FactorBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double factor))
            throw new InvalidOperationException("Factor must be a number, e.g. 0.5 or 1.5.");
        var row=new PersonalityTraitModifier(_selectedModifier?.Id ?? 0,_selected.Key,attribute,
            (string)OutcomeBox.SelectedItem,(string)ChangeBox.SelectedItem,factor);
        _store.SaveTraitModifier(row);
        RefreshModifiers(row.Id);
        Status.Text="Modifier saved. Reopen interaction scenes to load the updated formula.";
    });
    private void DeleteModifier_Click(object sender,RoutedEventArgs e)=>Run(()=>
    {
        if (_selectedModifier is null) return;
        _store.DeleteTraitModifier(_selectedModifier.Id);
        RefreshModifiers();
        Status.Text="Modifier deleted.";
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Run(Action action)
    {
        try { action(); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Personality traits",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
}
