using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SoulCaliber.Dialogue;

/// <summary>
/// Universal two-character dialogue scene. Keep your existing .tscn and Inspector
/// portraits: dynamic wrapping relationship panels are created at the top in code.
/// </summary>
public partial class InteractionScene : Control
{
	[Export] public string VisitorCharacterId { get; set; } = "ARK";
	[Export] public string PartnerCharacterId { get; set; } = ""; // blank = interaction default
	[Export] public string InteractionKey { get; set; } = "ROYAL_AUDIENCE";

	private enum DisplayStage { Choices, PlayerSpeaking, HostSpeaking, Finished }

	private DialogueEngine _engine = null!;
	private DialogueResolution? _lastResult;
	private readonly SqliteInteractionHistoryStore _historyStore = new();
	private DisplayStage _stage;
	private Texture2D? _defaultLeftTexture;
	private Texture2D? _defaultRightTexture;
	private Label _leftOutcomeBadge = null!;
	private Label _rightOutcomeBadge = null!;

	private Label _speakerLabel = null!;
	private Label _dialogueLabel = null!;
	private Label _leftName = null!;
	private Label _rightName = null!;
	private Label _leftPlaceholder = null!;
	private Label _rightPlaceholder = null!;
	private Label _sceneTitle = null!;
	private Label _footer = null!;
	private Panel _leftFrame = null!;
	private Panel _rightFrame = null!;
	private TextureRect _leftPortrait = null!;
	private TextureRect _rightPortrait = null!;
	private ScrollContainer _choicesScroll = null!;
	private VBoxContainer _choicesContainer = null!;
	private Button _continueButton = null!;

	private Label _leftStatsTitle = null!;
	private Label _rightStatsTitle = null!;
	private readonly Dictionary<string, Label> _leftStatLabels = new();
	private readonly Dictionary<string, Label> _rightStatLabels = new();
	private readonly Dictionary<string, Control> _leftStatChips = new();
	private readonly Dictionary<string, Control> _rightStatChips = new();

	public override void _Ready()
	{
		_speakerLabel = GetNode<Label>("SpeakerLabel");
		_dialogueLabel = GetNode<Label>("DialoguePanel/DialogueLabel");
		_choicesScroll = GetNode<ScrollContainer>("ChoicesScroll");
		_choicesContainer = GetNode<VBoxContainer>("ChoicesScroll/Choices");
		_continueButton = GetNode<Button>("ContinueButton");
		_leftFrame = GetNode<Panel>("LeftFrame");
		_rightFrame = GetNode<Panel>("RightFrame");
		_leftPortrait = GetNode<TextureRect>("LeftFrame/LeftPortrait");
		_rightPortrait = GetNode<TextureRect>("RightFrame/RightPortrait");
		_rightPortrait.FlipH = true; // preserve the existing right-side mirror presentation
		_leftPlaceholder = GetNode<Label>("LeftFrame/LeftPlaceholder");
		_rightPlaceholder = GetNode<Label>("RightFrame/RightPlaceholder");
		_leftName = GetNode<Label>("LeftName");
		_rightName = GetNode<Label>("RightName");
		_sceneTitle = GetNode<Label>("SceneTitle");
		_footer = GetNode<Label>("Footer");

		_engine = new DialogueEngine(new DialogueRepository(), new SqliteRelationshipStore(),
			InteractionKey, VisitorCharacterId, PartnerCharacterId);

		// Respect existing Inspector textures if no data image is present.
		_defaultLeftTexture = LoadImage(_engine.Visitor.DefaultImage) ?? _leftPortrait.Texture;
		_defaultRightTexture = LoadImage(_engine.Host.DefaultImage) ?? _rightPortrait.Texture;
		_leftName.Text = _engine.Visitor.Name;
		_rightName.Text = _engine.Host.Name;

		BuildRelationshipPanels();
		_leftOutcomeBadge = CreateOutcomeBadge(_leftFrame, "LeftOutcomeBadge");
		_rightOutcomeBadge = CreateOutcomeBadge(_rightFrame, "RightOutcomeBadge");
		_continueButton.Pressed += ContinueConversation;
		RestartConversation();
	}

