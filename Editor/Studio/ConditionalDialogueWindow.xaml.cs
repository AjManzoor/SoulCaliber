using SoulCaliber.Shared;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class ConditionalDialogueWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly Node _node;
    private readonly Option? _option;
    private readonly List<DialogueClause> _conditions = new();
    private readonly Dictionary<string, TextBox> _testValues = new();
    private ConditionalVariantRow? _selected;
    private bool _loading;
    private bool _dirty;

    private bool IsIntro => KindBox.SelectedIndex == 0;

    public ConditionalDialogueWindow(DialogueDesignerStore store, Node node, Option? option)
    {
        InitializeComponent();
        _store=store; _node=node; _option=option;
        Heading.Text = $"Conditional dialogue — {node.Key}" +
            (option is null ? "" : $" / {option.Key}");
        var characters=store.Characters();
        SpeakerBox.ItemsSource=new[] { "" }.Concat(characters).ToArray();
        OtherBox.ItemsSource=new[] { "" }.Concat(characters).ToArray();
        PreviewActorBox.ItemsSource=characters;
        PreviewPartnerBox.ItemsSource=characters;
        PreviewActorBox.SelectedItem=characters.FirstOrDefault();
        PreviewPartnerBox.SelectedItem=characters.Skip(1).FirstOrDefault();
        DirectionBox.ItemsSource=DialogueDesignerStore.Directions;
        AttributeBox.ItemsSource=DialogueDesignerStore.RelationshipAttributes;
        DirectionBox.SelectedIndex=0;AttributeBox.SelectedIndex=4;
        OutcomeBox.SelectedIndex=0;
        BuildStats(ActorStatsPanel,"ACTOR_TO_PARTNER");
        BuildStats(PartnerStatsPanel,"PARTNER_TO_ACTOR");
        _loading=true;
        KindBox.SelectedIndex=0;
        _loading=false;
        KindBox.IsEnabled=option is not null;
        foreach(var field in new[] { PriorityBox,PhraseBox,ImageBox })
            field.TextChanged+=(_,_)=>{if(!_loading)_dirty=true;};
        SpeakerBox.SelectionChanged+=(_,_)=>{if(!_loading)_dirty=true;};
        OtherBox.SelectionChanged+=(_,_)=>{if(!_loading)_dirty=true;};
        OutcomeBox.SelectionChanged+=(_,_)=>{if(!_loading)_dirty=true;};
        Refresh();
    }

    private void BuildStats(UniformGrid panel,string direction)
    {
        foreach(string name in DialogueDesignerStore.RelationshipAttributes)
        {
            var box=new StackPanel { Margin=new Thickness(2) };
            box.Children.Add(new TextBlock { Text=name,FontSize=10 });
            var input=new TextBox { Text="0",MinWidth=43 };
            box.Children.Add(input);
            panel.Children.Add(box);
            _testValues[ConditionalDialogueRules.StatKey(direction,name)]=input;
        }
    }

    private bool CanSwitch()
    {
        if (!_dirty) return true;
        if (MessageBox.Show(this,"Discard unsaved conditional dialogue edits?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning)!=MessageBoxResult.Yes) return false;
        _dirty=false;
        return true;
    }

    private void Refresh(int? id=null)
    {
        _loading=true;
        try
        {
            var rows=IsIntro ? _store.ConditionalVariants(_node.Id,null) :
                _option is null ? new List<ConditionalVariantRow>() : _store.ConditionalVariants(null,_option.Id);
            VariantList.ItemsSource=rows;
            var item=rows.FirstOrDefault(x=>x.Id==id) ?? rows.FirstOrDefault();
            VariantList.SelectedItem=item;
            Populate(item);
        }
        finally { _loading=false;_dirty=false; }
    }

    private void Populate(ConditionalVariantRow? row)
    {
        _selected=row;
        SpeakerBox.IsEnabled=row is null;
        OtherBox.IsEnabled=row is null;
        OutcomeBox.IsEnabled=row is null && !IsIntro;
        SpeakerBox.SelectedItem=row?.CharacterId ?? "";
        OtherBox.SelectedItem=row?.VisitorId ?? "";
        OutcomeBox.SelectedIndex=row?.Outcome switch { "SUCCESS"=>1,"FAILURE"=>2,_=>0 };
        PriorityBox.Text=(row?.Priority ?? 0).ToString(CultureInfo.InvariantCulture);
        PhraseBox.Text=row?.Text ?? "";
        ImageBox.Text=row?.ImagePath ?? "";
        OutcomePanel.Visibility=IsIntro ? Visibility.Collapsed : Visibility.Visible;
        ImagePanel.Visibility=IsIntro ? Visibility.Collapsed : Visibility.Visible;
        _conditions.Clear();
        if(row is not null) _conditions.AddRange(row.Conditions);
        int initialCondition = _conditions.Count > 0 ? 0 : -1;
        ReloadConditions(initialCondition);
        if (initialCondition >= 0)
            DisplayCondition(initialCondition);
        else
        {
            DirectionBox.SelectedItem = "PARTNER_TO_ACTOR";
            AttributeBox.SelectedItem = "Trust";
            MinimumBox.Clear();
            MaximumBox.Clear();
        }
        Status.Text=row is null ? "New variant: blank speaker and other match any two participants. Write a line and add at least one condition." :
            "Existing speaker, other character and outcome are immutable. Use New for another variant.";
    }

    private void ReloadConditions(int selectedIndex = -1)
    {
        // Refresh the list without retaining a stale selection after a deletion.
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            ConditionsList.ItemsSource = null;
            ConditionsList.ItemsSource = _conditions.Select(c => $"{c.Direction}: {c.Attribute} " +
                (c.Minimum is null ? "" : $">= {c.Minimum} ") +
                (c.Maximum is null ? "" : $"<= {c.Maximum}")).ToArray();
            ConditionsList.SelectedIndex = selectedIndex >= 0 && selectedIndex < _conditions.Count
                ? selectedIndex : -1;
        }
        finally { _loading = wasLoading; }
    }

    private void ConditionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        int index = ConditionsList.SelectedIndex;
        if (index < 0 || index >= _conditions.Count) return;
        DisplayCondition(index);
        Status.Text = "Change Min/Max above, then click Update selected condition. Remember to save the variant afterwards.";
    }

    private void DisplayCondition(int index)
    {
        var condition = _conditions[index];
        DirectionBox.SelectedItem = condition.Direction;
        AttributeBox.SelectedItem = condition.Attribute;
        MinimumBox.Text = condition.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "";
        MaximumBox.Text = condition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private DialogueClause ConditionFromFields()
    {
        string direction = DirectionBox.SelectedItem as string ?? "";
        string attribute = AttributeBox.SelectedItem as string ?? "";
        if (string.IsNullOrWhiteSpace(direction) || string.IsNullOrWhiteSpace(attribute))
            throw new InvalidOperationException("Choose a relationship direction and attribute.");
        int? min = ParseOptional(MinimumBox.Text);
        int? max = ParseOptional(MaximumBox.Text);
        if (min is null && max is null)
            throw new InvalidOperationException("Set a minimum or maximum.");
        int lowerBound = attribute is "Fear" or "Romance" ? 0 : -100;
        if (min is int lo && (lo < lowerBound || lo > 100) ||
            max is int hi && (hi < lowerBound || hi > 100) ||
            min is int low && max is int high && low > high)
            throw new InvalidOperationException($"Valid {attribute} range is {lowerBound} to 100, with Min <= Max.");
        return new DialogueClause(direction, attribute, min, max);
    }

    private void Kind_Changed(object sender,SelectionChangedEventArgs e)
    {
        if (_loading || _store is null) return;
        if(!CanSwitch())
        {
            _loading=true;
            try { KindBox.SelectedIndex=IsIntro ? 1:0; }
            finally { _loading=false; }
            return;
        }
        Refresh();
    }
    private void Variant_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading)return;
        var next=VariantList.SelectedItem as ConditionalVariantRow;
        if(!CanSwitch())
        {
            _loading=true;
            try { VariantList.SelectedItem=_selected; }
            finally { _loading=false; }
            return;
        }
        _loading=true;
        try { Populate(next); }
        finally { _loading=false; }
    }
    private void New_Click(object sender,RoutedEventArgs e)
    {
        if(!CanSwitch())return;
        _loading=true;
        try { VariantList.SelectedItem=null;Populate(null);_dirty=false; }
        finally { _loading=false; }
    }

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var condition = ConditionFromFields();
            if (_conditions.Any(c => c.Direction == condition.Direction && c.Attribute == condition.Attribute))
                throw new InvalidOperationException("That direction and attribute already has a condition. Select it and click Update selected condition instead.");
            _conditions.Add(condition);
            ReloadConditions(_conditions.Count - 1);
            _dirty = true;
            Status.Text = "Condition added locally. Click Save variant and conditions to write it to SQLite.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Condition validation"); }
    }

    private void UpdateCondition_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int index = ConditionsList.SelectedIndex;
            if (index < 0) throw new InvalidOperationException("Select a condition in the list first.");
            var updated = ConditionFromFields();
            if (_conditions.Where((_, i) => i != index)
                .Any(c => c.Direction == updated.Direction && c.Attribute == updated.Attribute))
                throw new InvalidOperationException("A condition already exists for that direction and attribute.");
            _conditions[index] = updated;
            ReloadConditions(index);
            _dirty = true;
            Status.Text = "Condition updated locally. Click Save variant and conditions to write it to SQLite.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Condition validation"); }
    }

    private void RemoveCondition_Click(object sender, RoutedEventArgs e)
    {
        int index = ConditionsList.SelectedIndex;
        if (index < 0) return;
        _conditions.RemoveAt(index);
        ReloadConditions();
        _dirty = true;
        Status.Text = "Condition removed locally. Click Save variant and conditions to write it to SQLite.";
    }

    private static int? ParseOptional(string s)
    {
        if(string.IsNullOrWhiteSpace(s))return null;
        if(!int.TryParse(s,out int number))throw new InvalidOperationException("Relationship bounds must be whole numbers.");
        return number;
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!int.TryParse(PriorityBox.Text,out int priority))
                throw new InvalidOperationException("Priority must be a whole number.");
            string? outcome=IsIntro ? null : ((OutcomeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()) switch
            { "SUCCESS" => "SUCCESS", "FAILURE" => "FAILURE", _ => null };
            int id=_store.SaveConditionalVariant(new ConditionalVariantRow(_selected?.Id ?? 0,
                IsIntro?_node.Id:null,IsIntro?null:_option?.Id,
                SpeakerBox.SelectedItem as string ?? "",OtherBox.SelectedItem as string,
                outcome,priority,PhraseBox.Text,IsIntro?"":ImageBox.Text,_conditions.ToArray()));
            Refresh(id);
            Status.Text="Saved to the master database; a backup was created.";
        }
        catch(Exception ex){ MessageBox.Show(this,ex.Message,"Unable to save conditional dialogue",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(_selected is null || !CanSwitch())return;
        if(MessageBox.Show(this,"Delete this conditional variant? Standard fallback dialogue remains.",
            "Confirm deletion",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try { _store.DeleteConditionalVariant(_selected.Id);Refresh(); }
        catch(Exception ex){ MessageBox.Show(this,ex.Message,"Delete failed"); }
    }
    private void Preview_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(PreviewActorBox.SelectedItem is not string actor || PreviewPartnerBox.SelectedItem is not string partner || actor==partner)
                throw new InvalidOperationException("Select two different characters.");
            var stats=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach(var (key,input) in _testValues)
            {
                if(!int.TryParse(input.Text,out int number) || number < (key.EndsWith(":FEAR") || key.EndsWith(":ROMANCE") ? 0:-100) || number > 100)
                    throw new InvalidOperationException($"Invalid simulated relationship value for {key}.");
                stats.Add(key,number);
            }
            var intro=_store.PreviewConditionalIntroduction(_node.Id,partner,actor,stats);
            string result=$"INTRODUCTION: {intro.Source}\n{intro.Candidate?.Text}\n{intro.Explanation}\n\n";
            if(_option is not null)
            {
                foreach(bool success in new[] {true,false})
                {
                    string state=success?"SUCCESS":"FAILURE";
                    var player=_store.PreviewConditionalReply(_option.Id,actor,partner,success,stats,_option.DefaultPlayerLine);
                    var host=_store.PreviewConditionalReply(_option.Id,partner,actor,success,stats,
                        success?_option.DefaultHostSuccessLine:_option.DefaultHostFailureLine);
                    result+=$"{state} — ACTOR: {player.Source}\n{player.Candidate?.Text}\n{player.Explanation}\n" +
                        $"{state} — PARTNER: {host.Source}\n{host.Candidate?.Text}\n{host.Explanation}\n\n";
                }
            }
            PreviewResult.Text=result.Replace("{actor}",_store.CharacterName(actor),StringComparison.OrdinalIgnoreCase)
                .Replace("{partner}",_store.CharacterName(partner),StringComparison.OrdinalIgnoreCase);
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Preview failed",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(!CanSwitch())e.Cancel=true;
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
