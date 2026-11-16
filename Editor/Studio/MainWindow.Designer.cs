using System.Windows;
using System.Windows.Controls;
using static SoulCaliberStudio.DialogueDesignerStore;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private int _previousWorkspaceTab;
    private bool _revertingWorkspaceTab;
    private DialogueDesignerStore? _designer;
    private Interaction? _activeInteraction;
    private Node? _activeNode;
    private Option? _activeOption;
    private Line? _activeLine;
    private bool _designerLoading;
    private bool _designerDirty;

    private void InitDesigner(string path)
    {
        _designer?.Dispose();
        _designer = new DialogueDesignerStore(path, _editor!);
        _designerLoading = true;
        try
        {
            var characterIds = _designer.Characters();
            LineCharacterBox.ItemsSource = characterIds;
            LineVisitorBox.ItemsSource = new[] { "" }.Concat(characterIds).ToList();
            LoadReactionTypeChoices();
            _designerDirty = false;
        }
        finally { _designerLoading = false; }
        RefreshInteractions();
    }

    private void DesignerField_Changed(object sender, RoutedEventArgs e)
    {
        if (_designerLoading || _designer is null) return;
        _designerDirty = true;
        DesignerStatus.Text = "Unsaved form changes. Save this record before switching items.";
    }

    private bool DesignerCanSwitch()
    {
        if (!_designerDirty) return true;
        if (MessageBox.Show(this, "Discard unsaved changes to the selected form?", "Unsaved dialogue edits",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _designerDirty = false;
        return true;
    }
    private void RestoreSelection(ListBox list, object? oldSelection)
    {
        _designerLoading = true;
        try { list.SelectedItem = oldSelection; }
        finally { _designerLoading = false; }
    }
    private void RefreshInteractions(int? selectedId = null)
    {
        if (_designer is null) return;
        _designerLoading = true;
        try
        {
            var items = _designer.Interactions();
            InteractionList.ItemsSource = items;
            InteractionList.SelectedItem = items.FirstOrDefault(x => x.Id == selectedId) ?? items.FirstOrDefault();
            PopulateInteraction(InteractionList.SelectedItem as Interaction);
        }
        finally { _designerLoading = false; _designerDirty = false; }
    }
    private void PopulateInteraction(Interaction? interaction)
    {
        _activeInteraction = interaction;
        InteractionKeyBox.Text = interaction?.Key ?? "";
        InteractionTitleBox.Text = interaction?.Title ?? "";
        InteractionStartBox.Text = interaction?.StartNodeKey ?? "";
        InteractionKeyBox.IsReadOnly = interaction is not null;
        RefreshNodes();
    }
    private void RefreshNodes(int? selectedId = null)
    {
        var nodes = _designer is not null && _activeInteraction is not null
            ? _designer.Nodes(_activeInteraction.Id) : new List<Node>();
        NodeList.ItemsSource = nodes;
        OptionSuccessNextBox.ItemsSource = new[] { "" }.Concat(nodes.Select(n => n.Key)).ToArray();
        OptionFailureNextBox.ItemsSource = new[] { "" }.Concat(nodes.Select(n => n.Key)).ToArray();
        NodeList.SelectedItem = nodes.FirstOrDefault(x => x.Id == selectedId) ?? nodes.FirstOrDefault();
        PopulateNode(NodeList.SelectedItem as Node);
    }
    private void PopulateNode(Node? node)
    {
        _activeNode = node;
        NodeKeyBox.Text = node?.Key ?? "";
        NodeKeyBox.IsReadOnly = node is not null;
        NodeRoundBox.Text = (node?.Round ?? 1).ToString();
        NodeOrderBox.Text = (node?.SortOrder ?? 0).ToString();
        NodeIntroBox.Text = node?.HostIntroduction ?? "";
        RefreshOptions();
    }
    private void RefreshOptions(int? selectedId = null)
    {
        var options = _designer is not null && _activeNode is not null
            ? _designer.Options(_activeNode.Id) : new List<Option>();
        OptionList.ItemsSource = options;
        OptionList.SelectedItem = options.FirstOrDefault(x => x.Id == selectedId) ?? options.FirstOrDefault();
        PopulateOption(OptionList.SelectedItem as Option);
    }
    private void PopulateOption(Option? option)
    {
        _activeOption = option;
        OptionKeyBox.Text = option?.Key ?? "";
        OptionKeyBox.IsReadOnly = option is not null;
        OptionLabelBox.Text = option?.Label ?? "";
        OptionOrderBox.Text = (option?.SortOrder ?? 0).ToString();
        OptionChanceBox.Text = (option?.BaseSuccessChance ?? 50).ToString();
        OptionSuccessNextBox.Text = option?.SuccessNextNodeKey ?? "";
        OptionFailureNextBox.Text = option?.FailureNextNodeKey ?? "";
        OptionPlayerLineBox.Text = option?.DefaultPlayerLine ?? "";
        OptionHostSuccessBox.Text = option?.DefaultHostSuccessLine ?? "";
        OptionHostFailureBox.Text = option?.DefaultHostFailureLine ?? "";
        OptionSuccessReactionBox.SelectedItem = option?.SuccessTargetReaction ?? "RECEPTIVE";
        OptionFailureReactionBox.SelectedItem = option?.FailureTargetReaction ?? "DISMISSIVE";
        RefreshLines();
        MechanicsSummaryText.Text = option is null || _designer is null
            ? "Save a choice to configure its mechanics."
            : _designer.ChoiceMechanicsSummary(option.Id);
    }
    private void RefreshLines(int? selectedId = null)
    {
        var lines = _designer is not null && _activeOption is not null
            ? _designer.Lines(_activeOption.Id) : new List<Line>();
        LineList.ItemsSource = lines;
        LineList.SelectedItem = lines.FirstOrDefault(x => x.Id == selectedId) ?? lines.FirstOrDefault();
        PopulateLine(LineList.SelectedItem as Line);
    }
    private void PopulateLine(Line? line)
    {
        _activeLine = line;
        // A saved phrase has a fixed identity: speaker + other character + outcome.
        // To author another character's response, use New phrase (INSERT), not Save (UPDATE).
        LineCharacterBox.IsEnabled = line is null;
        LineVisitorBox.IsEnabled = line is null;
        LineOutcomeBox.IsEnabled = line is null;
        LineCharacterBox.SelectedItem = line?.CharacterId;
        LineVisitorBox.SelectedItem = line?.VisitorId ?? "";
        LineOutcomeBox.SelectedIndex = line?.Outcome switch { "SUCCESS" => 1, "FAILURE" => 2, _ => 0 };
        LineTextBox.Text = line?.Text ?? "";
        LineImageBox.Text = line?.ImagePath ?? "";
    }

    private void InteractionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_designerLoading || _designer is null) return;
        if (!DesignerCanSwitch()) { RestoreSelection(InteractionList, _activeInteraction); return; }
        _designerLoading = true;
        try { PopulateInteraction(InteractionList.SelectedItem as Interaction); }
        finally { _designerLoading = false; }
    }
    private void NodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_designerLoading || _designer is null) return;
        if (!DesignerCanSwitch()) { RestoreSelection(NodeList, _activeNode); return; }
        _designerLoading = true;
        try { PopulateNode(NodeList.SelectedItem as Node); }
        finally { _designerLoading = false; }
    }
    private void OptionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_designerLoading || _designer is null) return;
        if (!DesignerCanSwitch()) { RestoreSelection(OptionList, _activeOption); return; }
        _designerLoading = true;
        try { PopulateOption(OptionList.SelectedItem as Option); }
        finally { _designerLoading = false; }
    }
    private void LineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_designerLoading || _designer is null) return;
        if (!DesignerCanSwitch()) { RestoreSelection(LineList, _activeLine); return; }
        _designerLoading = true;
        try { PopulateLine(LineList.SelectedItem as Line); }
        finally { _designerLoading = false; }
    }

    private void NewInteraction_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || !DesignerCanSwitch()) return;
        _designerLoading = true;
        try
        {
            InteractionList.SelectedItem = null;
            PopulateInteraction(null);
            DesignerStatus.Text = "New interaction: enter key, title and host, then Save. Add nodes before setting its start node.";
        }
        finally { _designerLoading = false; }
    }
    private void NewNode_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null || !DesignerCanSwitch()) return;
        _designerLoading = true;
        try
        {
            NodeList.SelectedItem = null;
            PopulateNode(null);
            DesignerStatus.Text = "New node: enter a unique key within this interaction, then Save.";
        }
        finally { _designerLoading = false; }
    }
    private void NewOption_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeNode is null || !DesignerCanSwitch()) return;
        _designerLoading = true;
        try
        {
            OptionList.SelectedItem = null;
            PopulateOption(null);
            DesignerStatus.Text = "New choice: enter its key, label and generic dialogue, then Save.";
        }
        finally { _designerLoading = false; }
    }
    private void NewLine_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeOption is null || !DesignerCanSwitch()) return;
        _designerLoading = true;
        try
        {
            LineList.SelectedItem = null;
            PopulateLine(null);
            DesignerStatus.Text = "New character-specific phrase: choose a speaker, optional partner/outcome, then Save.";
        }
        finally { _designerLoading = false; }
    }
    private void RunDesignerAction(Action action)
    {
        try
        {
            action();
            _designerDirty = false;
            DesignerStatus.Text = "Saved successfully. No automatic backup was created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dialogue editor", MessageBoxButton.OK, MessageBoxImage.Error);
            DesignerStatus.Text = "Operation failed. The database change was rolled back.";
        }
    }
    private static int Number(TextBox box, string name)
    {
        if (!int.TryParse(box.Text, out int number)) throw new InvalidOperationException($"{name} must be a whole number.");
        return number;
    }
    private void SaveInteraction_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null) return;
        RunDesignerAction(() =>
        {
            int id = _designer.SaveInteraction(new(_activeInteraction?.Id ?? 0,
                InteractionKeyBox.Text, InteractionTitleBox.Text, InteractionStartBox.Text));
            RefreshInteractions(id);
        });
    }
    private void SaveNode_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null) return;
        RunDesignerAction(() =>
        {
            int id = _designer.SaveNode(new(_activeNode?.Id ?? 0, _activeInteraction.Id, NodeKeyBox.Text,
                Number(NodeRoundBox, "Round"), Number(NodeOrderBox, "Sort order"), NodeIntroBox.Text));
            _designerLoading = true;
            try { RefreshNodes(id); }
            finally { _designerLoading = false; }
        });
    }
    private void SaveOption_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null || _activeNode is null) return;
        RunDesignerAction(() =>
        {
            int id = _designer.SaveOption(new(_activeOption?.Id ?? 0, _activeNode.Id, OptionKeyBox.Text,
                OptionLabelBox.Text, Number(OptionOrderBox, "Sort order"), Number(OptionChanceBox, "Base chance"),
                OptionSuccessNextBox.Text, OptionFailureNextBox.Text, OptionPlayerLineBox.Text,
                OptionHostSuccessBox.Text, OptionHostFailureBox.Text,
                OptionSuccessReactionBox.SelectedItem as string ?? "",
                OptionFailureReactionBox.SelectedItem as string ?? ""), _activeInteraction.Id);
            _designerLoading = true;
            try { RefreshOptions(id); }
            finally { _designerLoading = false; }
        });
    }
    private void SaveLine_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeOption is null) return;
        RunDesignerAction(() =>
        {
            var outcome = (LineOutcomeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            int id = _designer.SaveLine(new(_activeLine?.Id ?? 0, _activeOption.Id,
                LineCharacterBox.SelectedItem as string ?? "", LineVisitorBox.SelectedItem as string,
                outcome == "ANY" ? null : outcome, LineTextBox.Text, LineImageBox.Text));
            _designerLoading = true;
            try { RefreshLines(id); }
            finally { _designerLoading = false; }
        });
    }
    private bool ConfirmDelete(string label) => MessageBox.Show(this,
        $"Permanently delete {label} from the master content database?\n\nNo automatic backup will be created.",
        "Confirm content deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private void DeleteInteraction_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null || !ConfirmDelete("interaction " + _activeInteraction.Key)) return;
        RunDesignerAction(() => { _designer.DeleteInteraction(_activeInteraction.Id); RefreshInteractions(); });
    }
    private void DeleteNode_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeNode is null || _activeInteraction is null || !ConfirmDelete("node " + _activeNode.Key)) return;
        RunDesignerAction(() => { _designer.DeleteNode(_activeNode.Id, _activeInteraction.Id, _activeNode.Key); _designerLoading = true; try { RefreshNodes(); } finally { _designerLoading = false; } });
    }
    private void DeleteOption_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeOption is null || !ConfirmDelete("choice " + _activeOption.Key)) return;
        RunDesignerAction(() => { _designer.DeleteOption(_activeOption.Id); _designerLoading = true; try { RefreshOptions(); } finally { _designerLoading = false; } });
    }
    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeLine is null || !ConfirmDelete("selected phrase")) return;
        RunDesignerAction(() => { _designer.DeleteLine(_activeLine.Id); _designerLoading = true; try { RefreshLines(); } finally { _designerLoading = false; } });
    }
    private void ValidateInteraction_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null) return;
        try { MessageBox.Show(this, _designer.ValidateInteraction(_activeInteraction.Id), "Dialogue structure check"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Validation error"); }
    }
    private void EditChoiceMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeInteraction is null || _activeNode is null || _activeOption is null)
        {
            MessageBox.Show(this, "Save and select an existing choice first.", "Choice mechanics");
            return;
        }
        if (_designerDirty)
        {
            MessageBox.Show(this, "Save your choice and dialogue changes before editing relationship mechanics.",
                "Unsaved dialogue changes");
            return;
        }
        try
        {
            var window = new ChoiceMechanicsWindow(_designer, _activeInteraction, _activeNode, _activeOption)
            { Owner = this };
            window.ShowDialog();
            MechanicsSummaryText.Text = _designer.ChoiceMechanicsSummary(_activeOption.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to open choice mechanics",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditIntroductions_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeNode is null || _activeInteraction is null) return;
        if (_designerDirty)
        {
            MessageBox.Show(this, "Save your current dialogue form edits before opening the introduction editor.", "Unsaved changes");
            return;
        }
        var window = new IntroOverridesWindow(_designer, _activeNode, defaultHostId: null) { Owner = this };
        window.ShowDialog();
        // The modal saves to the same master database; the active node and choice remain selected.
    }

    private void PreviewDialogue_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || _activeNode is null || _activeOption is null) return;
        if (_designerDirty)
        {
            MessageBox.Show(this, "Save your current dialogue form edits before previewing saved dialogue.", "Unsaved changes");
            return;
        }
        var previewCharacters = _designer.Characters();
        var window = new DialoguePreviewWindow(_designer, _activeNode, _activeOption,
            previewCharacters.FirstOrDefault(), previewCharacters.Skip(1).FirstOrDefault()) { Owner = this };
        window.ShowDialog();
    }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != WorkspaceTabs || _revertingWorkspaceTab) return;
        int next = WorkspaceTabs.SelectedIndex;
        if (_editor is null) { _previousWorkspaceTab = next; return; }
        if (next == _previousWorkspaceTab) return;
        bool canLeave = _previousWorkspaceTab switch
        {
            0 => DesignerCanSwitch(),
            1 => CharactersCanSwitch(),
            2 => CanLeaveTable(),
            _ => true
        };
        if (!canLeave)
        {
            _revertingWorkspaceTab = true;
            try { WorkspaceTabs.SelectedIndex = _previousWorkspaceTab; }
            finally { _revertingWorkspaceTab = false; }
            return;
        }
        // Rehydrate the panel being left, so a user-approved discard really discards.
        if (_previousWorkspaceTab == 2 && _currentTableName is not null && IsDirty())
            LoadTable(_currentTableName);
        _previousWorkspaceTab = next;
        if (next == 0 && _designer is not null)
            RefreshInteractions(_activeInteraction?.Id);
        if (next == 1 && _characterStore is not null)
            RefreshCharacters();
        if (next == 2 && _currentTableName is not null)
            LoadTable(_currentTableName);
    }
}
