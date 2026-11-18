using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

/// <summary>Three independent audience sections. All sections are optional.
/// The preview uses authored starting statuses, not the runtime savegame.</summary>
public sealed class KnowledgeAudienceEditorWindow : Window
{
    private readonly DialogueDesignerStore _store;
    private readonly KnowledgeAudienceSpec _draft;
    private readonly Dictionary<string, CheckBox> _actorStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _affectedStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _other = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _includeActor = new() { Content = "The Actor knows", Foreground = Brushes.White };
    private readonly CheckBox _includeAffected = new() { Content = "Each Affected character knows", Foreground = Brushes.White };
    private readonly CheckBox _excludeActor = new() { Content = "Exclude the Actor (even if another rule includes them)", Foreground = Brushes.White };
    private readonly CheckBox _excludeAffected = new() { Content = "Exclude all Affected characters (even if another rule includes them)", Foreground = Brushes.White };
    private readonly ComboBox _previewActor = new() { MinWidth = 175 };
    private readonly ComboBox _previewAffected = new() { MinWidth = 175 };
    public KnowledgeAudienceSpec? Result { get; private set; }

    public KnowledgeAudienceEditorWindow(DialogueDesignerStore store, KnowledgeAudienceSpec? existing)
    {
        _store = store;
        _draft = existing is null ? new KnowledgeAudienceSpec() : KnowledgeAudienceSpec.Parse(existing.ToJson());
        Title = "Custom memory knowledge audiences";
        Width = 930; Height = 800; MinWidth = 680; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(21, 32, 51));
        Foreground = Brushes.White;
        var dock = new DockPanel { Margin = new Thickness(18) };
        Content = dock;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        dock.Children.Add(actions);
        AddButton(actions, "Preview recipients", Preview);
        AddButton(actions, "Cancel", Close);
        AddButton(actions, "Use these audiences", () => { Result = Gather(); DialogResult = true; });
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(scroll);
        var body = new StackPanel { Margin = new Thickness(4, 0, 12, 14) };
        scroll.Content = body;
        AddHeading(body, "KNOWLEDGE AUDIENCES", 22);
        AddNote(body, "Groups use outgoing directional statuses: Actor → other, or Affected → other. All choices are independent; explicit exclusions take precedence. Knowledge is assigned once when a memory occurs.");
        AddHeading(body, "1. Actor's side", 17);
        _includeActor.IsChecked = _draft.IncludeActor;
        body.Children.Add(_includeActor);
        AddNote(body, "Which of the Actor's relationship groups know?");
        AddStatusChoices(body, _actorStatuses, _draft.ActorStatusKeys);
        AddHeading(body, "2. Affected side", 17);
        _includeAffected.IsChecked = _draft.IncludeAffected;
        body.Children.Add(_includeAffected);
        AddNote(body, "For multiple affected characters, the selected groups of EACH affected character are combined.");
        AddStatusChoices(body, _affectedStatuses, _draft.AffectedStatusKeys);
        AddHeading(body, "3. Other characters", 17);
        AddNote(body, "Include specific witnesses or outsiders, regardless of either side's relationship statuses.");
        AddCharacters(body, _other, _draft.OtherCharacterIds);
        AddHeading(body, "4. Explicit exclusions", 17);
        _excludeActor.IsChecked = _draft.ExcludeActor;
        _excludeAffected.IsChecked = _draft.ExcludeAffected;
        body.Children.Add(_excludeActor);
        body.Children.Add(_excludeAffected);
        AddNote(body, "Exclude individual characters even if another section includes them.");
        AddCharacters(body, _excluded, _draft.ExcludedCharacterIds);
        AddHeading(body, "Preview with starting relationships", 17);
        AddNote(body, "Select an example Actor and Affected character. This preview uses Studio starting statuses, not the changing statuses in a gameplay save.");
        var previewRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        body.Children.Add(previewRow);
        previewRow.Children.Add(new TextBlock { Text = "Actor: ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        previewRow.Children.Add(_previewActor);
        previewRow.Children.Add(new TextBlock { Text = "  Affected: ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        previewRow.Children.Add(_previewAffected);
        var characters = _store.StatusCharacters().ToArray();
        _previewActor.ItemsSource = characters;
        _previewActor.DisplayMemberPath = "Name";
        _previewAffected.ItemsSource = characters;
        _previewAffected.DisplayMemberPath = "Name";
        if (characters.Length > 0) _previewActor.SelectedIndex = 0;
        if (characters.Length > 1) _previewAffected.SelectedIndex = 1;
    }

    private void AddStatusChoices(Panel body, Dictionary<string, CheckBox> checks, IEnumerable<string> selected)
    {
        var chosen = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        body.Children.Add(wrap);
        foreach (var status in _store.StatusDefinitions(activeOnly: true))
        {
            var check = new CheckBox { Content = $"{status.Name} ({status.Key})", Tag = status.Key,
                IsChecked = chosen.Contains(status.Key), Foreground = Brushes.White, MinWidth = 190, Margin = new Thickness(0, 4, 12, 8) };
            checks.Add(status.Key, check);
            wrap.Children.Add(check);
        }
    }

    private void AddCharacters(Panel body, Dictionary<string, CheckBox> checks, IEnumerable<string> selected)
    {
        var chosen = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 12) };
        body.Children.Add(wrap);
        foreach (var character in _store.StatusCharacters())
        {
            var check = new CheckBox { Content = character.Name, Tag = character.Id,
                IsChecked = chosen.Contains(character.Id), Foreground = Brushes.White, MinWidth = 155, Margin = new Thickness(0, 3, 10, 8) };
            checks.Add(character.Id, check);
            wrap.Children.Add(check);
        }
    }

    private KnowledgeAudienceSpec Gather() => new()
    {
        IncludeActor = _includeActor.IsChecked == true,
        IncludeAffected = _includeAffected.IsChecked == true,
        ExcludeActor = _excludeActor.IsChecked == true,
        ExcludeAffected = _excludeAffected.IsChecked == true,
        ActorStatusKeys = Checked(_actorStatuses),
        AffectedStatusKeys = Checked(_affectedStatuses),
        OtherCharacterIds = Checked(_other),
        ExcludedCharacterIds = Checked(_excluded)
    };
    private static List<string> Checked(Dictionary<string, CheckBox> list) =>
        list.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToList();

    private void Preview()
    {
        if (_previewActor.SelectedItem is not StatusCharacter actor ||
            _previewAffected.SelectedItem is not StatusCharacter affected)
        {
            MessageBox.Show(this, "Select an Actor and an Affected character to preview.", "Preview");
            return;
        }
        try
        {
            var knowers = _store.PreviewAudience(Gather(), actor.Id, new[] { affected.Id });
            MessageBox.Show(this, knowers.Count == 0 ? "Nobody initially knows this memory." :
                string.Join(Environment.NewLine, knowers.Select(c => $"• {c.Name} ({c.Id})")),
                $"Initial knowers ({knowers.Count})", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Preview error", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void AddHeading(Panel panel, string text, double size) =>
        panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold,
            FontSize = size, Margin = new Thickness(0, 10, 0, 7), Foreground = Brushes.White });
    private static void AddNote(Panel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 3, 0, 7) });
    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(5, 8, 0, 2),
            Padding = new Thickness(12, 7, 12, 7) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
}