	private void RestartConversation()
	{
		_engine.Restart();
		_leftPortrait.Texture = _defaultLeftTexture;
		_rightPortrait.Texture = _defaultRightTexture;
		_leftPortrait.Modulate = Colors.White;
		_rightPortrait.Modulate = Colors.White;
		_leftOutcomeBadge.Visible = false;
		_rightOutcomeBadge.Visible = false;
		_lastResult = null;
		RefreshPlaceholders();
		ShowCurrentNode();
	}

	private void ShowCurrentNode()
	{
		_stage = DisplayStage.Choices;
		_lastResult = null;
		_sceneTitle.Text = $"{_engine.Definition.Title.ToUpperInvariant()}  •  ROUND {_engine.CurrentNode.Round}/{_engine.Definition.Nodes.Max(n => n.Round)}";
		Speak(_engine.Host.Id, _engine.Introduction());
		_choicesScroll.Visible = true;
		_choicesScroll.ScrollVertical = 0;
		_continueButton.Visible = false;

		foreach (Node child in _choicesContainer.GetChildren())
		{
			_choicesContainer.RemoveChild(child);
			child.QueueFree();
		}

		int index = 0;
		foreach (AvailableOption choice in _engine.GetOptions())
		{
			index++;
			string key = choice.Definition.Key;
			var button = new Button
			{
				Name = $"Choice{index}",
				Text = choice.IsLocked
					? $"{index}. {choice.Definition.Label}  [LOCKED: {choice.LockReason}]"
					: $"{index}. {choice.Definition.Label}  ({choice.Chance}%" +
						(string.IsNullOrEmpty(_engine.SkillLabel(choice.Definition))
							? "" : $" · {_engine.SkillLabel(choice.Definition)}") + ")",
				TooltipText = _engine.ChanceBreakdown(choice.Definition),
				Disabled = choice.IsLocked,
				CustomMinimumSize = new Vector2(0, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			button.AddThemeFontSizeOverride("font_size", 17);
			button.Pressed += () => Choose(key);
			_choicesContainer.AddChild(button);
		}

		UpdateRelationshipPanels();
		_footer.Text = "Relationship states are directional  •  choose an action";
	}

	private void Choose(string optionKey)
	{
		if (_stage != DisplayStage.Choices) return;
		_lastResult = _engine.Choose(optionKey);
		_historyStore.Record(_engine, _lastResult);
		_stage = DisplayStage.PlayerSpeaking;
		_choicesScroll.Visible = false;
		_continueButton.Visible = true;
		_continueButton.Text = "Continue";

		// Only the ACTOR rolls. The target reacts; they are never a second
		// success/failure roll, nor the inverse winner/loser of the actor.
		SetActorOutcomePortrait(_leftPortrait, _leftOutcomeBadge,
			_engine.Visitor, _lastResult.PlayerLine,
			_lastResult.Succeeded, _defaultLeftTexture);
		SetTargetReactionPortrait(_rightPortrait, _rightOutcomeBadge,
			_engine.Host, _lastResult.HostLine,
			_lastResult.Succeeded, _lastResult.TargetReaction,
			_defaultRightTexture);

		Speak(_engine.Visitor.Id, _lastResult.PlayerLine.Text);
		UpdateRelationshipPanels(_lastResult.Changes);
		string skill = _engine.SkillLabel(_lastResult.Choice);
		_footer.Text = $"Chance {_lastResult.Chance}%" +
			(string.IsNullOrEmpty(skill) ? "" : $" ({skill})") +
			$"  •  Rolled {_lastResult.Roll}/100  •  " +
			$"{_engine.Visitor.Name}: " + (_lastResult.Succeeded ? "Success" : "Failure") +
			$"  •  {_engine.Host.Name}: {ReactionLabel(_lastResult.TargetReaction)}" +
			$"  •  {_lastResult.Changes.Count} stat change(s)";
	}

	private void ContinueConversation()
	{
		if (_stage == DisplayStage.PlayerSpeaking && _lastResult is not null)
		{
			_stage = DisplayStage.HostSpeaking;
			Speak(_engine.Host.Id, _lastResult.HostLine.Text);
			// Both outcome portraits stay visible for the whole round.
			_continueButton.Text = string.IsNullOrWhiteSpace(_lastResult.NextNodeKey)
				? "Finish conversation" : "Next round";
		}
		else if (_stage == DisplayStage.HostSpeaking && _lastResult is not null)
		{
			if (_engine.Advance(_lastResult.NextNodeKey))
				ShowCurrentNode();
			else
				EndConversation();
		}
		else if (_stage == DisplayStage.Finished)
		{
			RestartConversation();
		}
	}

	private void EndConversation()
	{
		_stage = DisplayStage.Finished;
		_choicesScroll.Visible = false;
		_continueButton.Visible = true;
		_continueButton.Text = "Start another conversation";
		_footer.Text = "Conversation complete  •  relationship changes saved";
		UpdateRelationshipPanels();
	}

	private void Speak(string speakerId, string text)
	{
		bool actorSpeaks = string.Equals(speakerId, _engine.Visitor.Id, StringComparison.OrdinalIgnoreCase);
		_speakerLabel.Text = actorSpeaks ? _engine.Visitor.Name : _engine.Host.Name;
		_dialogueLabel.Text = text;
		_leftName.Modulate = actorSpeaks ? Colors.White : new Color(0.65f, 0.65f, 0.72f);
		_rightName.Modulate = actorSpeaks ? new Color(0.65f, 0.65f, 0.72f) : Colors.White;
	}

	private static Label CreateOutcomeBadge(Panel portraitFrame, string name)
	{
		// Visible even when Win/Lose art has not been configured yet.
		var badge = new Label
		{
			Name = name,
			Visible = false,
			MouseFilter = MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			AnchorLeft = 0f,
			AnchorRight = 1f,
			AnchorTop = 1f,
			AnchorBottom = 1f,
			OffsetLeft = 12f,
			OffsetRight = -12f,
			OffsetTop = -48f,
			OffsetBottom = -12f
		};
		badge.AddThemeFontSizeOverride("font_size", 20);
		badge.AddThemeColorOverride("font_shadow_color", Colors.Black);
		badge.AddThemeConstantOverride("shadow_offset_x", 1);
		badge.AddThemeConstantOverride("shadow_offset_y", 2);
		portraitFrame.AddChild(badge);
		return badge;
	}

	private void SetActorOutcomePortrait(TextureRect target, Label badge,
		CharacterDefinition character, CharacterLine line, bool won, Texture2D? defaultTexture)
	{
		// The order matters: (1) per-choice/per-result art from lines.json,
		// (2) generic character WinImage/LoseImage, (3) generic dialogue art,
		// (4) original portrait. No files are invented; missing paths are skipped.
		string outcome = won ? "SUCCESS" : "FAILURE";
		bool hasSpecificLine = string.Equals(line.Outcome, outcome,
			StringComparison.OrdinalIgnoreCase);
		string genericOutcome = won ? character.WinImage : character.LoseImage;

		Texture2D? texture = null;
		if (hasSpecificLine)
			texture = LoadImage(line.ImagePath);
		texture ??= LoadImage(genericOutcome);
		if (!hasSpecificLine)
			texture ??= LoadImage(line.ImagePath);

		target.Texture = texture ?? defaultTexture;
		// Also communicate the result if matching art hasn't yet been added.
		target.Modulate = won
			? new Color(0.91f, 1f, 0.91f)
			: new Color(1f, 0.89f, 0.89f);
		badge.Text = won ? "SUCCESS" : "FAILED";
		badge.AddThemeColorOverride("font_color", won
			? new Color(0.57f, 1f, 0.65f)
			: new Color(1f, 0.65f, 0.60f));
		badge.Visible = true;
		RefreshPlaceholders();
	}

	private void SetTargetReactionPortrait(TextureRect target, Label badge,
		CharacterDefinition character, CharacterLine line, bool actorSucceeded,
		string reaction, Texture2D? defaultTexture)
	{
		// Priority: pair/option/outcome-specific dialogue image, then character's
		// named reaction art, then neutral dialogue art, then their default portrait.
		// We intentionally NEVER use the target's WinImage or LoseImage.
		string outcome = actorSucceeded ? "SUCCESS" : "FAILURE";
		bool hasSpecificLine = string.Equals(line.Outcome, outcome,
			StringComparison.OrdinalIgnoreCase);

		Texture2D? texture = null;
		if (hasSpecificLine)
			texture = LoadImage(line.ImagePath);
		texture ??= LoadImage(character.ReactionImage(reaction));
		if (!hasSpecificLine)
			texture ??= LoadImage(line.ImagePath);
		target.Texture = texture ?? defaultTexture;
		target.Modulate = Colors.White; // No red/green win/lose colouring on target.

		badge.Text = ReactionLabel(reaction);
		badge.AddThemeColorOverride("font_color", new Color(0.91f, 0.79f, 1f));
		badge.Visible = true;
		RefreshPlaceholders();
	}

	private static string ReactionLabel(string reaction) =>
		string.IsNullOrWhiteSpace(reaction) ? "REACTING" :
		reaction.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();

	private void BuildRelationshipPanels()
	{
		// Responsive icon panels sit above the portraits; they wrap onto additional
		// rows rather than requiring horizontal scrolling.
		_leftStatsTitle = AddStatsStrip("ActorRelationships", 0.025f, 0.26f,
			_leftStatLabels, _leftStatChips);
		_rightStatsTitle = AddStatsStrip("PartnerRelationships", 0.74f, 0.975f,
			_rightStatLabels, _rightStatChips);
	}

	private Label AddStatsStrip(string name, float left, float right,
		Dictionary<string, Label> values, Dictionary<string, Control> chips)
	{
		// Flow layout displays every registered attribute; no horizontal scrolling.
		// Compact chips fit five across on the standard desktop layout, wrapping
		// automatically when the panel is narrower or attributes are added.
		var panel = new Panel { Name = name, MouseFilter = MouseFilterEnum.Pass };
		AddChild(panel);
		panel.AnchorLeft = left;
		panel.AnchorRight = right;
		panel.AnchorTop = 0.008f;
		panel.AnchorBottom = 0.119f;
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.11f,0.095f,0.15f,0.98f),
			BorderColor = new Color(0.42f,0.34f,0.53f)
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(7);
		panel.AddThemeStyleboxOverride("panel",style);

		var margin = new MarginContainer();
		panel.AddChild(margin);
		margin.AnchorRight=1;
		margin.AnchorBottom=1;
		margin.OffsetLeft=5;
		margin.OffsetRight=-5;
		margin.OffsetTop=2;
		margin.OffsetBottom=-2;
		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation",2);
		margin.AddChild(stack);
		var title = new Label { HorizontalAlignment=HorizontalAlignment.Center, ClipText=true };
		title.AddThemeFontSizeOverride("font_size",12);
		title.AddThemeColorOverride("font_color",new Color(0.92f,0.81f,0.66f));
		stack.AddChild(title);

		var flow = new HFlowContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Pass
		};
		flow.AddThemeConstantOverride("h_separation", 3);
		flow.AddThemeConstantOverride("v_separation", 3);
		stack.AddChild(flow);

		foreach (var attribute in _engine.ActorToPartner.Catalogue.Active)
		{
			var chip = new PanelContainer
			{
				Name = $"{attribute.Key}Chip",
				CustomMinimumSize = new Vector2(49,23),
				MouseFilter = MouseFilterEnum.Pass
			};
			var chipStyle = new StyleBoxFlat { BgColor=new Color(0.17f,0.145f,0.22f,1f) };
			chipStyle.SetCornerRadiusAll(4);
			chipStyle.ContentMarginLeft=2;
			chipStyle.ContentMarginRight=2;
			chipStyle.ContentMarginTop=1;
			chipStyle.ContentMarginBottom=1;
			chip.AddThemeStyleboxOverride("panel",chipStyle);
			flow.AddChild(chip);
			var content = new HBoxContainer
			{
				Alignment=BoxContainer.AlignmentMode.Center,
				MouseFilter=MouseFilterEnum.Ignore
			};
			content.AddThemeConstantOverride("separation",1);
			chip.AddChild(content);
			var iconTexture = LoadImage(attribute.IconPath);
			if (iconTexture is not null)
				content.AddChild(new TextureRect
				{
					Texture=iconTexture,
					Modulate=new Color(attribute.Color),
					CustomMinimumSize=new Vector2(15,15),
					ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
					MouseFilter=MouseFilterEnum.Ignore
				});
			else
				content.AddChild(new Label { Text=attribute.Name[..1],
					MouseFilter=MouseFilterEnum.Ignore });
			var label = new Label
			{
				Text=$"{attribute.Default:+#;-#;0}",
				HorizontalAlignment=HorizontalAlignment.Center,
				VerticalAlignment=VerticalAlignment.Center,
				MouseFilter=MouseFilterEnum.Ignore
			};
			label.AddThemeFontSizeOverride("font_size",12);
			content.AddChild(label);
			values[attribute.Key]=label;
			chips[attribute.Key]=chip;
		}
		return title;
	}

