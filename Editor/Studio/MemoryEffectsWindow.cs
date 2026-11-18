using System.Windows;
using System.Windows.Controls;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

public sealed class MemoryEffectsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly int _optionId;
    private readonly ListBox _items=new() { Height=170 };
    private readonly ComboBox _memory=new(), _trigger=new(), _affected=new(), _severity=new(), _knowledge=new();
    private int _editingId;
    private KnowledgeAudienceSpec? _customAudience;

    public MemoryEffectsWindow(DialogueDesignerStore store,int optionId)
    {
        _store=store;_optionId=optionId;
        Title="Dialogue memory effects"; Width=690;Height=650;MinWidth=550;MinHeight=550;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var scroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var panel=new StackPanel { Margin=new Thickness(20) };scroll.Content=panel;Content=scroll;
        panel.Children.Add(new TextBlock {Text="Each effect records one memory occurrence after the choice is resolved. No relationship changes are implied.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(_items); _items.SelectionChanged+=(_,__)=>Select();
        AddField(panel,"Memory tag",_memory);
        AddField(panel,"When to record",_trigger);
        AddField(panel,"Affected character(s)",_affected);
        AddField(panel,"Severity override",_severity);
        AddField(panel,"Knowledge override",_knowledge);
        _memory.ItemsSource=_store.MemoryTypeDefinitions(true).ToList();
        _memory.DisplayMemberPath="Name";
        _trigger.ItemsSource=new[]{"SELECTED","SUCCESS","FAILURE","BOTH"};
        _affected.ItemsSource=new[] { new { Key="PARTNER", Label="Recipient" }, new { Key="ACTOR", Label="Initiator" }, new { Key="BOTH", Label="Both" } };
        _affected.DisplayMemberPath="Label";
        _affected.SelectedValuePath="Key";
        _severity.ItemsSource=new[]{"Use catalogue default","MAJOR","MINOR"};
        _knowledge.ItemsSource=new[]{"Use catalogue default","QUIET","ALLIES","GLOBAL","Custom audiences"};
        AddButton(panel,"Configure custom knowledge audiences...",ConfigureAudiences);
        var buttons=new WrapPanel {Margin=new Thickness(0,12,0,0)};
        AddButton(buttons,"New",()=>Clear());
        AddButton(buttons,"Save",Save);
        AddButton(buttons,"Delete selected",Delete);
        AddButton(buttons,"Close",Close);
        panel.Children.Add(buttons);
        Reload();
    }
    private static void AddField(Panel panel,string label,ComboBox combo)
    {
        panel.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,9,0,3)});
        combo.MinHeight=27;panel.Children.Add(combo);
    }
    private static void AddButton(Panel panel,string label,Action action)
    {
        var b=new Button {Content=label,Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,8,0)};
        b.Click+=(_,__)=>action();panel.Children.Add(b);
    }
    private void Reload(int selectId=0)
    {
        var rows=_store.MemoryEffects(_optionId).ToList();_items.ItemsSource=rows;
        _items.SelectedItem=rows.FirstOrDefault(x=>x.Id==selectId);
        if(_items.SelectedItem is null) Clear();
    }
    private void Clear()
    {
        _editingId=0;_items.SelectedItem=null;
        _memory.SelectedIndex=_memory.Items.Count>0?0:-1;
        _trigger.SelectedItem="SUCCESS";_affected.SelectedValue="PARTNER";
        _severity.SelectedIndex=0;_knowledge.SelectedIndex=0;
        _customAudience=null;
    }
    private void Select()
    {
        if(_items.SelectedItem is not DialogueMemoryEffectRow row)return;
        _editingId=row.Id;
        _memory.SelectedItem=_memory.Items.OfType<MemoryTypeDefinition>().FirstOrDefault(x=>x.Key==row.MemoryKey);
        _trigger.SelectedItem=row.Trigger;_affected.SelectedValue=row.Affected;
        _severity.SelectedItem=row.SeverityOverride??"Use catalogue default";
        _customAudience=_store.MemoryEffectAudience(row.Id);
        _knowledge.SelectedItem=_customAudience is null ? row.KnowledgeOverride??"Use catalogue default" : "Custom audiences";
    }
    private void ConfigureAudiences()
    {
        var editor=new KnowledgeAudienceEditorWindow(_store,_customAudience) { Owner=this };
        if(editor.ShowDialog()==true)
        {
            _customAudience=editor.Result;
            _knowledge.SelectedItem="Custom audiences";
        }
    }
    private void Save()
    {
        try
        {
            if(_memory.SelectedItem is not MemoryTypeDefinition memory)throw new InvalidOperationException("Select a memory type.");
            string? severity=_severity.SelectedIndex==0?null:_severity.SelectedItem as string;
            bool custom=_knowledge.SelectedItem as string=="Custom audiences";
            string? knowledge=custom || _knowledge.SelectedIndex==0?null:_knowledge.SelectedItem as string;
            int id=_store.SaveMemoryEffect(new(_editingId,_optionId,memory.Key,
                _trigger.SelectedItem as string??"SUCCESS",_affected.SelectedValue as string??"PARTNER",severity,knowledge),
                custom ? _customAudience ?? new KnowledgeAudienceSpec() : null);
            Reload(id);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Memory effect",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void Delete()
    {
        if(_editingId==0)return;
        if(MessageBox.Show(this,"Delete this memory effect?","Confirm",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try{_store.DeleteMemoryEffect(_optionId,_editingId);Reload();}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Memory effect",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
