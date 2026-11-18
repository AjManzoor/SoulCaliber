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
	// True for normal approaches and running this scene directly from Godot.
	// InteractionLauncher.Create() disables selection for targeted debugging.
	[Export] public bool EnableSpecialEncounterSelection { get; set; } = true;
	public string? SelectedSpecialEncounterKey { get; private set; }
	public long? SelectedAnchorMemoryOccurrenceId { get; private set; }

	private DialogueRepository _repository = null!;
	private SqliteRelationshipStore _numericStore = null!;
	private SqliteMemoryStore _memoryStore = null!;
	private SqliteInteractionEffectStore _effectStore = null!;
	private SqliteSpecialEncounterCompletionStore _completionStore = null!;
	private SelectedSpecialEncounter? _selectedEncounter;
	private readonly SpecialEncounterCompletionGuard _completionGuard = new();
	// Set only by the Conversation Selection screen, before AddChild/_Ready.
	private SelectedSpecialEncounter? _requestedEncounter;
	public Action? ReturnToConversationMenu { get; set; }
	public void ConfigureSelectedEncounter(SelectedSpecialEncounter selection)
	{
		_requestedEncounter = selection ?? throw new ArgumentNullException(nameof(selection));
		EnableSpecialEncounterSelection = false;
		InteractionKey = selection.InteractionKey;
	}

	private string _encounterSummary = "";

	private enum DisplayStage { Choices, PlayerSpeaking, HostSpeaking, Finished }

	private DialogueEngine _engine = null!;
	private SqliteRelationshipStatusStore _statusStore = null!;
	private DialogueResolution? _lastResult;
	private bool _choiceSubmissionInProgress;
	private readonly List<(Button Button, string OptionKey, bool WasLocked)> _choiceButtons = new();
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
	private Label _choiceTitle = null!;
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
	private Label _leftStatusLabel = null!;
	private TextureRect _leftStatusIcon = null!;
	private Label _rightStatusLabel = null!;
	private TextureRect _rightStatusIcon = null!;
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
		_choiceTitle = GetNode<Label>("ChoiceTitle");
		_choiceTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_choiceTitle.VerticalAlignment = VerticalAlignment.Center;
		_footer = GetNode<Label>("Footer");

		// Initialise before constructing any savegame stores. An existing
		// invalid save must NEVER be silently replaced by a new one.
		var startup = SaveGameStartup.TryInitialize();
		if (!startup.Success)
		{
			GD.PushError("[Savegame] Cannot initialise: " + startup.TechnicalDetails);
			_footer.Text = startup.UserMessage;
			_continueButton.Disabled = true;
			SaveGameStartupDialog.Show(this, "Savegame could not be loaded", startup.UserMessage);
			return;
		}

		try
		{
			_statusStore = new SqliteRelationshipStatusStore();
			// All savegame stores persist across process launches; only Reset Progress clears them.
			_memoryStore = new SqliteMemoryStore(_statusStore);
			_completionStore = new SqliteSpecialEncounterCompletionStore();
			_numericStore = new SqliteRelationshipStore();
			_effectStore = new SqliteInteractionEffectStore(_numericStore, _statusStore, _memoryStore);
			_repository = new DialogueRepository();
			SelectAndCreateEngine();
	
			// Respect existing Inspector textures if no data image is present.
			_defaultLeftTexture = LoadImage(_engine.Visitor.DefaultImage) ?? _leftPortrait.Texture;
			_defaultRightTexture = LoadImage(_engine.Host.DefaultImage) ?? _rightPortrait.Texture;
			_leftName.Text = _engine.Visitor.Name;
			_rightName.Text = _engine.Host.Name;
	
			BuildRelationshipPanels();
			_leftOutcomeBadge = CreateOutcomeBadge(_leftFrame, "LeftOutcomeBadge");
			_rightOutcomeBadge = CreateOutcomeBadge(_rightFrame, "RightOutcomeBadge");
			_continueButton.Pressed += ContinueConversation;
			BuildResetProgressControl();
			RestartConversation(reselect: false);
		}
		catch (Exception ex)
		{
			// A content load failure must not crash the scene or trigger New Game.
			GD.PushError("[InteractionScene] Cannot load game state or published content: " + ex);
			_footer.Text = "Could not load the conversation or saved state. Progress was not reset. Check the diagnostic log.";
			_continueButton.Disabled = true;
			SaveGameStartupDialog.Show(this, "Conversation could not be loaded", _footer.Text);
		}
	}

	private void SelectAndCreateEngine()
	{
		string dialogueKey = InteractionKey;
		SelectedSpecialEncounterKey = null;
		SelectedAnchorMemoryOccurrenceId = null;
		_selectedEncounter = null;
		_encounterSummary = "Normal dialogue";
		if (_requestedEncounter is not null)
		{
			_selectedEncounter = _requestedEncounter;
			dialogueKey = _requestedEncounter.InteractionKey;
			SelectedSpecialEncounterKey = _requestedEncounter.EncounterKey;
			SelectedAnchorMemoryOccurrenceId = _requestedEncounter.AnchorMemoryOccurrenceId;
			_encounterSummary = $"SPECIAL: {_requestedEncounter.Title} [{_requestedEncounter.EncounterKey}]";
		}
		else if (EnableSpecialEncounterSelection)
		{
			try
			{
				var selector = new SpecialEncounterSelector(_memoryStore, _statusStore,
					_completionStore);
				var selection = selector.Select(VisitorCharacterId, PartnerCharacterId,
					message => GD.Print("[SpecialEncounter] " + message));
				if (selection is not null)
				{
					_selectedEncounter = selection;
					dialogueKey = selection.InteractionKey;
					SelectedSpecialEncounterKey = selection.EncounterKey;
					SelectedAnchorMemoryOccurrenceId = selection.AnchorMemoryOccurrenceId;
					_encounterSummary = $"SPECIAL: {selection.Title} [{selection.EncounterKey}]";
				}
			}
			catch (Exception ex)
			{
				GD.PushWarning("[SpecialEncounter] Selection failed; using normal dialogue: " + ex.Message);
			}
		}
		else
			GD.Print("[SpecialEncounter] Direct dialogue launch — selector bypassed.");

		_engine = new DialogueEngine(_repository, _numericStore,
			dialogueKey, VisitorCharacterId, PartnerCharacterId, _statusStore,
			_effectStore, _memoryStore);
		if (_selectedEncounter?.AnchorMemoryOccurrenceId is long anchoredId)
		{
			var incident = _memoryStore.FindById(anchoredId)
				?? throw new InvalidOperationException($"Anchored memory occurrence #{anchoredId} no longer exists.");
			_engine.BindMemoryOccurrence(incident, _selectedEncounter.BoundAffectedCharacterId);
		}
	}

	private void BuildResetProgressControl()
	{
		// Low-profile prototype control outside the central conversation and portrait areas.
		var resetButton = new Button
		{
			Name = "ResetProgressButton",
			Text = "New game / Reset",
			AnchorLeft = 0.79f,
			AnchorRight = 0.975f,
			AnchorTop = 0.935f,
			AnchorBottom = 0.985f,
			FocusMode = FocusModeEnum.None
		};
		resetButton.AddThemeFontSizeOverride("font_size", 13);
		AddChild(resetButton);
		var confirmation = new ConfirmationDialog
		{
			Name = "ResetProgressConfirmation",
			Title = "Start a new game?",
			DialogText = "This permanently clears saved relationship values, relationship statuses, interaction history and memories. Authored Studio defaults stay unchanged. Continue?"
		};
		AddChild(confirmation);
		resetButton.Pressed += () => confirmation.PopupCentered(new Vector2I(560, 190));
		confirmation.Confirmed += () =>
		{
			try
			{
				SaveGameProgress.ResetAll();
				if (ReturnToConversationMenu is not null)
				{
					// Re-evaluate eligibility from the cleared savegame rather
					// than continuing the now-ineligible selected encounter.
					ReturnToConversationMenu();
				}
				else
				{
					RestartConversation();
					_footer.Text = "New game started — saved progress cleared; authored defaults restored.";
				}
			}
			catch (Exception ex)
			{
				GD.PushError($"Could not reset savegame: {ex}");
				_footer.Text = "Reset failed — existing savegame has not been deliberately cleared.";
			}
		};
	}

	private void RestartConversation(bool reselect = true)
	{
		// Every NEW encounter (including Start another conversation) rechecks
		// eligibility. Advancing dialogue nodes never invokes the selector.
		if (reselect) SelectAndCreateEngine();
		_completionGuard.Reset();
		_engine.Restart();
		_leftPortrait.Texture = _defaultLeftTexture;
		_rightPortrait.Texture = _defaultRightTexture;
		_leftPortrait.Modulate = Colors.White;
		_rightPortrait.Modulate = Colors.White;
		_leftOutcomeBadge.Visible = false;
		_rightOutcomeBadge.Visible = false;
		_lastResult = null;
		_choiceSubmissionInProgress = false;
		RefreshPlaceholders();
		ShowCurrentNode();
	}

	private void ShowCurrentNode()
	{
		_stage = DisplayStage.Choices;
		_lastResult = null;
		_choiceTitle.Text = "CHOOSE YOUR RESPONSE";
		_choiceTitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.71f, 0.78f));
		_choiceTitle.TooltipText = "";
		_sceneTitle.Text = $"{_engine.Definition.Title.ToUpperInvariant()}  •  ROUND {_engine.CurrentNode.Round}/{_engine.Definition.Nodes.Max(n => n.Round)}";
		Speak(_engine.Host.Id, _engine.Introduction());
		_choicesScroll.Visible = true;
		_choicesScroll.ScrollVertical = 0;
		_continueButton.Visible = false;

		_choiceButtons.Clear();
		foreach (Node child in _choicesContainer.GetChildren())
		{
			_choicesContainer.RemoveChild(child);
			child.QueueFree();
		}

		var availableChoices = _engine.GetOptions();
		if (availableChoices.Count == 0)
		{
			// Avoid an unexplained empty response area if every authored choice is hidden.
			var message = new Label
			{
				Text = "No responses are currently available.",
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_choicesContainer.AddChild(message);
		}
		int index = 0;
		foreach (AvailableOption choice in availableChoices)
		{
			index++;
			string key = choice.Definition.Key;
			var button = new Button
			{
				Name = $"Choice{index}",
				Text = choice.IsLocked
					? $"{index}. {_engine.FormatDialogueText(choice.Definition.DynamicTarget is null ? choice.Definition.Label : choice.Definition.Label.Replace("{target}", "…", StringComparison.OrdinalIgnoreCase))}  [LOCKED: {choice.LockReason}]"
					: $"{index}. {_engine.FormatDialogueText(choice.Definition.DynamicTarget is null ? choice.Definition.Label : choice.Definition.Label.Replace("{target}", "…", StringComparison.OrdinalIgnoreCase))}  ({choice.Chance}%" +
						(string.IsNullOrEmpty(_engine.SkillLabel(choice.Definition))
							? "" : $" · {_engine.SkillLabel(choice.Definition)}") + ")",
				TooltipText = _engine.ChanceBreakdown(choice.Definition),
				Disabled = choice.IsLocked,
				CustomMinimumSize = new Vector2(0, 48),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			button.AddThemeFontSizeOverride("font_size", 17);
			button.Pressed += () =>
			{
				if(choice.Definition.DynamicTarget is not null) ShowDynamicTargetMenu(key);
				else Choose(key);
			};
			_choicesContainer.AddChild(button);
			_choiceButtons.Add((button, key, choice.IsLocked));
		}

		UpdateRelationshipPanels();
		_footer.Text = _encounterSummary + "  •  choose an action";
	}

	/// <summary>Second stage of a dynamic option: choose exactly ONE matching character.</summary>
	private void ShowDynamicTargetMenu(string optionKey)
	{
		if (_stage != DisplayStage.Choices || _choiceSubmissionInProgress || _engine.HasCommittedChoice) return;
		var targets=_engine.DynamicTargetsForOption(optionKey);
		if (targets.Count==0)
		{
			_footer.Text="No matching relationship targets are currently available.";
			return;
		}
		var definition=_engine.CurrentNode.Options.First(o=>
			string.Equals(o.Key,optionKey,StringComparison.OrdinalIgnoreCase));
		_choiceTitle.Text="SELECT ONE CHARACTER";
		_choicesScroll.ScrollVertical=0;
		_choiceButtons.Clear();
		foreach (Node child in _choicesContainer.GetChildren())
		{
			_choicesContainer.RemoveChild(child);child.QueueFree();
		}
		foreach (var target in targets)
		{
			string selection=optionKey+"::"+target.Id;
			string title=definition.Label.Contains("{target}",StringComparison.OrdinalIgnoreCase)
				? definition.Label.Replace("{target}",target.Name,StringComparison.OrdinalIgnoreCase)
				: target.Name;
			var button=new Button
			{
				Text=title,
				CustomMinimumSize=new Vector2(0,48),
				SizeFlagsHorizontal=SizeFlags.ExpandFill
			};
			button.AddThemeFontSizeOverride("font_size",17);
			button.Pressed+=()=>Choose(selection);
			_choicesContainer.AddChild(button);
			_choiceButtons.Add((button,selection,false));
		}
		var back=new Button
		{
			Text="Back to choices",
			CustomMinimumSize=new Vector2(0,44),
			SizeFlagsHorizontal=SizeFlags.ExpandFill
		};
		back.Pressed+=ShowCurrentNode;
		_choicesContainer.AddChild(back);
		// When a database save fails, only the already-rolled target may be retried.
		_choiceButtons.Add((back,"__back__",false));
		_footer.Text="Choose one relationship target. Only that character will be affected.";
	}

	private void SetChoiceButtons(bool disableAll, string? retryOnlyKey = null)
	{
		foreach (var (button, key, wasLocked) in _choiceButtons)
			button.Disabled = disableAll || wasLocked ||
				(retryOnlyKey is not null &&
				 !string.Equals(key, retryOnlyKey, StringComparison.OrdinalIgnoreCase));
	}

	private void Choose(string optionKey)
	{
		// The UI guard stops double-clicks; DialogueEngine independently blocks
		// duplicate commits (including a callback after UI presentation fails).
		if (_stage != DisplayStage.Choices || _choiceSubmissionInProgress ||
			_engine.HasCommittedChoice) return;

		_choiceSubmissionInProgress = true;
		SetChoiceButtons(disableAll: true);
		try
		{
			try
			{
				_lastResult = _engine.Choose(optionKey); // One roll, one atomic commit.
			}
			catch (Exception ex)
			{
				// A failed transaction can retry ONLY this already-resolved choice.
				// Do not offer other options, which would let a failed roll be discarded.
				GD.PushError($"[Dialogue] Could not save choice '{optionKey}': {ex}");
				if (_engine.HasCommittedChoice)
				{
					_footer.Text = "Choice already saved; cannot submit it again.";
					return;
				}
				SetChoiceButtons(disableAll: false, retryOnlyKey: _engine.PendingChoiceKey);
				_footer.Text = _engine.PendingChoiceKey is null
					? "Could not resolve dialogue choice; check the error and retry."
					: "Could not save dialogue choice — retry the SAME choice (roll preserved).";
				return;
			}

			// Set this BEFORE drawing; a portrait/UI exception must not allow
			// another submission of an already committed outcome.
			_stage = DisplayStage.PlayerSpeaking;
			_choicesScroll.Visible = false;
			_continueButton.Visible = true;
			_continueButton.Text = "Continue";

			try
			{
				// Only the initiator rolls. The recipient reacts; they are never
				// a second success/failure roll or the inverse of the initiator.
				SetActorOutcomePortrait(_leftPortrait, _leftOutcomeBadge,
					_engine.Visitor, _lastResult.PlayerLine,
					_lastResult.Succeeded, _defaultLeftTexture);
				SetTargetReactionPortrait(_rightPortrait, _rightOutcomeBadge,
					_engine.Host, _lastResult.HostLine,
					_lastResult.Succeeded, _lastResult.TargetReaction,
					_defaultRightTexture);

				Speak(_engine.Visitor.Id, _lastResult.PlayerLine.Text);
				UpdateRelationshipPanels(_lastResult.Changes);
				ShowStatusChangeFeedback(_lastResult.StatusChanges);
				string skill = _engine.SkillLabel(_lastResult.Choice);
				_footer.Text = $"Chance {_lastResult.Chance}%" +
					(string.IsNullOrEmpty(skill) ? "" : $" ({skill})") +
					$"  •  Rolled {_lastResult.Roll}/100  •  " +
					$"{_engine.Visitor.Name}: " + (_lastResult.Succeeded ? "Success" : "Failure") +
					$"  •  {_engine.Host.Name}: {ReactionLabel(_lastResult.TargetReaction)}" +
					$"  •  {_lastResult.Changes.Count} stat change(s)" +
					$"  •  {_lastResult.StatusChanges.Count} status change(s)";
			}
			catch (Exception ex)
			{
				GD.PushError($"[Dialogue] Choice saved but presentation failed: {ex}");
				_footer.Text = "Choice saved. Some presentation failed; continue without resubmitting.";
			}
		}
		finally
		{
			_choiceSubmissionInProgress = false;
		}
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
			if (ReturnToConversationMenu is not null)
				ReturnToConversationMenu();
			else
				RestartConversation();
		}
	}

	private void EndConversation()
	{
		// Only terminal dialogue flow calls this. Choices were already committed
		// individually; the encounter becomes complete only after the player has
		// seen the final recipient response and selected Finish conversation.
		// Never reselect a memory here: _selectedEncounter retains the exact
		// anchor and directional pair chosen at launch.
		_continueButton.Disabled = true;
		try
		{
			_completionGuard.Finish(() =>
			{
				if (_selectedEncounter is null) return; // Normal conversation.
				bool recorded = _completionStore.RecordCompletion(_selectedEncounter,
					_engine.Visitor.Id, _engine.Host.Id);
				GD.Print($"[SpecialEncounter] Completed {_selectedEncounter.EncounterKey}" +
					(recorded ? " — saved." : " — repeatable or already saved."));
			});
		}
		catch (Exception ex)
		{
			// The guard only marks completion on successful persistence.
			// Retry the SAME end operation; never reapply the final choice.
			GD.PushError($"[SpecialEncounter] Could not save completion: {ex}");
			_continueButton.Text = "Retry finish";
			_footer.Text = "Could not save special encounter completion — retry finish.";
			return;
		}
		finally
		{
			_continueButton.Disabled = false;
		}

		// If presentation fails after persistence, the guard remembers that the
		// completion was committed; another Finish will not write a second row.
		_stage = DisplayStage.Finished;
		_choicesScroll.Visible = false;
		_continueButton.Visible = true;
		_continueButton.Text = "Start another conversation";
		_footer.Text = "Conversation complete  •  effects saved" +
			(_selectedEncounter is null ? "" : "  •  special encounter completed");
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
		(_leftStatsTitle, _leftStatusIcon, _leftStatusLabel) = AddStatsStrip("ActorRelationships", 0.025f, 0.26f,
			_leftStatLabels, _leftStatChips);
		(_rightStatsTitle, _rightStatusIcon, _rightStatusLabel) = AddStatsStrip("PartnerRelationships", 0.74f, 0.975f,
			_rightStatLabels, _rightStatChips);
	}

	private (Label Title, TextureRect StatusIcon, Label Status) AddStatsStrip(string name, float left, float right,
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
		// Status sits on the SAME line as the directional heading; portrait and
		// numerical attribute rows keep exactly their existing positions.
		var heading = new HBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Pass
		};
		heading.AddThemeConstantOverride("separation", 7);
		stack.AddChild(heading);
		var title = new Label { ClipText=true, MouseFilter=MouseFilterEnum.Ignore };
		title.AddThemeFontSizeOverride("font_size",12);
		title.AddThemeColorOverride("font_color",new Color(0.92f,0.81f,0.66f));
		heading.AddChild(title);
		var statusIcon = new TextureRect
		{
			CustomMinimumSize = new Vector2(15, 15),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false
		};
		heading.AddChild(statusIcon);
		var status = new Label { MouseFilter=MouseFilterEnum.Pass };
		status.AddThemeFontSizeOverride("font_size",12);
		heading.AddChild(status);

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
		return (title, statusIcon, status);
	}

	private void UpdateRelationshipPanels(IReadOnlyList<AppliedRelationshipChange>? changes = null)
	{
		var actorView = _engine.ActorToPartner;
		var partnerView = _engine.PartnerToActor;
		_leftStatsTitle.Text = $"{_engine.Visitor.Name} → {_engine.Host.Name}";
		_rightStatsTitle.Text = $"{_engine.Host.Name} → {_engine.Visitor.Name}";
		UpdateStatusLabel(_leftStatusIcon, _leftStatusLabel, _engine.ActorToPartnerStatus);
		UpdateStatusLabel(_rightStatusIcon, _rightStatusLabel, _engine.PartnerToActorStatus);

		FillStats(_leftStatLabels, _leftStatChips, actorView,
			_engine.Visitor.Id, _engine.Host.Id,
			_engine.Visitor.Name, _engine.Host.Name, changes);
		FillStats(_rightStatLabels, _rightStatChips, partnerView,
			_engine.Host.Id, _engine.Visitor.Id,
			_engine.Host.Name, _engine.Visitor.Name, changes);
	}

    private void UpdateStatusLabel(TextureRect icon, Label label, string statusKey)
    {
        string displayName = _statusStore.GetDisplayName(statusKey);
        string iconPath = _statusStore.GetIconPath(statusKey);
        string hexColor = _statusStore.GetColorHex(statusKey);
        Color colour = new Color(hexColor);
        Texture2D? texture = LoadImage(iconPath);
        icon.Texture = texture;
        icon.Modulate = colour;
        icon.Visible = texture is not null;
        // An authored icon replaces the old dot; a missing/empty icon retains it.
        label.Text = texture is null ? $"• {displayName}" : displayName;
        label.TooltipText = $"Current status: {displayName} ({statusKey})";
        label.AddThemeColorOverride("font_color", colour);
    }

    private void ShowStatusChangeFeedback(IReadOnlyList<AppliedRelationshipStatusChange> changes)
    {
        if (changes.Count == 0)
        {
            _choiceTitle.Text = "RESPONSE RECORDED";
            return;
        }

        string[] lines = changes.Select(change =>
            $"STATUS  •  {DisplayCharacter(change.FromId)} → {DisplayCharacter(change.ToId)}: " +
            $"{_statusStore.GetDisplayName(change.Before)} → {_statusStore.GetDisplayName(change.After)}")
            .ToArray();
        _choiceTitle.Text = string.Join("\n", lines);
        _choiceTitle.TooltipText = string.Join("\n", lines);
        _choiceTitle.AddThemeColorOverride("font_color", new Color(0.93f, 0.83f, 0.66f));
    }

    private string DisplayCharacter(string id) =>
        string.Equals(id, _engine.Visitor.Id, StringComparison.OrdinalIgnoreCase)
            ? _engine.Visitor.Name
            : string.Equals(id, _engine.Host.Id, StringComparison.OrdinalIgnoreCase)
                ? _engine.Host.Name : _engine.CharacterName(id);

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
