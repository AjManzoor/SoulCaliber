using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SoulCaliberStudio;

public partial class StatusEffectsWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly int _optionId;
    private DialogueStatusEffectRow? _active;
    private bool _loading;
    private bool _dirty;

    private sealed record DirectionValue(string Key,string Label)
    {
        public override string ToString() => Label;
    }

    public StatusEffectsWindow(DialogueDesignerStore store, int optionId)
    {
        InitializeComponent();
        _store=store;
        _optionId=optionId;
        _loading=true;
        OutcomeBox.ItemsSource=new[]{"SUCCESS","FAILURE"};
        DirectionBox.ItemsSource=new[]{
            new DirectionValue("ACTOR_TO_PARTNER","Initiator → Recipient"),
            new DirectionValue("PARTNER_TO_ACTOR","Recipient → Initiator"),
            new DirectionValue("BOTH","Both directions")};
        StatusBox.ItemsSource=store.StatusDefinitions(activeOnly:true);
        _loading=false;
        Reload();
    }

    private void Reload(int? selectedId=null)
    {
        _loading=true;
        try
        {
            var rows=_store.StatusEffects(_optionId);
            EffectList.ItemsSource=rows;
            EffectList.SelectedItem=rows.FirstOrDefault(x=>x.Id==selectedId);
            Populate(EffectList.SelectedItem as DialogueStatusEffectRow);
            _dirty=false;
        }
        finally { _loading=false; }
    }
    private void Populate(DialogueStatusEffectRow? row)
    {
        _active=row;
        OutcomeBox.SelectedItem=row?.Outcome??"SUCCESS";
        DirectionBox.SelectedItem=DirectionBox.Items.OfType<DirectionValue>()
            .FirstOrDefault(d=>d.Key==(row?.Direction??"PARTNER_TO_ACTOR"));
        StatusBox.SelectedValue=row?.StatusKey??"ALLY";
    }
    private bool MayDiscard()
    {
        if(!_dirty) return true;
        if(MessageBox.Show(this,"Discard unsaved status-effect edits?","Unsaved changes",
           MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return false;
        _dirty=false; return true;
    }
    private void Editor_Changed(object sender,RoutedEventArgs e)
    {
        if(_loading)return;
        _dirty=true;
        StatusMessage.Text="Unsaved changes. Save before switching or closing.";
    }
    private void EffectList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading)return;
        if(!MayDiscard())
        {
            _loading=true;
            try{EffectList.SelectedItem=_active;}
            finally{_loading=false;}
            return;
        }
        _loading=true;
        try{Populate(EffectList.SelectedItem as DialogueStatusEffectRow);}
        finally{_loading=false;}
    }
    private void New_Click(object sender,RoutedEventArgs e)
    {
        if(!MayDiscard())return;
        _loading=true;
        try{EffectList.SelectedItem=null;Populate(null);}
        finally{_loading=false;}
        StatusMessage.Text="New effect: select an outcome, direction and status, then save.";
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var direction=(DirectionBox.SelectedItem as DirectionValue)?.Key??"";
            int id=_store.SaveStatusEffect(new DialogueStatusEffectRow(_active?.Id??0,_optionId,
                OutcomeBox.SelectedItem as string??"",direction,StatusBox.SelectedValue as string??""));
            Reload(id);
            StatusMessage.Text="Status effect saved. No automatic backup created.";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Save status effect",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(_active is null || !MayDiscard())return;
        if(MessageBox.Show(this,"Delete this status effect?","Confirm",
            MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try
        {
            _store.DeleteStatusEffect(_optionId,_active.Id);
            Reload();
            StatusMessage.Text="Effect deleted.";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Delete status effect",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void Window_Closing(object? sender,CancelEventArgs e){if(!MayDiscard())e.Cancel=true;}
}
