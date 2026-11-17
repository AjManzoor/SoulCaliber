using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

/// <summary>Simple tree editor for nested memory conditions. No arbitrary expression text or scripts.</summary>
public sealed class MemoryRequirementsWindow : Window
{
    private sealed record ReferenceChoice(string Key, string Description)
    {
        public override string ToString() => Description;
    }
    private sealed record DirectionChoice(string Key, string Description)
    {
        public override string ToString() => Description;
    }

    private readonly DialogueDesignerStore _store;
    private readonly Func<MemoryConditionNode?> _loadConditions;
    private readonly Action<MemoryConditionNode?> _saveConditions;
    private readonly string _subject;
    private readonly TreeView _tree = new() { MinHeight = 170, Height = 260 };
    private readonly ComboBox _tag = new(), _actor = new(), _affected = new(), _knower = new();
    private readonly ComboBox _status = new(), _statusDirection = new();
    private readonly TextBox _minimum = new(), _maximum = new();
    private readonly ComboBox _operator = new();
    private readonly TextBlock _editorHelp = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
    private readonly List<MemoryTypeDefinition> _tags;
    private readonly List<RelationshipStatusDefinition> _statuses;
    private readonly List<ReferenceChoice> _references;
    private MemoryConditionNode? _root;
    private MemoryConditionNode? _selected;
    private bool _loading;
    private bool _dirty;

    public MemoryRequirementsWindow(DialogueDesignerStore store, int optionId)
        : this(store, () => store.GetMemoryRequirements(optionId),
            tree => store.SaveMemoryRequirements(optionId, tree), "dialogue choice") { }

