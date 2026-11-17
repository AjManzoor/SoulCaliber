using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Microsoft.Data.Sqlite;
using SoulCaliber.Dialogue;

/// <summary>
/// Prototype entry point for conversation selection. Choose a pair, inspect
/// eligible topics and launch the existing InteractionScene. The source .tscn is
/// intentionally minimal; all layout is built here, avoiding new art assets.
/// </summary>
public partial class ConversationSelectionScene : Control
{
	[Export] public string DefaultInitiatorId { get; set; } = "LAW";
	[Export] public string DefaultRecipientId { get; set; } = "YOURI";

	private readonly List<(string Id, string Name)> _characters = new();
	private readonly List<AvailableConversation> _entries = new();
	private OptionButton _initiator = null!;
	private OptionButton _recipient = null!;
	private VBoxContainer _entriesList = null!;
	private VBoxContainer _diagnosticsList = null!;
	private VBoxContainer _diagnosticsPane = null!;
	private Label _diagnosticsSummary = null!;
	private ConversationSelectionReport? _lastReport;
	private Label _hint = null!;
	private Control? _activeDialogue;
	private Control _menu = null!;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		BuildUi();
		try
		{
			LoadCharacters();
			Populate(_initiator, DefaultInitiatorId);
			Populate(_recipient, DefaultRecipientId);
			_initiator.ItemSelected += _ => Refresh();
			_recipient.ItemSelected += _ => Refresh();
			Refresh();
		}
		catch (Exception ex)
		{
			_hint.Text = "Could not load content: " + ex.Message;
			GD.PushError("[ConversationMenu] " + ex);
		}
	}

	private void BuildUi()
	{
		var background = new ColorRect { Color = new Color(0.065f, 0.056f, 0.085f) };
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(background);
		_menu = new MarginContainer();
		_menu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_menu.AddThemeConstantOverride("margin_left", 48);
		_menu.AddThemeConstantOverride("margin_right", 48);
		_menu.AddThemeConstantOverride("margin_top", 30);
		_menu.AddThemeConstantOverride("margin_bottom", 30);
		AddChild(_menu);
		var layout = new VBoxContainer();
		layout.AddThemeConstantOverride("separation", 18);
		_menu.AddChild(layout);
		var title = new Label { Text = "CONVERSATIONS", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		title.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.64f));
		layout.AddChild(title);

		var pair = new HBoxContainer();
		pair.AddThemeConstantOverride("separation", 20);
		layout.AddChild(pair);
		_initiator = AddCharacterSelector(pair, "Initiator");
		_recipient = AddCharacterSelector(pair, "Recipient");
		var refresh = new Button { Text = "Refresh", CustomMinimumSize = new Vector2(120, 45) };
		refresh.Pressed += Refresh;
		pair.AddChild(refresh);
		_hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_hint.AddThemeColorOverride("font_color", new Color(0.76f, 0.73f, 0.84f));
		layout.AddChild(_hint);
		var diagnosticToggle = new CheckButton
		{
			Text = "Show eligibility diagnostics (testing)", ButtonPressed = false,
			Visible = OS.IsDebugBuild()
		};
		layout.AddChild(diagnosticToggle);

		var panes = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		panes.AddThemeConstantOverride("separation", 24);
		layout.AddChild(panes);

		var availablePane = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		availablePane.AddThemeConstantOverride("separation", 10);
		availablePane.AddChild(new Label { Text = "AVAILABLE CONVERSATIONS" });
		panes.AddChild(availablePane);
		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		availablePane.AddChild(scroll);
		_entriesList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_entriesList.AddThemeConstantOverride("separation", 12);
		scroll.AddChild(_entriesList);

		_diagnosticsPane = new VBoxContainer
		{
			Visible = false,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		_diagnosticsPane.AddThemeConstantOverride("separation", 10);
		panes.AddChild(_diagnosticsPane);
		var diagnosticsHeader = new HBoxContainer();
		diagnosticsHeader.AddChild(new Label { Text = "ELIGIBILITY DIAGNOSTICS" });
		var copyButton = new Button { Text = "Copy report" };
		copyButton.Pressed += CopyDiagnostics;
		diagnosticsHeader.AddChild(copyButton);
		_diagnosticsPane.AddChild(diagnosticsHeader);
		_diagnosticsSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_diagnosticsPane.AddChild(_diagnosticsSummary);
		var diagnosticsScroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		_diagnosticsPane.AddChild(diagnosticsScroll);
		_diagnosticsList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_diagnosticsList.AddThemeConstantOverride("separation", 12);
		diagnosticsScroll.AddChild(_diagnosticsList);

		diagnosticToggle.Toggled += show => _diagnosticsPane.Visible = show;
	}

	private static OptionButton AddCharacterSelector(HBoxContainer parent, string label)
	{
		var section = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		section.AddChild(new Label { Text = label });
		var select = new OptionButton { CustomMinimumSize = new Vector2(220, 44) };
		section.AddChild(select);
		parent.AddChild(section);
		return select;
	}

	private void LoadCharacters()
	{
		using var connection = OpenContent();
		using var cmd = connection.CreateCommand();
		cmd.CommandText = "SELECT Id, Name FROM Characters ORDER BY Name COLLATE NOCASE";
		using var rows = cmd.ExecuteReader();
		while (rows.Read()) _characters.Add((rows.GetString(0), rows.GetString(1)));
	}

	private void Populate(OptionButton select, string preferred)
	{
		select.Clear();
		for (int i = 0; i < _characters.Count; i++)
		{
			select.AddItem(_characters[i].Name);
			if (string.Equals(_characters[i].Id, preferred, StringComparison.OrdinalIgnoreCase))
				select.Select(i);
		}
	}

	private string SelectedId(OptionButton control) => _characters[control.Selected].Id;

	private void Refresh()
	{
		if (_activeDialogue is not null) return;
		ClearList(_entriesList);
		ClearList(_diagnosticsList);
		_lastReport = null;
		_diagnosticsSummary.Text = "";
		_entries.Clear();
		if (_initiator.Selected < 0 || _recipient.Selected < 0) return;
		string actor = SelectedId(_initiator), recipient = SelectedId(_recipient);
		if (string.Equals(actor, recipient, StringComparison.OrdinalIgnoreCase))
		{
			_hint.Text = "Select two different characters.";
			return;
		}
		try
		{
			var statusStore = new SqliteRelationshipStatusStore();
			var memories = new SqliteMemoryStore(statusStore);
			var completions = new SqliteSpecialEncounterCompletionStore();
			var selector = new ConversationAvailabilitySelector(memories, statusStore, completions);
			var report = selector.Inspect(actor, recipient,
				message => GD.Print("[ConversationMenu] " + message));
			_lastReport = report;
			_entries.AddRange(report.Available);
			ShowDiagnostics(report);
			_hint.Text = _entries.Count == 0
				? "No conversations currently qualify. Use eligibility diagnostics to inspect the rules or check your published Studio content."
				: _entries[0].Category == "Mandatory"
					? "A mandatory encounter takes priority. Other conversations are unavailable."
					: "Select a conversation to begin. The list reflects your current saved relationships and memories.";
			foreach (var entry in _entries)
			{
				var button = new Button
				{
					Text = $"{entry.Title}   [{entry.Category}]",
					TooltipText = $"{entry.Key} → {entry.InteractionKey}",
					CustomMinimumSize = new Vector2(0, 62),
					SizeFlagsHorizontal = SizeFlags.ExpandFill
				};
				button.AddThemeFontSizeOverride("font_size", 19);
				button.Pressed += () => StartConversation(entry);
				_entriesList.AddChild(button);
			}
		}
		catch (Exception ex)
		{
			_hint.Text = "Could not evaluate conversations: " + ex.Message;
			GD.PushError("[ConversationMenu] " + ex);
		}
	}


	private static void ClearList(VBoxContainer list)
	{
		foreach (Node node in list.GetChildren())
		{
			list.RemoveChild(node);
			node.QueueFree();
		}
	}

	private void ShowDiagnostics(ConversationSelectionReport report)
	{
		int available = 0, excluded = 0, suppressed = 0;
		foreach (var item in report.Diagnostics)
		{
			if (item.Status == "Available") available++;
			else if (item.Status == "Suppressed") suppressed++;
			else excluded++;

			var card = new VBoxContainer();
			card.AddThemeConstantOverride("separation", 3);
			var status = new Label
			{
				Text = $"{item.Status.ToUpperInvariant()}  ·  {item.Category}  ·  {item.Title} [{item.Key}]",
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			status.AddThemeColorOverride("font_color", item.Status switch
			{
				"Available" => new Color(0.55f, 0.88f, 0.69f),
				"Suppressed" => new Color(0.96f, 0.77f, 0.49f),
				_ => new Color(0.95f, 0.57f, 0.57f)
			});
			card.AddChild(status);
			var explanation = new Label
			{
				Text = $"{item.InteractionKey} · {item.Explanation}",
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			explanation.AddThemeColorOverride("font_color", new Color(0.79f, 0.76f, 0.84f));
			card.AddChild(explanation);
			_diagnosticsList.AddChild(card);
			_diagnosticsList.AddChild(new HSeparator());
		}
		_diagnosticsSummary.Text = $"{available} offered · {excluded} ineligible · {suppressed} eligible but overridden";
		foreach (string warning in report.Warnings)
		{
			var line = new Label
			{
				Text = "NOTICE: " + warning,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			line.AddThemeColorOverride("font_color", new Color(0.96f, 0.77f, 0.49f));
			_diagnosticsList.AddChild(line);
		}
		if (report.Diagnostics.Count == 0 && report.Warnings.Count == 0)
			_diagnosticsList.AddChild(new Label { Text = "No authored conversation rules to inspect." });
	}

	private void CopyDiagnostics()
	{
		if (_lastReport is null) return;
		var lines = new List<string> { "SoulCaliber conversation eligibility diagnostics" };
		lines.AddRange(_lastReport.Diagnostics.Select(item =>
			$"{item.Status} | {item.Category} | {item.Key} | {item.InteractionKey} | {item.Explanation}"));
		lines.AddRange(_lastReport.Warnings.Select(item => "NOTICE | " + item));
		DisplayServer.ClipboardSet(string.Join("\n", lines));
	}

	private void StartConversation(AvailableConversation entry)
	{
		if (_activeDialogue is not null) return;
		try
		{
			var scene = InteractionLauncher.Create(SelectedId(_initiator), SelectedId(_recipient), entry.InteractionKey);
			if (entry.SpecialEncounter is not null)
				scene.ConfigureSelectedEncounter(entry.SpecialEncounter);
			scene.ReturnToConversationMenu = ReturnToMenu;
			scene.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_activeDialogue = scene;
			_menu.Visible = false;
			AddChild(scene);
			GD.Print($"[ConversationMenu] Selected {entry.Category}: {entry.Key} -> {entry.InteractionKey}");
		}
		catch (Exception ex)
		{
			_hint.Text = "Could not launch conversation: " + ex.Message;
			_menu.Visible = true;
			GD.PushError("[ConversationMenu] " + ex);
		}
	}

	private void ReturnToMenu()
	{
		if (_activeDialogue is not null)
		{
			RemoveChild(_activeDialogue);
			_activeDialogue.QueueFree();
			_activeDialogue = null;
		}
		_menu.Visible = true;
		Refresh();
	}

	private static SqliteConnection OpenContent()
	{
		var connection = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
			Pooling = false, ForeignKeys = true
		}.ToString());
		connection.Open();
		return connection;
	}
}
