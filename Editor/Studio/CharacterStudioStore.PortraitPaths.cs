using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text;

namespace SoulCaliberStudio;

// Explicit paths remain the only source of truth for Godot.
// The editor generates and migrates paths; Godot requires no new conventions or code.
public sealed partial class CharacterStudioStore
{
    public static string PortraitFolder(string characterId) =>
        $"res://Images/{Identity(characterId, "Character ID")}/";

    public static string FilenameForState(string state) => state switch
    {
        "Default" => "Default.png",
        "Success" => "Win.png",
        "Failure" => "Lose.png",
        _ => ReactionFile(state)
    };

    private static string ReactionFile(string key)
    {
        string id = Identity(key, "Reaction key");
        string name = string.Concat(id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
        return name + ".png";
    }

    /// <summary>
    /// Stores the generated strings in Characters / ReactionImages in one transaction.
    /// Existing nonempty values are preserved unless overwrite=true.
    /// Files do not need to exist: the publisher will warn about missing artwork.
    /// </summary>
    public int GenerateDefaultPortraitPaths(string characterId, IReadOnlyList<string> reactionKeys, bool overwrite)
    {
        string id = Identity(characterId, "Character ID");
        string basePath = PortraitFolder(id);
        if (reactionKeys.Count == 0) throw new InvalidOperationException("No reactions were selected.");
        string[] keys = reactionKeys.Select(k => Identity(k, "Reaction key"))
            .Distinct(StringComparer.Ordinal).ToArray();
        using (var exists = Sql("SELECT 1 FROM Characters WHERE Id=$id LIMIT 1;"))
        {
            Add(exists, "$id", id);
            if (exists.ExecuteScalar() is null) throw new InvalidOperationException("Save the character before generating portraits.");
        }

        int changed = 0;
        Write(tx =>
        {
            using (var cmd = Sql(overwrite ?
                """
                UPDATE Characters SET DefaultImage=$neutral,WinImage=$win,LoseImage=$lose
                WHERE Id=$id AND (DefaultImage<>$neutral OR WinImage<>$win OR LoseImage<>$lose);
                """ :
                """
                UPDATE Characters SET
                    DefaultImage=CASE WHEN trim(DefaultImage)='' THEN $neutral ELSE DefaultImage END,
                    WinImage=CASE WHEN trim(WinImage)='' THEN $win ELSE WinImage END,
                    LoseImage=CASE WHEN trim(LoseImage)='' THEN $lose ELSE LoseImage END
                WHERE Id=$id AND (trim(DefaultImage)='' OR trim(WinImage)='' OR trim(LoseImage)='');
                """, tx))
            {
                Add(cmd, "$id", id);
                Add(cmd, "$neutral", basePath + "Default.png");
                Add(cmd, "$win", basePath + "Win.png");
                Add(cmd, "$lose", basePath + "Lose.png");
                changed += cmd.ExecuteNonQuery();
            }

            foreach (string key in keys)
            {
                using var cmd = Sql(overwrite ?
                    """
                    INSERT INTO ReactionImages(CharacterId,ReactionKey,ImagePath)
                    VALUES($id,$key,$path)
                    ON CONFLICT(CharacterId,ReactionKey) DO UPDATE SET ImagePath=excluded.ImagePath
                    WHERE ReactionImages.ImagePath<>excluded.ImagePath;
                    """ :
                    """
                    INSERT INTO ReactionImages(CharacterId,ReactionKey,ImagePath)
                    VALUES($id,$key,$path)
                    ON CONFLICT(CharacterId,ReactionKey) DO UPDATE SET ImagePath=excluded.ImagePath
                    WHERE trim(ReactionImages.ImagePath)='';
                    """, tx);
                Add(cmd, "$id", id);
                Add(cmd, "$key", key);
                Add(cmd, "$path", basePath + ReactionFile(key));
                changed += cmd.ExecuteNonQuery();
            }
        });
        return changed;
    }

    public sealed record PortraitFolderChange(string CharacterId, string OldFolder,
        string NewFolder, string? ActualSourceFolder, string? Conflict)
    {
        public string OldPrefix => $"res://Images/{OldFolder}/";
        public string NewPrefix => $"res://Images/{NewFolder}/";
        public bool RequiresPhysicalMove => ActualSourceFolder is not null &&
            !string.Equals(ActualSourceFolder, NewFolder, StringComparison.Ordinal);
    }

    public sealed record PortraitMigrationPreview(
        IReadOnlyList<PortraitFolderChange> Changes,
        int DatabaseReferences, int ResourceFiles,
        IReadOnlyList<string> Warnings);

    private static string? ImageFolderFromPath(string? path)
    {
        const string prefix = "res://Images/";
        if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string remainder = path[prefix.Length..];
        int separator = remainder.IndexOf('/');
        return separator > 0 ? remainder[..separator] : null;
    }

    private static string CheckedGodotRoot(string gameRoot)
    {
        string root = Path.GetFullPath(gameRoot);
        if (!File.Exists(Path.Combine(root, "project.godot")))
            throw new InvalidOperationException("Select the correct Godot project.godot file first.");
        return root;
    }

    private static readonly (string Table, string Column)[] ArtworkColumns =
    [
        ("Characters", "DefaultImage"), ("Characters", "WinImage"),
        ("Characters", "LoseImage"), ("ReactionImages", "ImagePath"),
        ("Lines", "ImagePath")
    ];

    public PortraitMigrationPreview PreviewPortraitFolderMigration(string gameRoot)
    {
        string root = CheckedGodotRoot(gameRoot);
        string imagesRoot = Path.Combine(root, "Images");
        var actualFolders = Directory.Exists(imagesRoot)
            ? Directory.EnumerateDirectories(imagesRoot).Select(Path.GetFileName)
                .Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();

        var changes = new List<PortraitFolderChange>();
        var warnings = new List<string>();
        foreach (var c in Characters())
        {
            string? oldFolder = ImageFolderFromPath(c.DefaultImage) ??
                ImageFolderFromPath(c.WinImage) ?? ImageFolderFromPath(c.LoseImage);
            if (oldFolder is null)
            {
                var reaction = GetReactions(c.Id).FirstOrDefault();
                oldFolder = ImageFolderFromPath(reaction?.ImagePath);
            }
            if (oldFolder is null)
            {
                warnings.Add($"{c.Id}: no existing Images/... path to migrate. Use Generate default paths later.");
                continue;
            }
            // Even when the DB already uses ARK, Windows may still hold a folder named Ark.
            // Rename its physical casing so exports work on case-sensitive Linux/Steam Deck.
            bool pathAlreadyCanonical = string.Equals(oldFolder, c.Id, StringComparison.Ordinal);
            if (pathAlreadyCanonical && actualFolders.Contains(c.Id, StringComparer.Ordinal)) continue;
            if (oldFolder is "." or ".." || oldFolder.Contains('\\') || oldFolder.Contains(':'))
                throw new InvalidOperationException($"Unsafe image directory recorded for {c.Id}: {oldFolder}");

            string? actual = actualFolders.FirstOrDefault(n => string.Equals(n, oldFolder, StringComparison.OrdinalIgnoreCase));
            string? target = actualFolders.FirstOrDefault(n => string.Equals(n, c.Id, StringComparison.OrdinalIgnoreCase));
            string? conflict = actual is not null && target is not null &&
                !string.Equals(actual, target, StringComparison.OrdinalIgnoreCase)
                ? $"Destination folder Images/{target} already exists." : null;

            if (actual is null)
                warnings.Add($"{c.Id}: old folder Images/{oldFolder} is missing; database strings will still be migrated.");
            changes.Add(new(c.Id, oldFolder, c.Id, actual, conflict));
        }

        foreach (var group in changes.GroupBy(x => x.OldFolder, StringComparer.OrdinalIgnoreCase))
            if (group.Select(x => x.NewFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                throw new InvalidOperationException($"Multiple characters share Images/{group.Key}; resolve the shared folder before migrating.");

        int references = 0;
        foreach (var change in changes)
        foreach (var (table, column) in ArtworkColumns)
        {
            using var cmd = Sql($"SELECT COUNT(*) FROM \"{table}\" WHERE lower(substr(\"{column}\",1,length($old)))=lower($old);");
            Add(cmd, "$old", change.OldPrefix);
            references += Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        int textFiles = ScanResourceFiles(root, changes).Count;
        return new(changes, references, textFiles, warnings);
    }

    // Only Godot text resources are edited; never touch binaries or Godot's generated .godot cache.
    private static List<(string Path, string Original, string Updated)> ScanResourceFiles(
        string root, IReadOnlyList<PortraitFolderChange> changes)
    {
        if (changes.Count == 0) return [];
        var result = new List<(string, string, string)>();
        var directories = new Stack<string>();
        directories.Push(root);
        string[] excluded = [".godot", ".git", "bin", "obj", "Images", "Builds", "Published", "StudioBackups"];
        string[] accepted = [".tscn", ".tres", ".json", ".gd", ".cs", ".cfg", ".godot"];
        while (directories.Count > 0)
        {
            string dir = directories.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!excluded.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                    directories.Push(sub);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!accepted.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFileName(file), "project.godot", StringComparison.OrdinalIgnoreCase)) continue;
                if (new FileInfo(file).Length > 5_000_000) continue;
                string original = File.ReadAllText(file, Encoding.UTF8);
                string updated = original;
                foreach (var change in changes)
                    updated = updated.Replace(change.OldPrefix, change.NewPrefix, StringComparison.OrdinalIgnoreCase);
                if (original != updated) result.Add((file, original, updated));
            }
        }
        return result;
    }

    // Handles Windows case-only directory rename, e.g. Ark -> ARK.
    private static void RenameDirectory(string source, string destination)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal)) return;
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            string temporary = source + ".studio-rename-" + Guid.NewGuid().ToString("N");
            Directory.Move(source, temporary);
            try { Directory.Move(temporary, destination); }
            catch { Directory.Move(temporary, source); throw; }
        }
        else Directory.Move(source, destination);
    }

    public string StandardisePortraitFolders(string gameRoot)
    {
        string root = CheckedGodotRoot(gameRoot);
        var preview = PreviewPortraitFolderMigration(root);
        if (preview.Changes.Count == 0) return "Portrait folders already follow character IDs.";
        var conflicts = preview.Changes.Where(x => x.Conflict is not null).ToList();
        if (conflicts.Count > 0)
            throw new InvalidOperationException("Resolve destination folder conflicts first:\n" +
                string.Join("\n", conflicts.Select(x => $"{x.CharacterId}: {x.Conflict}")));

        var textChanges = ScanResourceFiles(root, preview.Changes);
        string backup = _backups.Backup();
        string backupsRoot = Path.Combine(Path.GetDirectoryName(backup)!,
            "PortraitFolders_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupsRoot);
        var renamed = new List<(string From, string To)>();
        var rewritten = new List<(string Path, byte[] Bytes)>();

        using var tx = _db.BeginTransaction();
        try
        {
            foreach (var change in preview.Changes)
            {
                if (!change.RequiresPhysicalMove) continue;
                string source = Path.Combine(root, "Images", change.ActualSourceFolder!);
                string destination = Path.Combine(root, "Images", change.NewFolder);
                RenameDirectory(source, destination);
                renamed.Add((source, destination));
            }

            foreach (var change in preview.Changes)
            foreach (var (table, column) in ArtworkColumns)
            {
                using var cmd = Sql($"""
                    UPDATE "{table}"
                    SET "{column}"=$new || substr("{column}",length($old)+1)
                    WHERE lower(substr("{column}",1,length($old)))=lower($old);
                    """, tx);
                Add(cmd, "$old", change.OldPrefix);
                Add(cmd, "$new", change.NewPrefix);
                cmd.ExecuteNonQuery();
            }

            foreach (var change in textChanges)
            {
                byte[] originalBytes = File.ReadAllBytes(change.Path);
                string relative = Path.GetRelativePath(root, change.Path);
                string backupFile = Path.Combine(backupsRoot, "GodotResources", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                File.WriteAllBytes(backupFile, originalBytes);
                rewritten.Add((change.Path, originalBytes));
                File.WriteAllText(change.Path, change.Updated, Encoding.UTF8);
            }

            tx.Commit();
            try { File.WriteAllText(Path.Combine(backupsRoot, "migration-report.txt"),
                "SQLite backup: " + backup + Environment.NewLine +
                "Moved folders:\n" + string.Join("\n", renamed.Select(x => x.From + " -> " + x.To)) +
                "\nUpdated Godot text files: " + rewritten.Count + "\n" +
                "Existing artwork files were moved, not regenerated. Reopen Godot to re-import the moved files."); }
            catch (Exception) { /* Migration already committed; the SQLite backup remains available. */ }
            return $"Migrated {preview.Changes.Count} folder name(s), {preview.DatabaseReferences} database path reference(s), " +
                   $"and {rewritten.Count} Godot text resource(s). SQLite backup: {backup}";
        }
        catch (Exception failure)
        {
            try { tx.Rollback(); } catch { /* Transaction may already have finished. */ }
            var rollbackErrors = new List<string>();
            foreach (var change in rewritten.AsEnumerable().Reverse())
                try { File.WriteAllBytes(change.Path, change.Bytes); }
                catch (Exception ex) { rollbackErrors.Add("Restore " + change.Path + ": " + ex.Message); }
            foreach (var move in renamed.AsEnumerable().Reverse())
                try { RenameDirectory(move.To, move.From); }
                catch (Exception ex) { rollbackErrors.Add("Restore " + move.From + ": " + ex.Message); }
            throw new IOException("Image folder migration failed: " + failure.Message +
                (rollbackErrors.Count == 0 ? "\nDatabase and folder moves rolled back." :
                    "\nManual recovery may be needed:\n" + string.Join("\n", rollbackErrors)) +
                "\nSQLite backup: " + backup, failure);
        }
    }
}