    // Also used by authored special encounters. Both owners share the same
    // validation, tree UI and JSON format; no second condition engine.
    public MemoryRequirementsWindow(DialogueDesignerStore store,
        Func<MemoryConditionNode?> loadConditions,
        Action<MemoryConditionNode?> saveConditions, string subject)
    {
        _store = store;
        _loadConditions = loadConditions;
        _saveConditions = saveConditions;
        _subject = subject;
        Title = $"Advanced requirements — {_subject}";
        Width = 840; Height = 790; MinWidth = 620; MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _tags = store.MemoryTypeDefinitions(true).ToList();
        _statuses = store.StatusDefinitions(activeOnly: true).ToList();
        _references = new List<ReferenceChoice>
        {
            new("ANY", "Any character / no filter"),
            new("ACTOR", "Current conversation initiator"),
            new("PARTNER", "Current conversation recipient")
        };
        _references.AddRange(store.MemoryRequirementCharacterIds()
            .Select(id => new ReferenceChoice("CHAR:" + id, "Specific character: " + id)));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = panel;
        Content = scroll;
        panel.Children.Add(new TextBlock
        {
            Text = "Build a single tree of memory and relationship-status checks. AND requires all children, OR requires any, " +
                   "and NOT negates exactly one child. Existing simple numerical and status requirements still apply using AND.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock { Text = "CONDITION TREE", FontWeight = FontWeights.Bold });
        _tree.Margin = new Thickness(0, 6, 0, 8);
        _tree.SelectedItemChanged += (_, _) => OnSelection();
        panel.Children.Add(_tree);

        var controls = new WrapPanel();
        AddButton(controls, "Add memory check", () => AddNode("MEMORY"));
        AddButton(controls, "Add status check", () => AddNode("STATUS"));
        AddButton(controls, "Add AND", () => AddNode("AND"));
        AddButton(controls, "Add OR", () => AddNode("OR"));
        AddButton(controls, "Add NOT", () => AddNode("NOT"));
        AddButton(controls, "Remove selected", RemoveSelected);
        panel.Children.Add(controls);

        panel.Children.Add(new TextBlock { Text = "EDIT SELECTED NODE", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 2) });
        panel.Children.Add(_editorHelp);
        AddField(panel, "Group operator (for a selected group)", _operator);
        _operator.ItemsSource = new[] { "AND", "OR", "NOT" };
        AddField(panel, "Memory type", _tag);
        _tag.ItemsSource = _tags; _tag.DisplayMemberPath = "Name";
        AddField(panel, "Required relationship status", _status);
        _status.ItemsSource = _statuses;
        AddField(panel, "Relationship direction to check", _statusDirection);
        _statusDirection.ItemsSource = new[]
        {
            new DirectionChoice("ACTOR_TO_PARTNER", "Initiator → Recipient"),
            new DirectionChoice("PARTNER_TO_ACTOR", "Recipient → Initiator"),
            new DirectionChoice("BOTH", "Both directions must match")
        };
        AddField(panel, "Who performed the action?", _actor);
        AddField(panel, "Who was directly affected?", _affected);
        AddField(panel, "Who must know? (Any = check whether it happened)", _knower);
        foreach (var combo in new[] { _actor, _affected, _knower }) combo.ItemsSource = _references;
        var countRow = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var left = new StackPanel { Width = 160, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(new TextBlock { Text = "Minimum occurrences" });
        left.Children.Add(_minimum);
        var right = new StackPanel { Width = 160 };
        right.Children.Add(new TextBlock { Text = "Maximum (blank = any)" });
        right.Children.Add(_maximum);
        countRow.Children.Add(left); countRow.Children.Add(right);
        panel.Children.Add(countRow);
        var apply = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddButton(apply, "Apply selected node edits", () => { ApplySelected(); });
        AddButton(apply, "Clear all requirements", ClearAll);
        panel.Children.Add(apply);
        panel.Children.Add(new TextBlock
        {
            Text = "Apply edits to the selected node before selecting another. Save tree writes the whole tree to your master content database.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8)
        });
        var footer = new WrapPanel();
        AddButton(footer, "Save tree", Save);
        AddButton(footer, "Close", Close);
        panel.Children.Add(footer);
        _root = _loadConditions();
        Refresh(_root);
        Closing += (_, e) =>
        {
            if (!_dirty) return;
            if (MessageBox.Show(this, "Discard unsaved advanced requirement changes?", "Unsaved changes",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
    }

    private static void AddButton(Panel panel, string label, Action action)
    {
        var b = new Button { Content = label, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 6, 10, 6) };
        b.Click += (_, _) => action(); panel.Children.Add(b);
    }

    private static void AddField(Panel panel, string label, ComboBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 3) });
        box.MinHeight = 27;
        panel.Children.Add(box);
    }

    private TreeViewItem BuildItem(MemoryConditionNode node, MemoryConditionNode? selected)
    {
        var item = new TreeViewItem { Tag = node, Header = MemoryConditionRules.Describe(node), IsExpanded = true };
        foreach (var child in node.Children ?? new List<MemoryConditionNode>()) item.Items.Add(BuildItem(child, selected));
        if (ReferenceEquals(node, selected)) item.IsSelected = true;
        return item;
    }

    private void Refresh(MemoryConditionNode? selected)
    {
        _tree.Items.Clear();
        if (_root is not null) _tree.Items.Add(BuildItem(_root, selected));
        _selected = selected;
        ShowSelected();
    }

    private void OnSelection()
    {
        if (_loading) return;
        _selected = (_tree.SelectedItem as TreeViewItem)?.Tag as MemoryConditionNode;
        ShowSelected();
    }

    private static void Pick(ComboBox combo, string value) =>
        combo.SelectedItem = combo.Items.OfType<ReferenceChoice>().FirstOrDefault(x => x.Key == value);

    private static string SelectedReference(ComboBox combo) =>
        (combo.SelectedItem as ReferenceChoice)?.Key ?? "ANY";

    private void ShowSelected()
    {
        _loading = true;
        try
        {
            var n = _selected;
            bool memory = n?.Kind == "MEMORY";
            bool status = n?.Kind == "STATUS";
            _operator.IsEnabled = n is not null && !memory && !status;
            _status.IsEnabled = status;
            _statusDirection.IsEnabled = status;
            _status.SelectedItem = _statuses.FirstOrDefault(x => x.Key == n?.StatusKey);
            _statusDirection.SelectedItem = _statusDirection.Items.OfType<DirectionChoice>()
                .FirstOrDefault(x => x.Key == (n?.StatusDirection ?? "PARTNER_TO_ACTOR"));
            _tag.IsEnabled = memory;
            _actor.IsEnabled = memory;
            _affected.IsEnabled = memory;
            _knower.IsEnabled = memory;
            _minimum.IsEnabled = memory;
            _maximum.IsEnabled = memory;
            _operator.SelectedItem = memory || status ? null : n?.Kind;
            _tag.SelectedItem = _tags.FirstOrDefault(x => x.Key == n?.MemoryKey);
            Pick(_actor, n?.ActorRef ?? "ANY");
            Pick(_affected, n?.AffectedRef ?? "ANY");
            Pick(_knower, n?.KnowerRef ?? "ANY");
            _minimum.Text = n is null ? "1" : n.MinimumCount.ToString();
            _maximum.Text = n?.MaximumCount?.ToString() ?? "";
            _editorHelp.Text = n is null ? "Select a node, or add a memory/status check to begin."
                : memory ? "A memory check counts historical occurrences. Any for knowledge ignores who knows."
                : status ? "A status check examines current directional Ally/Enemy/custom status, not the authored starting value."
                : "Select AND, OR, or NOT. NOT must contain exactly one child.";
        }
        finally { _loading = false; }
    }

    private MemoryConditionNode? ParentOf(MemoryConditionNode current, MemoryConditionNode target)
    {
        foreach (var child in current.Children)
        {
            if (ReferenceEquals(child, target)) return current;
            var parent = ParentOf(child, target);
            if (parent is not null) return parent;
        }
        return null;
    }

    private void AddNode(string kind)
    {
        try
        {
            if (_root is null)
            {
                if (kind is "MEMORY" or "STATUS")
                {
                    var initial = kind == "MEMORY" ? NewMemory() : NewStatus();
                    _root = new MemoryConditionNode { Kind = "AND", Children = new() { initial } };
                    _dirty = true;
                    Refresh(initial);
                }
                else
                {
                    _root = new MemoryConditionNode { Kind = kind };
                    _dirty = true;
                    Refresh(_root);
                }
                return;
            }
            var parent = _selected is null ? _root :
                _selected.Kind is "MEMORY" or "STATUS" ? ParentOf(_root, _selected) ?? _root : _selected;
            if (parent.Kind == "NOT" && parent.Children.Count >= 1)
                throw new InvalidOperationException("NOT can contain only one condition. Select the enclosing AND/OR group instead.");
            var added = kind switch
            {
                "MEMORY" => NewMemory(),
                "STATUS" => NewStatus(),
                _ => new MemoryConditionNode { Kind = kind }
            };
            parent.Children.Add(added);
            _dirty = true;
            Refresh(added);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private MemoryConditionNode NewMemory() => new()
    {
        Kind = "MEMORY",
        MemoryKey = _tags.FirstOrDefault()?.Key,
        ActorRef = "ANY", AffectedRef = "ANY", KnowerRef = "ANY", MinimumCount = 1
    };

    private MemoryConditionNode NewStatus() => new()
    {
        Kind = "STATUS", StatusKey = _statuses.FirstOrDefault(x => x.Key == "ALLY")?.Key
            ?? _statuses.FirstOrDefault()?.Key,
        StatusDirection = "PARTNER_TO_ACTOR"
    };

    private bool ApplySelected()
    {
        try
        {
            if (_selected is null) throw new InvalidOperationException("Select a node first.");
            if (_selected.Kind == "MEMORY")
            {
                if (_tag.SelectedItem is not MemoryTypeDefinition tag)
                    throw new InvalidOperationException("Choose a memory type.");
                if (!int.TryParse(_minimum.Text.Trim(), out int min) || min < 0)
                    throw new InvalidOperationException("Minimum count must be a non-negative whole number.");
                int? max = null;
                if (!string.IsNullOrWhiteSpace(_maximum.Text))
                {
                    if (!int.TryParse(_maximum.Text.Trim(), out int entered) || entered < min)
                        throw new InvalidOperationException("Maximum must be a number at least as large as the minimum.");
                    max = entered;
                }
                _selected.MemoryKey = tag.Key;
                _selected.ActorRef = SelectedReference(_actor);
                _selected.AffectedRef = SelectedReference(_affected);
                _selected.KnowerRef = SelectedReference(_knower);
                _selected.MinimumCount = min;
                _selected.MaximumCount = max;
            }
            else if (_selected.Kind == "STATUS")
            {
                if (_status.SelectedItem is not RelationshipStatusDefinition status)
                    throw new InvalidOperationException("Choose a relationship status.");
                if (_statusDirection.SelectedItem is not DirectionChoice direction)
                    throw new InvalidOperationException("Choose a relationship direction.");
                _selected.StatusKey = status.Key;
                _selected.StatusDirection = direction.Key;
            }
            else
            {
                if (_operator.SelectedItem is not string op)
                    throw new InvalidOperationException("Choose a group operator.");
                if (op == "NOT" && _selected.Children.Count > 1)
                    throw new InvalidOperationException("NOT must have exactly one child. Remove the extra conditions first.");
                _selected.Kind = op;
            }
            _dirty = true;
            Refresh(_selected);
            return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }

    private void RemoveSelected()
    {
        if (_selected is null || _root is null) return;
        if (ReferenceEquals(_selected, _root)) { ClearAll(); return; }
        var parent = ParentOf(_root, _selected);
        if (parent is not null)
        {
            parent.Children.Remove(_selected);
            _dirty = true;
            Refresh(parent);
        }
    }

    private void ClearAll()
    {
        if (MessageBox.Show(this, "Remove all advanced requirements for this " + _subject + "? Changes are not saved until Save tree.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _root = null; _selected = null; _dirty = true; Refresh(null);
    }

    private void Save()
    {
        try
        {
            if (_selected is not null && !ApplySelected()) return;
            _saveConditions(_root);
            _dirty = false;
            MessageBox.Show(this, "Advanced requirements saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "Advanced condition editor", MessageBoxButton.OK, MessageBoxImage.Error);
}
