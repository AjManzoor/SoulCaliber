using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoulCaliberStudio;

/// <summary>One dynamic selection per authored option; each chosen character is resolved before any writes.</summary>
public sealed class DynamicTargetsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly int _optionId;
    private readonly ComboBox _selectionMode = new(), _owner = new(), _direction = new(), _groupStatus = new();
    private readonly StackPanel _groupFields = new(), _customFields = new(), _characterList = new();
    private readonly TextBox _characterSearch = new();
    private readonly TextBlock _selectedCount = new() { Foreground = Brushes.White };
    private readonly Dictionary<string,CheckBox> _characterChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox _outcome = new(), _from = new(), _to = new(), _status = new(), _reverseStatus = new();
    private readonly CheckBox _mutual = new();
    private readonly ListBox _effects = new();
    private int _selectedEffectId;

    private sealed record Choice(string Key,string Title)
    {
        public override string ToString() => Title;
    }
    private static Choice Item(string key,string title) => new(key,title);
    private static string Value(ComboBox combo) => (combo.SelectedItem as Choice)?.Key ?? "";
    private static void Select(ComboBox combo,string? key) =>
        combo.SelectedItem=combo.Items.OfType<Choice>().FirstOrDefault(x=>x.Key.Equals(key,StringComparison.OrdinalIgnoreCase));
    private static TextBlock Text(string value) => new()
    {
        Text=value,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,
        Margin=new Thickness(0,6,0,3)
    };
    private static Button MakeButton(string title,Action handler)
    {
        var b=new Button { Content=title,MinWidth=115,Margin=new Thickness(0,4,8,4),Padding=new Thickness(8,5,8,5) };
        b.Click+=(_,_)=>handler();
        return b;
    }
    private static ComboBox Combo(IEnumerable<Choice> items)
    {
        return new ComboBox { ItemsSource=items.ToList(),MinWidth=190,Margin=new Thickness(0,0,0,6) };
    }
    private static void Fill(ComboBox combo,IEnumerable<Choice> choices)
    {
        combo.ItemsSource=choices.ToList();combo.SelectedIndex=0;
    }
    public DynamicTargetsWindow(DialogueDesignerStore store,int optionId)
    {
        _store=store;_optionId=optionId;
        Title="Dynamic choices and third-party status effects";
        Width=840;Height=760;MinWidth=650;MinHeight=540;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(24,29,39));
        Foreground=Brushes.White;
        var root=new DockPanel { Margin=new Thickness(16) };
        Content=root;
        var footer=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        footer.Children.Add(MakeButton("Close",Close));
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        var body=new StackPanel { Margin=new Thickness(0,0,12,0) };scroll.Content=body;
        body.Children.Add(new TextBlock { Text="DYNAMIC CHARACTER SELECTION",FontSize=19,FontWeight=FontWeights.Bold,Foreground=Brushes.White });
        body.Children.Add(Text("Choose how the selectable characters are determined. The player picks ONE target; {target} is then available in subsequent dialogue and status effects."));
        body.Children.Add(Text("How should characters be selected?"));
        body.Children.Add(_selectionMode);
        body.Children.Add(_groupFields);
        _groupFields.Children.Add(Text("Whose relationship group?"));_groupFields.Children.Add(_owner);
        _groupFields.Children.Add(Text("Which relationship direction?"));_groupFields.Children.Add(_direction);
        _groupFields.Children.Add(Text("Required current relationship status"));_groupFields.Children.Add(_groupStatus);
        body.Children.Add(_customFields);
        _customFields.Children.Add(Text("Select the characters who may appear in the picker, regardless of their current relationships. The current initiator and recipient are automatically excluded at runtime."));
        _customFields.Children.Add(Text("Search characters"));
        _characterSearch.Margin=new Thickness(0,0,0,6);
        _characterSearch.TextChanged+=(_,_)=>FilterCharacters();
        _customFields.Children.Add(_characterSearch);
        var customScroller=new ScrollViewer { MaxHeight=230,MinHeight=100,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(8) };
        customScroller.Content=_characterList;
        _customFields.Children.Add(customScroller);
        _selectedCount.Margin=new Thickness(0,5,0,6);
        _customFields.Children.Add(_selectedCount);
        var selectionActions=new WrapPanel();
        selectionActions.Children.Add(MakeButton("Save selection rule",SaveSelection));
        selectionActions.Children.Add(MakeButton("Remove selection rule",RemoveSelection));
        body.Children.Add(selectionActions);
        body.Children.Add(new Separator {Margin=new Thickness(0,18,0,10)});
        body.Children.Add(new TextBlock { Text="STATUS CHANGES FOR THE SELECTED CHARACTER",FontSize=17,FontWeight=FontWeights.Bold,Foreground=Brushes.White });
        body.Children.Add(Text("Create a SUCCESS or FAILURE effect. From/To can be Initiator, Recipient, the one selected {target}, or a named character. Mutual is enabled by default; uncheck it for a one-way change."));
        _effects.MinHeight=110;_effects.MaxHeight=160;
        _effects.SelectionChanged+=(_,_)=>PopulateSelected();
        body.Children.Add(_effects);
        body.Children.Add(Text("Apply when"));body.Children.Add(_outcome);
        var pair=new Grid();pair.ColumnDefinitions.Add(new ColumnDefinition());pair.ColumnDefinitions.Add(new ColumnDefinition());
        var fromBox=new StackPanel {Margin=new Thickness(0,0,10,0)};
        fromBox.Children.Add(Text("From"));fromBox.Children.Add(_from);
        Grid.SetColumn(fromBox,0);pair.Children.Add(fromBox);
        var toBox=new StackPanel();toBox.Children.Add(Text("To"));toBox.Children.Add(_to);
        Grid.SetColumn(toBox,1);pair.Children.Add(toBox);
        body.Children.Add(pair);
        body.Children.Add(Text("Set From → To to"));body.Children.Add(_status);
        _mutual.Content="Also change the reverse relationship (To → From)";
        _mutual.Foreground=Brushes.White;_mutual.IsChecked=true;_mutual.Margin=new Thickness(0,8,0,4);
        _mutual.Checked+=(_,_)=>_reverseStatus.IsEnabled=true;
        _mutual.Unchecked+=(_,_)=>_reverseStatus.IsEnabled=false;
        body.Children.Add(_mutual);
        body.Children.Add(Text("Reverse status (can differ from forward status)"));body.Children.Add(_reverseStatus);
        var actions=new WrapPanel();
        actions.Children.Add(MakeButton("New status effect",()=>{_effects.SelectedItem=null;ResetEffect();}));
        actions.Children.Add(MakeButton("Save status effect",SaveEffect));
        actions.Children.Add(MakeButton("Delete selected",DeleteEffect));
        body.Children.Add(actions);
        body.Children.Add(Text("Example: Recipient → Selected target = Neutral, reverse = Neutral. Only the selected character changes. No bulk changes or memories are created automatically."));

        var statuses=_store.StatusDefinitions(activeOnly:true)
            .Select(x=>Item(x.Key,x.Name+" ["+x.Key+"]")).ToList();
        Fill(_selectionMode,[Item("GROUP","Current relationship group"),Item("CUSTOM","Custom list of characters")]);
        _selectionMode.SelectionChanged+=(_,_)=>UpdateSelectionMode();
        foreach(var character in _store.StatusCharacters())
        {
            var check=new CheckBox {Content=$"{character.Name} [{character.Id}]",Foreground=Brushes.White,
                Margin=new Thickness(3,4,0,4)};
            check.Checked+=(_,_)=>UpdateSelectedCount();
            check.Unchecked+=(_,_)=>UpdateSelectedCount();
            _characterChecks[character.Id]=check;
            _characterList.Children.Add(check);
        }
        UpdateSelectedCount();
        Fill(_owner,[Item("RECIPIENT","Recipient"),Item("INITIATOR","Initiator")]);
        Fill(_direction,[Item("OUTGOING","Owner → other character"),Item("INCOMING","Other character → owner")]);
        Fill(_groupStatus,statuses);
        Fill(_outcome,[Item("SUCCESS","Success"),Item("FAILURE","Failure")]);
        var characters=new[] {Item("RECIPIENT","Recipient"),Item("INITIATOR","Initiator"),Item("TARGET","Selected target")}
            .Concat(_store.Characters().Select(id=>Item(id,"Character: "+id))).ToList();
        Fill(_from,characters);Fill(_to,characters);
        Fill(_status,statuses);Fill(_reverseStatus,statuses);
        Select(_from,"RECIPIENT");Select(_to,"TARGET");Select(_status,"NEUTRAL");Select(_reverseStatus,"NEUTRAL");
        var existing=_store.DynamicTargets(optionId);
        if(existing is not null)
        {
            Select(_owner,existing.OwnerRef);Select(_direction,existing.RelationshipDirection);
            Select(_groupStatus,existing.RequiredStatusKey);
            Select(_selectionMode,existing.SelectionMode);
            foreach(var id in existing.CharacterIds??Array.Empty<string>())
                if(_characterChecks.TryGetValue(id,out var check)) check.IsChecked=true;
        }
        UpdateSelectionMode();
        ReloadEffects();
    }
    private void Do(Action work,string title)
    {
        try { work(); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,title,MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private void UpdateSelectionMode()
    {
        bool custom=Value(_selectionMode)=="CUSTOM";
        _groupFields.Visibility=custom?Visibility.Collapsed:Visibility.Visible;
        _customFields.Visibility=custom?Visibility.Visible:Visibility.Collapsed;
    }
    private void FilterCharacters()
    {
        string filter=_characterSearch.Text.Trim();
        foreach(var (_,check) in _characterChecks)
            check.Visibility=string.IsNullOrWhiteSpace(filter) ||
                (check.Content?.ToString()?.Contains(filter,StringComparison.OrdinalIgnoreCase)??false)
                ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateSelectedCount()
    {
        _selectedCount.Text=$"{_characterChecks.Values.Count(x=>x.IsChecked==true)} character(s) selected";
    }
    private void SaveSelection()=>Do(()=>
    {
        string mode=Value(_selectionMode);
        var selected=mode=="CUSTOM" ? _characterChecks
            .Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToList() : new List<string>();
        _store.SaveDynamicTargets(new(_optionId,Value(_owner),Value(_direction),Value(_groupStatus),mode,selected),_optionId);
        MessageBox.Show(this,mode=="CUSTOM" ? "Custom character list saved." : "Relationship group selection saved.","Saved");
    },"Dynamic target selection");
    private void RemoveSelection()=>Do(()=>
    {
        _store.SaveDynamicTargets(null,_optionId);
        MessageBox.Show(this,"This choice no longer generates a target selection.","Removed");
    },"Dynamic target selection");
    private void ReloadEffects(int id=0)
    {
        var rows=_store.PairStatusEffects(_optionId);
        _effects.ItemsSource=rows;
        _effects.SelectedItem=rows.FirstOrDefault(x=>x.Id==id);
        if(_effects.SelectedItem is null) ResetEffect();
    }
    private void ResetEffect()
    {
        _selectedEffectId=0;
        Select(_outcome,"SUCCESS");Select(_from,"RECIPIENT");Select(_to,"TARGET");
        Select(_status,"NEUTRAL");Select(_reverseStatus,"NEUTRAL");_mutual.IsChecked=true;
    }
    private void PopulateSelected()
    {
        if(_effects.SelectedItem is not PairStatusEffectRow row)return;
        _selectedEffectId=row.Id;
        Select(_outcome,row.Outcome);Select(_from,row.FromRef);Select(_to,row.ToRef);
        Select(_status,row.StatusKey);Select(_reverseStatus,row.ReverseStatusKey??row.StatusKey);
        _mutual.IsChecked=row.ReverseStatusKey is not null;
    }
    private void SaveEffect()=>Do(()=>
    {
        var row=new PairStatusEffectRow(_selectedEffectId,_optionId,Value(_outcome),Value(_from),Value(_to),
            Value(_status),_mutual.IsChecked==true ? Value(_reverseStatus) : null);
        int id=_store.SavePairStatusEffect(row);
        ReloadEffects(id);
    },"Status effect");
    private void DeleteEffect()=>Do(()=>
    {
        if(_selectedEffectId==0)return;
        _store.DeletePairStatusEffect(_selectedEffectId,_optionId);ReloadEffects();
    },"Status effect");
}
