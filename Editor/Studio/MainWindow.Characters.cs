using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static SoulCaliberStudio.CharacterStudioStore;

namespace SoulCaliberStudio;

public partial class MainWindow
{
    private CharacterStudioStore? _characterStore;
    private Character? _activeCharacter;
    private Reaction? _activeReaction;
    private string? _godotRoot;
    private bool _characterLoading;
    private bool _characterDirty;
    private bool _reactionDirty;

    private static readonly string[] CommonReactionKeys =
    [
        "ACCEPTING", "AMUSED", "ANGRY", "COLD", "CONSIDERING", "DISMISSIVE",
        "FLATTERED", "IMPRESSED", "INTERESTED", "INTIMIDATED", "OFFENDED",
        "PLEASED", "RECEPTIVE", "REFLECTIVE", "RELIEVED", "SUSPICIOUS",
        "UNIMPRESSED", "UNINTERESTED", "UNMOVED", "WARM", "WARY"
    ];

    private void WireCharacterEditorEvents()
    {
        // ComboBox.SelectionChanged does not fire while typing a *new* reaction key.
        // Listen for the inner editable TextBox so leaving the form cannot lose it.
        ReactionKeyBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(ReactionKey_TextChanged), handledEventsToo: true);
    }

    private void ReactionKey_TextChanged(object sender, TextChangedEventArgs e) =>
        ReactionField_Changed(sender, e);

    private void InitCharacters(string dbPath)
    {
        _characterStore?.Dispose();
        _characterStore = new CharacterStudioStore(dbPath, _editor!);
        _characterLoading = true;
        try
        {
            ReactionKeyBox.ItemsSource = CommonReactionKeys;
            string? baseFolder = Path.GetDirectoryName(dbPath);
            string? parent = baseFolder is null ? null : Directory.GetParent(baseFolder)?.FullName;
            string? candidate = parent is null ? null : Path.Combine(parent, "Game");
            _godotRoot = candidate is not null && File.Exists(Path.Combine(candidate, "project.godot"))
                ? Path.GetFullPath(candidate) : null;
            GodotProjectText.Text = _godotRoot ?? "Locate your project.godot file to preview/import images.";
        }
        finally { _characterLoading = false; }
        RefreshCharacters();
    }

    private void RefreshCharacters(string? id = null)
    {
        if (_characterStore is null) return;
        _characterLoading = true;
        try
        {
            var all = _characterStore.Characters();
            string filter = CharacterSearchBox.Text.Trim();
            var visible = all.Where(c => c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                         c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            CharacterList.ItemsSource = visible;
            CharacterList.SelectedItem = visible.FirstOrDefault(c => c.Id == (id ?? _activeCharacter?.Id))
                ?? visible.FirstOrDefault();
            PopulateCharacter(CharacterList.SelectedItem as Character);
        }
        finally { _characterLoading = false; _characterDirty = false; _reactionDirty = false; }
    }

    private void PopulateCharacter(Character? character)
    {
        _activeCharacter = character;
        ReactionKeyBox.IsEnabled = character is not null;
        ReactionPathBox.IsEnabled = character is not null;
        CharacterIdBox.IsReadOnly = character is not null;
        CharacterIdBox.Text = character?.Id ?? "";
        CharacterNameBox.Text = character?.Name ?? "";
        CharDefaultImageBox.Text = character?.DefaultImage ?? "";
        CharWinImageBox.Text = character?.WinImage ?? "";
        CharLoseImageBox.Text = character?.LoseImage ?? "";

        Personality stats = character is null || _characterStore is null
            ? Personality.Default : _characterStore.GetPersonality(character.Id);
        CharPersuasionBox.Text = stats.Persuasion.ToString();
        CharIntimidationBox.Text = stats.Intimidation.ToString();
        CharDeceptionBox.Text = stats.Deception.ToString();
        CharCharmBox.Text = stats.Charm.ToString();
        CharInsightBox.Text = stats.Insight.ToString();
        CharResolveBox.Text = stats.Resolve.ToString();
        RefreshCharacterReactions();
        RefreshPortraitPreviews();
    }

    private void RefreshCharacterReactions(string? selectedKey = null)
    {
        bool wasLoading = _characterLoading;
        _characterLoading = true;
        try
        {
            var reactions = _activeCharacter is null || _characterStore is null
                ? new List<Reaction>() : _characterStore.GetReactions(_activeCharacter.Id);
            CharacterReactionList.ItemsSource = reactions;
            CharacterReactionList.SelectedItem = reactions.FirstOrDefault(r => r.ReactionKey == selectedKey)
                ?? reactions.FirstOrDefault();
            PopulateReaction(CharacterReactionList.SelectedItem as Reaction);
        }
        finally { _characterLoading = wasLoading; _reactionDirty = false; }
    }

    private void PopulateReaction(Reaction? reaction)
    {
        _activeReaction = reaction;
        ReactionKeyBox.IsReadOnly = reaction is not null;
        ReactionKeyBox.Text = reaction?.ReactionKey ?? "";
        ReactionPathBox.Text = reaction?.ImagePath ?? "";
        RefreshReactionPreview();
    }

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        if (!CharactersCanSwitch())
        {
            _characterLoading = true;
            try { CharacterList.SelectedItem = _activeCharacter; }
            finally { _characterLoading = false; }
            return;
        }
        _characterLoading = true;
        try { PopulateCharacter(CharacterList.SelectedItem as Character); }
        finally { _characterLoading = false; _characterDirty = false; _reactionDirty = false; }
    }

    private void CharacterReactionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        if (!CanSwitchReaction())
        {
            _characterLoading = true;
            try { CharacterReactionList.SelectedItem = _activeReaction; }
            finally { _characterLoading = false; }
            return;
        }
        _characterLoading = true;
        try { PopulateReaction(CharacterReactionList.SelectedItem as Reaction); }
        finally { _characterLoading = false; _reactionDirty = false; }
    }

    private bool CanSwitchReaction()
    {
        if (!_reactionDirty) return true;
        if (MessageBox.Show(this, "Discard your unsaved reaction changes?", "Unsaved reaction",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        _reactionDirty = false;
        return true;
    }

    private bool CharactersCanSwitch()
    {
        if (!_characterDirty && !_reactionDirty) return true;
        if (MessageBox.Show(this,
            "Discard unsaved character details and/or reaction edits? These forms have separate Save buttons.",
            "Unsaved character content", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
        _characterDirty = false;
        _reactionDirty = false;
        return true;
    }

    private void CharacterField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        _characterDirty = true;
        CharacterStatus.Text = "Unsaved character edits — click Save character & personality.";
        if (sender == CharDefaultImageBox || sender == CharWinImageBox || sender == CharLoseImageBox)
            RefreshPortraitPreviews();
    }

    private void ReactionField_Changed(object sender, RoutedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        _reactionDirty = true;
        CharacterStatus.Text = "Unsaved reaction changes — click Save reaction.";
        RefreshReactionPreview();
    }

    private void CharacterSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        if (_characterDirty || _reactionDirty)
        {
            CharacterStatus.Text = "Save or discard edits before filtering the character roster.";
            return;
        }
        RefreshCharacters();
    }

    private static int CharacterStat(TextBox box, string label)
    {
        if (!int.TryParse(box.Text, out int value) || value is < 0 or > 100)
            throw new InvalidOperationException($"{label} must be a whole number between 0 and 100.");
        return value;
    }

    private void SaveCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null) return;
        if (_reactionDirty)
        {
            MessageBox.Show(this, "Save or discard your reaction edit before saving character details. The two forms are saved separately.",
                "Unsaved reaction", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var item = new Character(CharacterIdBox.Text, CharacterNameBox.Text, CharDefaultImageBox.Text,
                CharWinImageBox.Text, CharLoseImageBox.Text);
            var stats = new Personality(
                CharacterStat(CharPersuasionBox, "Persuasion"),
                CharacterStat(CharIntimidationBox, "Intimidation"),
                CharacterStat(CharDeceptionBox, "Deception"),
                CharacterStat(CharCharmBox, "Charm"),
                CharacterStat(CharInsightBox, "Insight"),
                CharacterStat(CharResolveBox, "Resolve"));
            _characterStore.SaveCharacter(item, stats, _activeCharacter?.Id);
            _characterDirty = false;
            RefreshCharacters(item.Id.Trim().ToUpperInvariant());
            RefreshDesignerCharacterChoices();
            CharacterStatus.Text = "Character and personality saved. SQLite backup created. Reopen the Godot interaction scene to load changes.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save character failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void NewCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || !CharactersCanSwitch()) return;
        _characterLoading = true;
        try
        {
            CharacterSearchBox.Clear();
            CharacterList.SelectedItem = null;
            PopulateCharacter(null);
            CharacterStatus.Text = "New character: enter a permanent ID, display name and stats. Save before adding reactions.";
        }
        finally { _characterLoading = false; }
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || _activeCharacter is null || !CharactersCanSwitch()) return;
        if (MessageBox.Show(this, $"Delete {_activeCharacter.Name} ({_activeCharacter.Id}) and their portrait/personality records?\n\n" +
            "Dialogue references will block deletion. Files on disk will NOT be deleted. A backup is created first.",
            "Delete character?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _characterStore.DeleteCharacter(_activeCharacter.Id);
            RefreshCharacters();
            RefreshDesignerCharacterChoices();
            CharacterStatus.Text = "Character deleted; related game artwork on disk was preserved.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Deletion blocked", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void NewReaction_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || _activeCharacter is null || !CanSwitchReaction()) return;
        _characterLoading = true;
        try
        {
            CharacterReactionList.SelectedItem = null;
            PopulateReaction(null);
            CharacterStatus.Text = "New reaction: enter a key (e.g. AMUSED), import an image, then Save reaction.";
        }
        finally { _characterLoading = false; }
    }

    private void SaveReaction_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || _activeCharacter is null)
        {
            MessageBox.Show(this, "Save a character before adding reactions.", "Save character first");
            return;
        }
        try
        {
            string key = ReactionKeyBox.Text.Trim().ToUpperInvariant();
            _characterStore.SaveReaction(_activeCharacter.Id, key, ReactionPathBox.Text, _activeReaction is not null);
            _reactionDirty = false;
            RefreshCharacterReactions(key);
            CharacterStatus.Text = "Reaction saved. SQLite backup created.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save reaction failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void DeleteReaction_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || _activeCharacter is null || _activeReaction is null || !CanSwitchReaction()) return;
        if (MessageBox.Show(this, $"Delete reaction {_activeReaction.ReactionKey} for {_activeCharacter.Name}?\n" +
            "Existing dialogue may fall back to the default portrait. The image file will be kept.",
            "Delete reaction?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _characterStore.DeleteReaction(_activeCharacter.Id, _activeReaction.ReactionKey);
            RefreshCharacterReactions();
            CharacterStatus.Text = "Reaction record deleted; artwork file preserved.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Delete reaction failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RefreshDesignerCharacterChoices()
    {
        if (_designer is null) return;
        bool wasLoading = _designerLoading;
        _designerLoading = true;
        try
        {
            string? host = InteractionHostBox.SelectedItem as string;
            string? speaker = LineCharacterBox.SelectedItem as string;
            string? other = LineVisitorBox.SelectedItem as string;
            var ids = _designer.Characters();
            InteractionHostBox.ItemsSource = ids;
            LineCharacterBox.ItemsSource = ids;
            LineVisitorBox.ItemsSource = new[] { "" }.Concat(ids).ToList();
            InteractionHostBox.SelectedItem = host;
            LineCharacterBox.SelectedItem = speaker;
            LineVisitorBox.SelectedItem = other ?? "";
        }
        finally { _designerLoading = wasLoading; }
    }

    private void ChooseGodotProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Select the project.godot file inside your Godot game folder",
            Filter = "Godot project (project.godot)|project.godot|All files (*.*)|*.*",
            FileName = "project.godot",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        if (!string.Equals(Path.GetFileName(picker.FileName), "project.godot", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Select a file named project.godot.", "Wrong file");
            return;
        }
        _godotRoot = Path.GetDirectoryName(Path.GetFullPath(picker.FileName));
        GodotProjectText.Text = _godotRoot!;
        RefreshPublishGodotPath();
        RefreshPortraitPreviews();
        RefreshReactionPreview();
    }

    private static bool WithinRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private string? ResolveResource(string? resource)
    {
        if (_godotRoot is null || string.IsNullOrWhiteSpace(resource)) return null;
        string normal = resource.Trim().Replace('\\', '/');
        if (!normal.StartsWith("res://", StringComparison.Ordinal)) return null;
        string relative = normal[6..];
        if (relative.Length == 0 || relative.StartsWith('/') || relative.Contains(':') ||
            relative.Split('/').Any(s => s is "" or "." or "..")) return null;
        try
        {
            string path = Path.GetFullPath(Path.Combine(_godotRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            return WithinRoot(_godotRoot, path) ? path : null;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private (BitmapImage? Picture, string Status) GetPreview(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) return (null, "Not configured");
        if (_godotRoot is null) return (null, "Set the Godot project folder to preview images");
        string? file = ResolveResource(resource);
        if (file is null) return (null, "Invalid resource path");
        if (!File.Exists(file)) return (null, "Missing: " + resource);
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return (image, "OK");
        }
        catch
        {
            return (null, "File exists; preview not supported by Windows (e.g. some WebP/SVG formats)");
        }
    }

    private void RefreshPortraitPreviews()
    {
        if (CharDefaultPreview is null) return;
        var neutral = GetPreview(CharDefaultImageBox.Text);
        var success = GetPreview(CharWinImageBox.Text);
        var failure = GetPreview(CharLoseImageBox.Text);
        CharDefaultPreview.Source = neutral.Picture;
        CharWinPreview.Source = success.Picture;
        CharLosePreview.Source = failure.Picture;
        CorePortraitStatus.Text = $"Default: {neutral.Status}  |  Success: {success.Status}  |  Failure: {failure.Status}";
    }

    private void RefreshReactionPreview()
    {
        if (ReactionPreview is null) return;
        var preview = GetPreview(ReactionPathBox.Text);
        ReactionPreview.Source = preview.Picture;
        ReactionPortraitStatus.Text = preview.Status;
    }

    private void CheckPortraits_Click(object sender, RoutedEventArgs e)
    {
        if (_activeCharacter is null || _characterStore is null)
        {
            MessageBox.Show(this, "Select and save a character first.", "Portrait check");
            return;
        }
        if (_godotRoot is null)
        {
            MessageBox.Show(this, "Locate project.godot first.", "Portrait check");
            return;
        }
        var items = new List<(string State, string Path)>
        {
            ("Default", CharDefaultImageBox.Text), ("Success", CharWinImageBox.Text), ("Failure", CharLoseImageBox.Text)
        };
        items.AddRange(_characterStore.GetReactions(_activeCharacter.Id).Select(r => (r.ReactionKey, r.ImagePath)));
        var issues = items.Where(i => string.IsNullOrWhiteSpace(i.Path) ||
            ResolveResource(i.Path) is not string file || !File.Exists(file))
            .Select(i => $"• {i.State}: {(string.IsNullOrWhiteSpace(i.Path) ? "no image assigned" : i.Path)}").ToList();
        MessageBox.Show(this, issues.Count == 0
            ? $"All {items.Count} configured portrait states resolve to existing files.\n\nNote: this does not verify Godot's import settings."
            : $"{issues.Count} missing / invalid portrait(s):\n\n" + string.Join("\n", issues),
            "Portrait validation", MessageBoxButton.OK,
            issues.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ImportPortrait_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || sender is not Button button) return;
        if (_godotRoot is null)
        {
            MessageBox.Show(this, "Click 'Locate project.godot...' first so artwork is stored inside the game.", "Godot project required");
            return;
        }
        string state = button.Tag as string ?? "";
        if (state == "Reaction" && _activeCharacter is null)
        {
            MessageBox.Show(this, "Save the new character first, then add its reactions.", "Character required");
            return;
        }
        string id = CharacterIdBox.Text.Trim().ToUpperInvariant();
        if (id.Length == 0 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            MessageBox.Show(this, "Enter a valid Character ID first.", "Character ID required");
            return;
        }
        var picker = new OpenFileDialog
        {
            Title = "Choose a character portrait (existing game asset or new image)",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            string source = Path.GetFullPath(picker.FileName);
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
                throw new InvalidOperationException("Only PNG, JPG, WebP or BMP images can be assigned.");
            string target = source;
            if (!WithinRoot(_godotRoot, source))
            {
                // Import without overwriting any existing asset. Files are copied now;
                // save the matching character/reaction DB record separately.
                string directory = Path.Combine(_godotRoot, "Images", id);
                Directory.CreateDirectory(directory);
                string stateKey = state == "Reaction" ? ReactionKeyBox.Text.Trim().ToUpperInvariant() : state.ToUpperInvariant();
                if (stateKey.Length == 0 || stateKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                    throw new InvalidOperationException("Enter a valid reaction key before importing its portrait.");
                target = Path.Combine(directory, $"{stateKey}_{Guid.NewGuid():N}{extension}");
                File.Copy(source, target, overwrite: false);
            }
            string path = "res://" + Path.GetRelativePath(_godotRoot, target).Replace('\\', '/');
            switch (state)
            {
                case "Default": CharDefaultImageBox.Text = path; break;
                case "Success": CharWinImageBox.Text = path; break;
                case "Failure": CharLoseImageBox.Text = path; break;
                case "Reaction": ReactionPathBox.Text = path; _reactionDirty = true; break;
            }
            CharacterStatus.Text = "Artwork selected: " + path + ". Save the character/reaction record to store this path in SQLite.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Image import failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
