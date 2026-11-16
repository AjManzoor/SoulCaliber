using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class CharacterSkillsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private CharacterSkillDefinition? _selected;
    private bool _loading;
    public CharacterSkillsWindow(DialogueDesignerStore store)
    { InitializeComponent();_store=store;Refresh(); }
    private void Refresh(string? key=null)
    {
        _loading=true;
        try
        {
            var skills=_store.SkillDefinitions().ToList();
            SkillList.ItemsSource=skills;
            SkillList.SelectedItem=skills.FirstOrDefault(s=>s.Key==key)??skills.FirstOrDefault();
            Populate(SkillList.SelectedItem as CharacterSkillDefinition);
        }
        finally { _loading=false; }
    }
    private void Populate(CharacterSkillDefinition? item)
    {
        _selected=item;
        KeyBox.IsReadOnly=item is not null;
        KeyBox.Text=item?.Key??"";NameBox.Text=item?.Name??"";
        DescriptionBox.Text=item?.Description??"";
        MinimumBox.Text=(item?.Minimum??0).ToString(CultureInfo.InvariantCulture);
        MaximumBox.Text=(item?.Maximum??100).ToString(CultureInfo.InvariantCulture);
        DefaultBox.Text=(item?.Default??50).ToString(CultureInfo.InvariantCulture);
        OrderBox.Text=(item?.SortOrder??200).ToString(CultureInfo.InvariantCulture);
        ActiveBox.IsChecked=item?.IsActive??true;
        Status.Text=item is null ? "New skill: choose a permanent key." : "Edit display properties and save. Key is immutable.";
    }
    private void SkillList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    { if(!_loading) Populate(SkillList.SelectedItem as CharacterSkillDefinition); }
    private void New_Click(object sender,RoutedEventArgs e)
    { _loading=true;try{ SkillList.SelectedItem=null;Populate(null); }finally{_loading=false;} }
    private static int Num(string s,string field) => int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)
        ? n:throw new InvalidOperationException($"{field} must be an integer.");
    private void Save_Click(object sender,RoutedEventArgs e)=> Run(()=>
    {
        var item=new CharacterSkillDefinition(KeyBox.Text,NameBox.Text,DescriptionBox.Text,
            Num(MinimumBox.Text,"Minimum"),Num(MaximumBox.Text,"Maximum"),Num(DefaultBox.Text,"Default"),
            Num(OrderBox.Text,"Sort order"),ActiveBox.IsChecked==true);
        _store.SaveSkill(item,_selected is null);
        Refresh(item.Key.Trim().ToUpperInvariant());
        Status.Text="Saved. Reopen Choice Mechanics to refresh skill dropdowns.";
    });
    private void Delete_Click(object sender,RoutedEventArgs e)=> Run(()=>
    {
        if(_selected is null) return;
        if(MessageBox.Show(this,$"Delete {_selected.Name}?", "Delete skill",
            MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        _store.DeleteSkill(_selected.Key);Refresh();
    });
    private void Run(Action action)
    { try { action(); } catch(Exception ex){MessageBox.Show(this,ex.Message,"Character skills",MessageBoxButton.OK,MessageBoxImage.Warning);} }
}