	private void UpdateRelationshipPanels(IReadOnlyList<AppliedRelationshipChange>? changes = null)
	{
		var actorView = _engine.ActorToPartner;
		var partnerView = _engine.PartnerToActor;
		_leftStatsTitle.Text = $"{_engine.Visitor.Name} → {_engine.Host.Name}";
		_rightStatsTitle.Text = $"{_engine.Host.Name} → {_engine.Visitor.Name}";

		FillStats(_leftStatLabels, _leftStatChips, actorView,
			_engine.Visitor.Id, _engine.Host.Id,
			_engine.Visitor.Name, _engine.Host.Name, changes);
		FillStats(_rightStatLabels, _rightStatChips, partnerView,
			_engine.Host.Id, _engine.Visitor.Id,
			_engine.Host.Name, _engine.Visitor.Name, changes);
	}

	private static void FillStats(Dictionary<string, Label> labels,
		Dictionary<string, Control> chips, RelationshipStats stats,
		string from, string to, string fromName, string toName,
		IReadOnlyList<AppliedRelationshipChange>? changes)
	{
		foreach (var (attributeKey,label) in labels)
		{
			var definition = stats.Catalogue.Get(attributeKey);
			int value = stats.Get(attributeKey);
			int delta = changes?.Where(c => c.FromId==from && c.ToId==to &&
				c.Attribute.Equals(attributeKey,StringComparison.OrdinalIgnoreCase))
				.Sum(c=>c.Delta) ?? 0;
			label.Text=$"{value:+#;-#;0}";
			chips[attributeKey].TooltipText=$"{fromName} → {toName}\n{definition.Name}: {value:+#;-#;0} " +
				$"({definition.Minimum}..{definition.Maximum})" +
				(delta==0 ? "" : $"  (change {delta:+#;-#;0})");
			label.AddThemeColorOverride("font_color", delta>0
				? new Color(0.45f,0.9f,0.66f)
				: delta<0 ? new Color(1f,0.52f,0.5f)
				: new Color(0.93f,0.9f,0.96f));
		}
	}

	private static Texture2D? LoadImage(string path) =>
		!string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path)
			? GD.Load<Texture2D>(path)
			: null;


	private void RefreshPlaceholders()
	{
		_leftPlaceholder.Visible = _leftPortrait.Texture is null;
		_rightPlaceholder.Visible = _rightPortrait.Texture is null;
	}
}
