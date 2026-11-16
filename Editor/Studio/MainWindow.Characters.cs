using Microsoft.Win32;
using System.IO;
using System.Globalization;
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
    private readonly Dictionary<string, TextBox> _skillEditors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _traitEditors = new(StringComparer.OrdinalIgnoreCase);

    private void WireCharacterEditorEvents() { /* Reaction keys come from the catalogue; no free typing. */ }

    private void InitCharacters(string dbPath)
    {
        _characterStore?.Dispose();
        _characterStore = new CharacterStudioStore(dbPath, _editor!);
        _characterLoading = true;
        try
        {
            ReactionKeyBox.ItemsSource = _designer?.ReactionTypes().Select(x => x.Key).ToArray() ?? [];
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

        BuildCharacterStatEditors(character?.Id);
        BuildCharacterTraitEditors(character?.Id);
        RefreshCharacterReactions();
        RefreshPortraitPreviews();
    }

    private static TextBlock StatLabel(string text) => new()
    {
        Text=text,Foreground=System.Windows.Media.Brushes.LightSteelBlue,Margin=new Thickness(0,4,0,2),
        TextWrapping=TextWrapping.Wrap
    };

    private TextBox StatEditor(string text)
    {
        var box=new TextBox { Text=text,Margin=new Thickness(0,0,0,6),Padding=new Thickness(6),MinWidth=70 };
        box.TextChanged+=CharacterField_Changed;
        return box;
    }

    private void BuildCharacterStatEditors(string? characterId)
    {
        _skillEditors.Clear();
        CharacterSkillsPanel.Children.Clear();
        if(_designer is null || _characterStore is null) return;
        var skills=_characterStore.GetSkills(characterId);
        foreach(var definition in _designer.SkillDefinitions(true))
        {
            CharacterSkillsPanel.Children.Add(StatLabel($"{definition.Name} ({definition.Minimum} to {definition.Maximum})"));
            var box=StatEditor(skills.GetValueOrDefault(definition.Key,definition.Default).ToString(CultureInfo.InvariantCulture));
            _skillEditors.Add(definition.Key,box);
            CharacterSkillsPanel.Children.Add(box);
        }

    }

    private void BuildCharacterTraitEditors(string? characterId)
    {
        _traitEditors.Clear();
        CharacterTraitsPanel.Children.Clear();
        if (_designer is null || _characterStore is null) return;
        var assigned = _characterStore.GetCharacterTraits(characterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var trait in _designer.TraitDefinitions().Where(t => t.IsActive))
        {
            var box = new CheckBox
            {
                Content = trait.Name, ToolTip = $"{trait.Key}: {trait.Description}",
                IsChecked = assigned.Contains(trait.Key),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 3, 14, 7)
            };
            box.Checked += CharacterTrait_Changed;
            box.Unchecked += CharacterTrait_Changed;
            CharacterTraitsPanel.Children.Add(box);
            _traitEditors.Add(trait.Key, box);
        }
    }
    private void CharacterTrait_Changed(object sender, RoutedEventArgs e)
    {
        if (_characterLoading || _characterStore is null) return;
        _characterDirty = true;
        CharacterStatus.Text = "Unsaved character trait changes — click Save character, skills and traits.";
    }
    private void ManagePersonalityTraits_Click(object sender, RoutedEventArgs e)
    {
        if (_designer is null || !CharactersCanSwitch()) return;
        new PersonalityTraitsWindow(_designer) { Owner = this }.ShowDialog();
        _characterLoading = true;
        try { BuildCharacterTraitEditors(_activeCharacter?.Id); }
        finally { _characterLoading = false; }
    }

    private void ManageCharacterSkills_Click(object sender, RoutedEventArgs e)
    {
        if(_designer is null || !CharactersCanSwitch()) return;
        var window=new CharacterSkillsWindow(_designer){Owner=this};
        window.ShowDialog();
        RefreshCharacters(_activeCharacter?.Id);
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
        ReactionKeyBox.IsEnabled = reaction is null && _activeCharacter is not null;
        ReactionKeyBox.SelectedItem = reaction?.ReactionKey;
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
            var skills = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach(var (key,box) in _skillEditors)
            {
                if(!int.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))
                    throw new InvalidOperationException($"{key} must be a whole number.");
                skills.Add(key,value);
            }
            _characterStore.SaveCharacter(item, skills, _activeCharacter?.Id,
                _traitEditors.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray());
            _characterDirty = false;
            RefreshCharacters(item.Id.Trim().ToUpperInvariant());
            RefreshDesignerCharacterChoices();
            CharacterStatus.Text = "Character, skills and traits saved. Reopen Godot to load changes.";
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
        if (MessageBox.Show(this, $"Delete {_activeCharacter.Name} ({_activeCharacter.Id}) and their portrait/skill records?\n\n" +
            "Dialogue references will block deletion. Files on disk will NOT be deleted. No automatic backup will be created.",
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
            CharacterStatus.Text = "New reaction: choose a catalogue key, import an image, then Save reaction.";
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
            string key = ReactionKeyBox.SelectedItem as string ?? "";
            _characterStore.SaveReaction(_activeCharacter.Id, key, ReactionPathBox.Text, _activeReaction is not null);
            _reactionDirty = false;
            RefreshCharacterReactions(key);
            CharacterStatus.Text = "Reaction saved.";
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
            string? speaker = LineCharacterBox.SelectedItem as string;
            string? other = LineVisitorBox.SelectedItem as string;
            var ids = _designer.Characters();
            LineCharacterBox.ItemsSource = ids;
            LineVisitorBox.ItemsSource = new[] { "" }.Concat(ids).ToList();
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

    private void GenerateDefaultPortraitPaths_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null || _activeCharacter is null)
        {
            MessageBox.Show(this, "Save and select a character first.", "Character required");
            return;
        }
        if (_characterDirty || _reactionDirty)
        {
            MessageBox.Show(this, "Save your current character and reaction edits before generating database paths.",
                "Unsaved changes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string folder = CharacterStudioStore.PortraitFolder(_activeCharacter.Id);
        var decision = MessageBox.Show(this,
            $"Generate portrait paths for {_activeCharacter.Name}?\n\n" +
            $"Folder: {folder}\n" +
            $"Default.png, Win.png, Lose.png and {_designer?.ReactionTypes().Count ?? 0} catalogue reactions.\n\n" +
            "YES = fill empty paths only (preserve all custom paths)\n" +
            "NO = overwrite all these paths with standard names\n" +
            "CANCEL = make no changes\n\n" +
            "Missing actual PNG files will remain publishing warnings, not errors.",
            "Generate default paths", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (decision == MessageBoxResult.Cancel) return;
        try
        {
            int changed = _characterStore.GenerateDefaultPortraitPaths(_activeCharacter.Id,
                _designer?.ReactionTypes().Select(x => x.Key).ToArray() ?? [], overwrite: decision == MessageBoxResult.No);
            string id = _activeCharacter.Id;
            RefreshCharacters(id);
            CharacterStatus.Text = $"Generated {changed} character/reaction database record update(s) for {id}. " +
                "Check portrait previews for missing PNGs. No automatic backup was created.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Portrait generation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StandardisePortraitFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_characterStore is null) return;
        if (!CanLeaveTable() || !DesignerCanSwitch()) return;
        if (_godotRoot is null)
        {
            MessageBox.Show(this, "Select the Godot project.godot file first.", "Godot project required");
            return;
        }
        if (_characterDirty || _reactionDirty)
        {
            MessageBox.Show(this, "Save all character and reaction edits before migrating artwork folders.",
                "Unsaved edits", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var preview = _characterStore.PreviewPortraitFolderMigration(_godotRoot);
            if (preview.Changes.Count == 0)
            {
                MessageBox.Show(this, "All configured portrait directories already match the character IDs.",
                    "No changes needed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string summary = string.Join("\n", preview.Changes.Select(p =>
                $"{p.CharacterId}: Images/{p.OldFolder} -> Images/{p.NewFolder}" +
                (p.ActualSourceFolder is null ? " [source folder not found]" : "") +
                (p.Conflict is null ? "" : " [CONFLICT: " + p.Conflict + "]")));
            string warningText = preview.Warnings.Count == 0 ? "" :
                "\n\nWarnings:\n" + string.Join("\n", preview.Warnings.Take(10));
            var confirmed = MessageBox.Show(this,
                "This one-time migration will rename portrait folders and update explicit paths in " +
                "Characters, ReactionImages, Lines and Godot text resources (.tscn/.tres/etc.).\n\n" +
                summary + "\n\n" +
                $"References: {preview.DatabaseReferences} database paths; " +
                $"{preview.ResourceFiles} Godot text files." + warningText + "\n\n" +
                "BEFORE CONTINUING: close the Godot editor and running game, and keep a copy of your Godot project. " +
                "Studio creates a SQLite backup and copies modified Godot text resources to StudioBackups.\n\n" +
                "Proceed with the migration?",
                "Standardise portrait folder names", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes) return;
            string report = _characterStore.StandardisePortraitFolders(_godotRoot);
            RefreshCharacters(_activeCharacter?.Id);
            CharacterStatus.Text = report + " Restart Godot to re-import the moved images.";
            MessageBox.Show(this, report + "\n\nReopen Godot and check several portraits. " +
                "If you previously referenced these images by paths outside SQLite/standard Godot resources, " +
                "review those references too.", "Folder migration complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Folder migration stopped", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            string directory = Path.Combine(_godotRoot, "Images", id);
            Directory.CreateDirectory(directory);
            string stateKey = state == "Reaction" ? ReactionKeyBox.SelectedItem as string ?? "" : state;
            if (string.IsNullOrWhiteSpace(stateKey) || stateKey.Any(c =>
                    !char.IsAsciiLetterOrDigit(c) && c != '_'))
                throw new InvalidOperationException("Enter a valid reaction key before importing its portrait.");
            string canonical = Path.GetFileNameWithoutExtension(
                CharacterStudioStore.FilenameForState(stateKey)) + extension;
            string target = source;
            // Assets inside another image folder are copied into the character-ID folder too.
            if (!string.Equals(Path.GetDirectoryName(source), directory, StringComparison.OrdinalIgnoreCase))
            {
                target = Path.Combine(directory, canonical);
                if (File.Exists(target))
                    target = Path.Combine(directory,
                        Path.GetFileNameWithoutExtension(canonical) + "_" + Guid.NewGuid().ToString("N") + extension);
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

