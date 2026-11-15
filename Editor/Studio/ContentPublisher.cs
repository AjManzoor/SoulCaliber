using Microsoft.Data.Sqlite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

public enum PublishSeverity { Error, Warning }

public sealed record PublishIssue(PublishSeverity Severity, string Location, string Detail)
{
    public override string ToString() => $"[{Severity}] {Location}: {Detail}";
}

public sealed class PublishReport
{
    public List<PublishIssue> Issues { get; } = [];
    public Dictionary<string, int> Counts { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ReferencedArtwork { get; } = new(StringComparer.Ordinal);
    public string? PublishedDirectory { get; internal set; }
    public int Errors => Issues.Count(x => x.Severity == PublishSeverity.Error);
    public int Warnings => Issues.Count(x => x.Severity == PublishSeverity.Warning);
    public bool IsValid => Errors == 0;

    internal void Error(string location, string detail) => Issues.Add(new(PublishSeverity.Error, location, detail));
    internal void Warn(string location, string detail) => Issues.Add(new(PublishSeverity.Warning, location, detail));

    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(!IsValid ? "VALIDATION FAILED — nothing published"
            : Warnings > 0 ? "VALIDATION PASSED WITH WARNINGS — publishing is allowed"
            : "VALIDATION PASSED");
        text.AppendLine($"{Errors} error(s) · {Warnings} warning(s) · {ReferencedArtwork.Count} referenced image resource(s)");
        foreach (var (name, count) in Counts.OrderBy(x => x.Key))
            text.AppendLine($"  {name}: {count}");
        if (Issues.Count > 0)
        {
            text.AppendLine();
            foreach (var issue in Issues) text.AppendLine(issue.ToString());
        }
        if (PublishedDirectory is not null)
        {
            text.AppendLine();
            text.AppendLine("CONTENT PACKAGE CREATED:");
            text.AppendLine(PublishedDirectory);
            text.AppendLine("Godot must still be exported separately. Install the Content folder beside its executable.");
        }
        return text.ToString();
    }
}

/// <summary>
/// Produces an independent SQLite snapshot for Godot's exported sidecar Content folder.
/// Never writes to the source DB, savegame, or Godot project.
/// Exporting the Godot executable and PCK remains Godot's responsibility.
/// </summary>
public static class ContentPublisher
{
    private const string SnapshotName = "content.db";
    private static readonly Regex VersionPattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,48}$", RegexOptions.Compiled);
    private static readonly string[] RequiredTables =
    [
        "Characters", "ReactionTypes", "ReactionImages", "Personalities", "Interactions", "Nodes", "Options",
        "Requirements", "ChanceModifiers", "Effects", "Lines", "SkillChecks", "NodeIntroductions",
        "ConditionalDialogueVariants", "ConditionalDialogueConditions", "CheckProfiles", "CheckProfileModifiers", "OptionCheckProfiles"
    ];
    private static readonly HashSet<string> Abilities = new(StringComparer.OrdinalIgnoreCase)
    { "Persuasion", "Intimidation", "Deception", "Charm", "Insight", "Resolve" };

    private sealed record CharacterRow(string Id, string Name, string DefaultImage, string WinImage, string LoseImage);
    private sealed record InteractionRow(long Id, string Key, string Title, string Start);
    private sealed record NodeRow(long Id, long InteractionId, string Key, long Round);
    private sealed record OptionRow(long Id, long NodeId, string Key, string Label, long Chance,
        string? SuccessNext, string? FailureNext, string SuccessReaction, string FailureReaction);
    private sealed record LineRow(long Id, long OptionId, string CharacterId, string? VisitorId,
        string? Outcome, string ImagePath);

    public static PublishReport Validate(string masterPath, string godotRoot)
    {
        string temp = Path.Combine(Path.GetTempPath(), "SoulCaliberPublisher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string snapshot = Path.Combine(temp, SnapshotName);
            Snapshot(masterPath, snapshot);
            return Inspect(snapshot, godotRoot);
        }
        finally { DeleteTemporary(temp); }
    }

    public static PublishReport Publish(string masterPath, string godotRoot, string outputRoot, string version)
    {
        version = (version ?? "").Trim();
        if (!VersionPattern.IsMatch(version) || version is "." or "..")
            throw new InvalidOperationException("Version must contain only letters, numbers, dots, hyphens and underscores (e.g. 0.1.0). ");
        string project = CheckGodotProject(godotRoot);
        string destinationRoot = Path.GetFullPath(outputRoot);
        if (IsInside(project, destinationRoot))
            throw new InvalidOperationException("Published output cannot be placed inside the Godot project; it could be accidentally packed into the game.");
        string finalDirectory = Path.Combine(destinationRoot, "v" + version);
        if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
            throw new IOException($"Version v{version} already exists. Choose a new version; published packages are never overwritten.");
        Directory.CreateDirectory(destinationRoot);
        string staging = Path.Combine(destinationRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "Content"));
        try
        {
            string snapshotPath = Path.Combine(staging, "Content", SnapshotName);
            Snapshot(masterPath, snapshotPath);
            PublishReport report = Inspect(snapshotPath, project);
            if (!report.IsValid) return report; // finally removes the incomplete staging package.

            var assets = report.ReferencedArtwork.OrderBy(x => x, StringComparer.Ordinal).Select(resource =>
            {
                string file = ResolveAsset(project, resource);
                return new
                {
                    resourcePath = resource,
                    sha256 = FileHash(file),
                    bytes = new FileInfo(file).Length
                };
            }).ToArray();
            var manifest = new
            {
                format = "SoulCaliberContentPackage/1",
                version,
                createdUtc = DateTimeOffset.UtcNow.ToString("O"),
                sqliteUserVersion = ReadUserVersion(snapshotPath),
                database = new
                {
                    path = "Content/content.db",
                    sha256 = FileHash(snapshotPath),
                    bytes = new FileInfo(snapshotPath).Length
                },
                counts = report.Counts.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
                referencedGodotAssets = assets,
                warnings = report.Issues.Where(x => x.Severity == PublishSeverity.Warning)
                    .Select(x => $"{x.Location}: {x.Detail}").ToArray()
            };
            File.WriteAllText(Path.Combine(staging, "Content", "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "PUBLISH_INSTRUCTIONS.txt"),
                "SoulCaliber content package v" + version + Environment.NewLine + Environment.NewLine +
                "1. In Godot, export your Windows .NET game to a separate folder. Use the 'Export all resources in the project' mode " +
                "so portraits dynamically loaded from res:// paths are included in its PCK." + Environment.NewLine +
                "2. Place this package's Content folder beside your exported .exe (or use Studio's Install button)." + Environment.NewLine +
                "3. Expected layout: SoulCaliber.exe, SoulCaliber.pck, Content/content.db, Content/manifest.json." + Environment.NewLine +
                "4. Release builds must read <executable folder>/Content/content.db with SqliteOpenMode.ReadOnly. " +
                "Do not ship the development-only absolute project.godot database setting." + Environment.NewLine +
                "5. Player saves remain in Godot user://soulcaliber_savegame.db. Exported builds must be tested on a clean machine/folder." + Environment.NewLine +
                "6. Artwork files are NOT copied to Content. Godot must import them and include them inside the exported PCK." + Environment.NewLine +
                "7. Phase 7 conditional dialogue requires the THREE GodotPatch/Dialogue files to be compiled in the exported build." + Environment.NewLine,
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "validation-report.txt"), report.Describe(), new UTF8Encoding(false));
            Directory.Move(staging, finalDirectory);
            report.PublishedDirectory = finalDirectory;
            return report;
        }
        finally { DeleteTemporary(staging); }
    }

    public static string InstallIntoExport(string publishedDirectory, string exportedGameDirectory)
    {
        string packageFolder = Path.GetFullPath(publishedDirectory);
        string gameFolder = Path.GetFullPath(exportedGameDirectory);
        string sourceContent = Path.Combine(packageFolder, "Content");
        string sourceDb = Path.Combine(sourceContent, SnapshotName);
        string sourceManifest = Path.Combine(sourceContent, "manifest.json");
        if (!File.Exists(sourceDb) || !File.Exists(sourceManifest))
            throw new InvalidOperationException("Select a published package containing Content/content.db and Content/manifest.json.");
        if (!Directory.Exists(gameFolder) || File.Exists(Path.Combine(gameFolder, "project.godot")))
            throw new InvalidOperationException("Select an exported game folder, not the Godot source project.");
        if (!Directory.EnumerateFiles(gameFolder, "*.exe", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidOperationException("No .exe was found. Export your Godot Windows build into this folder first.");
        if (IsInside(gameFolder, packageFolder) || IsInside(packageFolder, gameFolder))
            throw new InvalidOperationException("Export folder must be different from the published package folder.");
        string destination = Path.Combine(gameFolder, "Content");
        Directory.CreateDirectory(destination);
        string suffix = ".staging-" + Guid.NewGuid().ToString("N");
        string dbTemp = Path.Combine(destination, SnapshotName + suffix);
        string manifestTemp = Path.Combine(destination, "manifest.json" + suffix);
        try
        {
            // Stage and verify both files before replacing either one.
            File.Copy(sourceDb, dbTemp);
            File.Copy(sourceManifest, manifestTemp);
            if (FileHash(dbTemp) != FileHash(sourceDb) || FileHash(manifestTemp) != FileHash(sourceManifest))
                throw new IOException("Content copy verification failed.");

            var changes = new List<(string Target, string Incoming, string? Backup)>();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "-" + Guid.NewGuid().ToString("N")[..6];
            foreach (var (name, incoming) in new[] { (SnapshotName, dbTemp), ("manifest.json", manifestTemp) })
            {
                string target = Path.Combine(destination, name);
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = Path.Combine(destination, name + ".backup-" + stamp);
                    File.Copy(target, backup); // Keep old release for recovery.
                }
                changes.Add((target, incoming, backup));
            }
            var installed = new List<(string Target, string? Backup)>();
            try
            {
                foreach (var change in changes)
                {
                    File.Move(change.Incoming, change.Target, overwrite: true);
                    installed.Add((change.Target, change.Backup));
                }
            }
            catch (Exception installationError)
            {
                var rollbackErrors = new List<Exception>();
                foreach (var previous in installed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (previous.Backup is not null)
                            File.Copy(previous.Backup, previous.Target, overwrite: true);
                        else
                            File.Delete(previous.Target);
                    }
                    catch (Exception rollbackError) { rollbackErrors.Add(rollbackError); }
                }
                if (rollbackErrors.Count > 0)
                    throw new AggregateException("Installation failed and automatic rollback was incomplete. Restore the .backup files manually.",
                        new[] { installationError }.Concat(rollbackErrors));
                throw;
            }
            return destination;
        }
        finally
        {
            if (File.Exists(dbTemp)) File.Delete(dbTemp);
            if (File.Exists(manifestTemp)) File.Delete(manifestTemp);
        }
    }

    private static void Snapshot(string masterPath, string snapshotPath)
    {
        string master = Path.GetFullPath(masterPath);
        if (!File.Exists(master)) throw new FileNotFoundException("Master database not found.", master);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = master, Mode = SqliteOpenMode.ReadOnly, ForeignKeys = true, Pooling = false }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = snapshotPath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, Pooling = false }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination); // Consistent even if the master is in WAL mode.
        using var command = new SqliteCommand("PRAGMA journal_mode=DELETE;", destination);
        string? mode = Convert.ToString(command.ExecuteScalar());
        if (!string.Equals(mode, "delete", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Unable to create a self-contained SQLite snapshot (journal mode: {mode}).");
    }

    private static PublishReport Inspect(string snapshotPath, string godotRoot)
    {
        string root = CheckGodotProject(godotRoot);
        var report = new PublishReport();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = snapshotPath, Mode = SqliteOpenMode.ReadOnly, ForeignKeys = true, Pooling = false }.ToString());
        db.Open();
        using (var integrity = new SqliteCommand("PRAGMA integrity_check;", db))
        {
            if (!string.Equals(Convert.ToString(integrity.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
                report.Error("SQLite", "integrity_check failed.");
        }
        foreach (string table in RequiredTables)
        {
            using var exists = new SqliteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name", db);
            exists.Parameters.AddWithValue("$name", table);
            if (exists.ExecuteScalar() is null) report.Error("Schema", $"Missing table '{table}'.");
        }
        if (report.Errors > 0) return report;
        using (var keys = new SqliteCommand("PRAGMA foreign_key_check;", db))
        using (var reader = keys.ExecuteReader())
        {
            while (reader.Read())
                report.Error("Foreign keys", $"{reader.GetString(0)} row {reader.GetValue(1)} references missing {reader.GetString(2)}.");
        }
        foreach (var table in RequiredTables)
        {
            using var count = new SqliteCommand($"SELECT COUNT(*) FROM \"{table}\"", db);
            report.Counts[table] = Convert.ToInt32((long)count.ExecuteScalar()!);
        }

        var characters = Read(db, "SELECT Id, Name, DefaultImage, WinImage, LoseImage FROM Characters", r =>
            new CharacterRow(r.GetString(0), r.GetString(1), Safe(r, 2), Safe(r, 3), Safe(r, 4)));
        var characterIds = characters.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (characters.Count != characterIds.Count)
            report.Error("Characters", "Duplicate character IDs differing only by letter case (Godot compares IDs case-insensitively).");
        if (characterIds.Count == 0) report.Error("Characters", "The roster is empty.");
        foreach (var ch in characters)
        {
            if (string.IsNullOrWhiteSpace(ch.Id) || string.IsNullOrWhiteSpace(ch.Name))
                report.Error("Characters", "A character is missing an ID or display name.");
            CheckAsset(report, root, ch.DefaultImage, $"{ch.Id} default portrait");
            CheckAsset(report, root, ch.WinImage, $"{ch.Id} success portrait");
            CheckAsset(report, root, ch.LoseImage, $"{ch.Id} failure portrait");
        }

        var reactionTypes = Read(db, "SELECT Key, DisplayName, SortOrder FROM ReactionTypes", r =>
            (Key: r.GetString(0), Name: r.GetString(1), SortOrder: r.GetInt64(2)));
        var validReactions = reactionTypes.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (validReactions.Count == 0) report.Error("ReactionTypes", "The reaction catalogue is empty.");
        foreach (var type in reactionTypes)
        {
            if (string.IsNullOrWhiteSpace(type.Name) || string.IsNullOrWhiteSpace(type.Key))
                report.Error("ReactionTypes", "A reaction type has a blank key or display name.");
        }

        var reactions = Read(db, "SELECT CharacterId, ReactionKey, ImagePath FROM ReactionImages", r =>
            (CharacterId: r.GetString(0), ReactionKey: r.GetString(1), ImagePath: Safe(r, 2)));
        foreach (var duplicates in reactions.GroupBy(x => (x.CharacterId.ToUpperInvariant(), x.ReactionKey.ToUpperInvariant()))
                     .Where(x => x.Count() > 1))
            report.Error("ReactionImages", $"Duplicate case-insensitive reaction key for character {duplicates.Key.Item1}.");
        foreach (var reaction in reactions)
        {
            if (!characterIds.Contains(reaction.CharacterId))
                report.Error("ReactionImages", $"Unknown character {reaction.CharacterId}.");
            if (!validReactions.Contains(reaction.ReactionKey))
                report.Error("ReactionImages", $"Unknown reaction type '{reaction.ReactionKey}' for {reaction.CharacterId}.");
            CheckAsset(report, root, reaction.ImagePath, $"{reaction.CharacterId} / {reaction.ReactionKey}");
        }

        var personality = Read(db,
            "SELECT CharacterId, Persuasion, Intimidation, Deception, Charm, Insight, Resolve FROM Personalities",
            r => (CharacterId: r.GetString(0), Stats: Enumerable.Range(1, 6).Select(i => r.GetInt64(i)).ToArray()));
        var withPersonality = personality.Select(x => x.CharacterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (withPersonality.Count != personality.Count)
            report.Error("Personalities", "Duplicate character personality rows differing only by letter case.");
        foreach (var p in personality)
        {
            if (!characterIds.Contains(p.CharacterId)) report.Error("Personalities", $"Unknown character {p.CharacterId}.");
            if (p.Stats.Any(x => x is < 0 or > 100)) report.Error("Personalities", $"Stats for {p.CharacterId} must be 0–100.");
        }
        foreach (var ch in characters.Where(x => !withPersonality.Contains(x.Id)))
            report.Warn("Personalities", $"{ch.Id} has no personality row; Godot will use the engine defaults.");

        // Phase 6C: character associations are per encounter, not per interaction definition.
        var interactions = Read(db, "SELECT Id, Key, Title, StartNodeKey FROM Interactions", r =>
            new InteractionRow(r.GetInt64(0), r.GetString(1), Safe(r, 2), Safe(r, 3)));
        var nodes = Read(db, "SELECT Id, InteractionId, Key, Round FROM Nodes", r =>
            new NodeRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3)));
        var options = Read(db,
            "SELECT Id, NodeId, Key, Label, BaseSuccessChance, SuccessNextNodeKey, FailureNextNodeKey, SuccessTargetReaction, FailureTargetReaction FROM Options",
            r => new OptionRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), Safe(r, 3), r.GetInt64(4), NullableText(r, 5), NullableText(r, 6), Safe(r, 7), Safe(r, 8)));
        if (interactions.Count == 0) report.Error("Interactions", "No interaction types exist.");
        if (interactions.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            report.Error("Interactions", "Duplicate interaction keys differing only by letter case.");
        var interactionIds = interactions.Select(x => x.Id).ToHashSet();
        var nodeLookup = nodes.ToDictionary(x => x.Id);
        var optionIds = options.Select(x => x.Id).ToHashSet();
        foreach (var inter in interactions)
        {
            if (string.IsNullOrWhiteSpace(inter.Key) || string.IsNullOrWhiteSpace(inter.Title))
                report.Error($"Interaction {inter.Id}", "Missing key or title.");
            var ownNodes = nodes.Where(x => x.InteractionId == inter.Id).ToList();
            if (!ownNodes.Any(n => string.Equals(n.Key, inter.Start, StringComparison.OrdinalIgnoreCase)))
                report.Error(inter.Key, $"Start node '{inter.Start}' does not exist in this interaction.");
            if (ownNodes.GroupBy(n => n.Key, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                report.Error(inter.Key, "Duplicate node key (case-insensitive).");
        }
        foreach (var node in nodes)
        {
            if (!interactionIds.Contains(node.InteractionId)) report.Error("Nodes", $"Node {node.Id} references a missing interaction.");
            if (node.Round < 1) report.Error("Nodes", $"Node {node.Key} has invalid round {node.Round}.");
            var choices = options.Where(o => o.NodeId == node.Id).ToList();
            if (choices.Count == 0) report.Error("Nodes", $"Node '{node.Key}' has no choices.");
            if (choices.GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                report.Error("Nodes", $"Duplicate option key in node '{node.Key}'.");
        }
        foreach (var option in options)
        {
            if (!nodeLookup.TryGetValue(option.NodeId, out var parent))
            {
                report.Error("Options", $"Option '{option.Key}' has missing node {option.NodeId}.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(option.Key) || string.IsNullOrWhiteSpace(option.Label))
                report.Error("Options", $"Option {option.Id} has missing key or label.");
            if (option.Chance is < 0 or > 100)
                report.Error("Options", $"Option {option.Key} has invalid base chance {option.Chance}.");
            if (!validReactions.Contains(option.SuccessReaction))
                report.Error("Options", $"Option '{option.Key}' has unknown success reaction '{option.SuccessReaction}'.");
            if (!validReactions.Contains(option.FailureReaction))
                report.Error("Options", $"Option '{option.Key}' has unknown failure reaction '{option.FailureReaction}'.");
            foreach (var next in new[] { option.SuccessNext, option.FailureNext })
            {
                if (string.IsNullOrWhiteSpace(next)) continue;
                if (!nodes.Any(n => n.InteractionId == parent.InteractionId && string.Equals(n.Key, next, StringComparison.OrdinalIgnoreCase)))
                    report.Error("Options", $"Option '{option.Key}' leads to unknown node '{next}'.");
            }
        }
        var lines = Read(db, "SELECT Id, OptionId, CharacterId, VisitorId, Outcome, Text, ImagePath FROM Lines", r =>
            new LineRow(r.GetInt64(0), r.GetInt64(1), Safe(r, 2), NullableText(r, 3), NullableText(r, 4), Safe(r, 6)));
        foreach (var line in lines)
        {
            if (!optionIds.Contains(line.OptionId)) report.Error("Lines", $"Phrase {line.Id} references nonexistent choice {line.OptionId}.");
            if (!characterIds.Contains(line.CharacterId)) report.Error("Lines", $"Phrase {line.Id} references unknown speaker '{line.CharacterId}'.");
            if (!string.IsNullOrWhiteSpace(line.VisitorId) && !characterIds.Contains(line.VisitorId))
                report.Error("Lines", $"Phrase {line.Id} references unknown other character '{line.VisitorId}'.");
            if (!string.IsNullOrWhiteSpace(line.Outcome) && !new[] { "SUCCESS", "FAILURE" }.Contains(line.Outcome, StringComparer.OrdinalIgnoreCase))
                report.Error("Lines", $"Phrase {line.Id} has invalid outcome '{line.Outcome}'.");
            if (!string.IsNullOrWhiteSpace(line.ImagePath))
                CheckAsset(report, root, line.ImagePath, $"Phrase {line.Id} image");
        }
        foreach (var duplicates in lines.GroupBy(x => (x.OptionId, x.CharacterId.ToUpperInvariant(),
                     (x.VisitorId ?? "").ToUpperInvariant(), (x.Outcome ?? "").ToUpperInvariant())).Where(x => x.Count() > 1))
            report.Error("Lines", $"Duplicate phrase identity for option {duplicates.Key.Item1}, character {duplicates.Key.Item2}.");

        // Phase 5: validate author-defined introductions and their precedence scopes.
        var introductions = Read(db,
            "SELECT Id, NodeId, CharacterId, VisitorId, Text FROM NodeIntroductions",
            r => (Id: r.GetInt64(0), NodeId: r.GetInt64(1), Speaker: Safe(r, 2),
                  Visitor: NullableText(r, 3), Text: Safe(r, 4)));
        foreach (var intro in introductions)
        {
            if (!nodeLookup.ContainsKey(intro.NodeId))
                report.Error("NodeIntroductions", $"Introduction {intro.Id} has an unknown dialogue node {intro.NodeId}.");
            if (!characterIds.Contains(intro.Speaker))
                report.Error("NodeIntroductions", $"Introduction {intro.Id} references unknown speaker {intro.Speaker}.");
            if (intro.Visitor is not null && !characterIds.Contains(intro.Visitor))
                report.Error("NodeIntroductions", $"Introduction {intro.Id} references unknown visitor {intro.Visitor}.");
            if (intro.Visitor is not null && string.Equals(intro.Visitor, intro.Speaker, StringComparison.OrdinalIgnoreCase))
                report.Error("NodeIntroductions", $"Introduction {intro.Id} has the same speaker and visitor.");
            if (string.IsNullOrWhiteSpace(intro.Text))
                report.Error("NodeIntroductions", $"Introduction {intro.Id} has no text.");
        }
        foreach (var duplicate in introductions.GroupBy(x =>
                     (x.NodeId, x.Speaker.ToUpperInvariant(), (x.Visitor ?? "").ToUpperInvariant()))
                 .Where(g => g.Count() > 1))
            report.Error("NodeIntroductions", $"Duplicate introduction identity for node {duplicate.Key.NodeId}, speaker {duplicate.Key.Item2}.");

        // Phase 7: structural validation of conditional variants and predicates.
        // Broken conditions block a release; missing optional artwork remains a warning.
        var variants = Read(db, @"SELECT Id,NodeId,OptionId,CharacterId,VisitorId,Outcome,Priority,Text,ImagePath
            FROM ConditionalDialogueVariants", r =>
            (Id:r.GetInt64(0), NodeId:r.IsDBNull(1)?(long?)null:r.GetInt64(1),
             OptionId:r.IsDBNull(2)?(long?)null:r.GetInt64(2), Speaker:NullableText(r,3),
             Other:NullableText(r,4), Outcome:NullableText(r,5), Priority:r.GetInt64(6),
             Text:Safe(r,7), Image:Safe(r,8)));
        var variantIds = variants.Select(v=>v.Id).ToHashSet();
        var conditions = Read(db, @"SELECT Id,VariantId,Direction,Attribute,Minimum,Maximum
            FROM ConditionalDialogueConditions",r=>(Id:r.GetInt64(0),VariantId:r.GetInt64(1),
            Direction:Safe(r,2),Attribute:Safe(r,3),
            Min:r.IsDBNull(4)?(long?)null:r.GetInt64(4),
            Max:r.IsDBNull(5)?(long?)null:r.GetInt64(5)));
        string[] allowedDirections = ["ACTOR_TO_PARTNER","PARTNER_TO_ACTOR"];
        string[] allowedAttributes = ["Love","Fear","Allyship","Respect","Trust","Romance"];
        foreach (var v in variants)
        {
            string location = $"Conditional variant {v.Id}";
            if ((v.NodeId is null) == (v.OptionId is null)) report.Error(location,"Expected exactly one node or option.");
            if (v.NodeId is long nodeId && !nodeLookup.ContainsKey(nodeId)) report.Error(location,"Unknown node.");
            if (v.OptionId is long optionId && !optionIds.Contains(optionId)) report.Error(location,"Unknown choice.");
            if (v.Speaker is not null && !characterIds.Contains(v.Speaker)) report.Error(location,"Unknown speaker.");
            if (v.Other is not null && !characterIds.Contains(v.Other)) report.Error(location,"Unknown other character.");
            if (v.Speaker is not null && v.Other is not null && string.Equals(v.Other,v.Speaker,StringComparison.OrdinalIgnoreCase))
                report.Error(location,"Speaker and other character are identical.");
            if (v.NodeId is not null && (v.Outcome is not null || v.Image.Length > 0))
                report.Error(location,"An introduction cannot set an outcome or image.");
            if (v.Outcome is not null && v.Outcome is not ("SUCCESS" or "FAILURE"))
                report.Error(location,"Invalid outcome.");
            if (v.Priority is < 0 or > 1000) report.Error(location,"Priority must be 0–1000.");
            if (string.IsNullOrWhiteSpace(v.Text)) report.Error(location,"Missing text.");
            if (conditions.All(c=>c.VariantId!=v.Id))report.Error(location,"At least one condition is required.");
            if (!string.IsNullOrWhiteSpace(v.Image))CheckAsset(report,root,v.Image,location+" portrait");
        }
        foreach(var c in conditions)
        {
            string location=$"Conditional clause {c.Id}";
            if (!variantIds.Contains(c.VariantId))report.Error(location,"Unknown variant.");
            if (!allowedDirections.Contains(c.Direction,StringComparer.OrdinalIgnoreCase))report.Error(location,"Invalid direction.");
            if (!allowedAttributes.Contains(c.Attribute,StringComparer.OrdinalIgnoreCase))report.Error(location,"Invalid attribute.");
            long lower = c.Attribute.Equals("Fear",StringComparison.OrdinalIgnoreCase) ||
                         c.Attribute.Equals("Romance",StringComparison.OrdinalIgnoreCase) ? 0 : -100;
            if (c.Min is null && c.Max is null || c.Min is long lo && (lo < lower || lo > 100) ||
                c.Max is long hi && (hi < lower || hi > 100) ||
                c.Min is long a && c.Max is long b && a>b)
                report.Error(location,"Invalid minimum/maximum bounds.");
        }
        foreach(var duplicate in conditions.GroupBy(c=>(c.VariantId,c.Direction.ToUpperInvariant(),c.Attribute.ToUpperInvariant()))
                     .Where(x=>x.Count()>1))
            report.Error("Conditional dialogue",$"Duplicate condition for variant {duplicate.Key.VariantId}.");
        foreach(var duplicate in variants.GroupBy(v=>(v.NodeId,v.OptionId,(v.Speaker??"").ToUpperInvariant(),
                 (v.Other??"").ToUpperInvariant(),(v.Outcome??"").ToUpperInvariant(),v.Priority)).Where(g=>g.Count()>1))
            report.Warn("Conditional dialogue",$"Multiple variants have the same identity and priority; lowest ID wins if their conditions overlap (e.g. #{duplicate.Min(x=>x.Id)}).");

        // Phase 6: the dialogue engine reads these tables as game rules, so
        // validate records authored in either the friendly editor OR Advanced Tables.
        var validDirections = new[] { "ACTOR_TO_PARTNER", "PARTNER_TO_ACTOR" };
        var validAttributes = new[] { "Love", "Fear", "Allyship", "Respect", "Trust", "Romance" };
        var requirements = Read(db,
            "SELECT Id, OptionId, Direction, Attribute, Minimum, Maximum FROM Requirements",
            r => (Id: r.GetInt64(0), OptionId: r.GetInt64(1), Direction: Safe(r, 2),
                Attribute: Safe(r, 3), Min: r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                Max: r.IsDBNull(5) ? (long?)null : r.GetInt64(5)));
        foreach (var requirement in requirements)
        {
            if (!optionIds.Contains(requirement.OptionId))
                report.Error("Requirements", $"Requirement {requirement.Id} refers to missing choice {requirement.OptionId}.");
            if (!validDirections.Contains(requirement.Direction, StringComparer.OrdinalIgnoreCase))
                report.Error("Requirements", $"Requirement {requirement.Id} has invalid direction '{requirement.Direction}'.");
            if (!validAttributes.Contains(requirement.Attribute, StringComparer.OrdinalIgnoreCase))
                report.Error("Requirements", $"Requirement {requirement.Id} has unknown attribute '{requirement.Attribute}'.");
            if (requirement.Min is null && requirement.Max is null)
                report.Error("Requirements", $"Requirement {requirement.Id} has no bounds.");
            long minValue = requirement.Attribute.Equals("Fear", StringComparison.OrdinalIgnoreCase) ||
                            requirement.Attribute.Equals("Romance", StringComparison.OrdinalIgnoreCase) ? 0 : -100;
            if (requirement.Min is long min && (min < minValue || min > 100) ||
                requirement.Max is long max && (max < minValue || max > 100) ||
                requirement.Min is long lower && requirement.Max is long upper && lower > upper)
                report.Error("Requirements", $"Requirement {requirement.Id} has inconsistent relationship bounds.");
        }

        var effects = Read(db,
            "SELECT Id, OptionId, Outcome, Direction, Attribute, Delta FROM Effects",
            r => (Id: r.GetInt64(0), OptionId: r.GetInt64(1), Outcome: Safe(r, 2),
                  Direction: Safe(r, 3), Attribute: Safe(r, 4)));
        foreach (var effect in effects)
        {
            if (!optionIds.Contains(effect.OptionId))
                report.Error("Effects", $"Effect {effect.Id} refers to missing choice {effect.OptionId}.");
            if (!new[] { "SUCCESS", "FAILURE" }.Contains(effect.Outcome, StringComparer.OrdinalIgnoreCase))
                report.Error("Effects", $"Effect {effect.Id} has invalid outcome '{effect.Outcome}'.");
            if (!validDirections.Contains(effect.Direction, StringComparer.OrdinalIgnoreCase))
                report.Error("Effects", $"Effect {effect.Id} has invalid direction '{effect.Direction}'.");
            if (!validAttributes.Contains(effect.Attribute, StringComparer.OrdinalIgnoreCase))
                report.Error("Effects", $"Effect {effect.Id} has invalid attribute '{effect.Attribute}'.");
        }

        var modifiers = Read(db,
            "SELECT Id, OptionId, Direction, Attribute, Factor FROM ChanceModifiers",
            r => (Id: r.GetInt64(0), OptionId: r.GetInt64(1), Direction: Safe(r, 2),
                  Attribute: Safe(r, 3), Factor: r.GetDouble(4)));
        foreach (var modifier in modifiers)
        {
            if (!optionIds.Contains(modifier.OptionId))
                report.Error("ChanceModifiers", $"Modifier {modifier.Id} refers to missing choice {modifier.OptionId}.");
            if (!validDirections.Contains(modifier.Direction, StringComparer.OrdinalIgnoreCase))
                report.Error("ChanceModifiers", $"Modifier {modifier.Id} has invalid direction '{modifier.Direction}'.");
            if (!validAttributes.Contains(modifier.Attribute, StringComparer.OrdinalIgnoreCase))
                report.Error("ChanceModifiers", $"Modifier {modifier.Id} has invalid attribute '{modifier.Attribute}'.");
            if (!double.IsFinite(modifier.Factor))
                report.Error("ChanceModifiers", $"Modifier {modifier.Id} has a non-finite probability factor.");
        }

        // Profile formula validation before referencing SkillChecks.
        var profileKeys = Read(db,"SELECT Key,Name FROM CheckProfiles",
            r => (Key:Safe(r,0), Name:Safe(r,1)));
        var knownProfiles = profileKeys.Select(p=>p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!knownProfiles.Contains("STANDARD"))
            report.Error("CheckProfiles", "STANDARD default profile is required (even without a skill check).");
        foreach(var profile in profileKeys)
        {
            if (string.IsNullOrWhiteSpace(profile.Key) || string.IsNullOrWhiteSpace(profile.Name))
                report.Error("CheckProfiles", "Every profile needs a key and name.");
        }
        var formulaTerms = Read(db,"SELECT ProfileKey,Direction,Attribute,Factor FROM CheckProfileModifiers",
            r => (Profile:Safe(r,0), Direction:Safe(r,1), Attribute:Safe(r,2), Factor:r.GetDouble(3)));
        foreach (var term in formulaTerms)
        {
            if (!knownProfiles.Contains(term.Profile)) report.Error("CheckProfileModifiers", $"Unknown profile '{term.Profile}'.");
            if (!new[]{"PARTNER_TO_ACTOR","ACTOR_TO_PARTNER"}.Contains(term.Direction,StringComparer.OrdinalIgnoreCase))
                report.Error("CheckProfileModifiers", $"Unknown direction '{term.Direction}'.");
            if (!new[]{"Love","Fear","Allyship","Respect","Trust","Romance"}.Contains(term.Attribute,StringComparer.OrdinalIgnoreCase))
                report.Error("CheckProfileModifiers", $"Unknown attribute '{term.Attribute}'.");
            if (!double.IsFinite(term.Factor) || term.Factor < -10 || term.Factor > 10)
                report.Error("CheckProfileModifiers", $"Non-finite/out-of-range factor for {term.Profile}.{term.Attribute}.");
        }

        var profileAssignments = Read(db, "SELECT OptionId,ProfileKey FROM OptionCheckProfiles",
            r => (OptionId:r.GetInt64(0),ProfileKey:Safe(r,1)));
        foreach(var assigned in profileAssignments)
        {
            if (!optionIds.Contains(assigned.OptionId))
                report.Error("OptionCheckProfiles", $"Unknown choice {assigned.OptionId}.");
            if (!knownProfiles.Contains(assigned.ProfileKey))
                report.Error("OptionCheckProfiles", $"Unknown assigned profile '{assigned.ProfileKey}'.");
        }

        var skillChecks = Read(db,
            "SELECT OptionId, ActorSkill, DefenderSkill, CheckMode, ActorFactor, DefenderFactor FROM SkillChecks",
            r => (OptionId: r.GetInt64(0), Actor: Safe(r, 1), Defender: Safe(r, 2), Mode: Safe(r, 3), ActorFactor: r.GetDouble(4), DefenderFactor: r.GetDouble(5)));
        foreach (var skill in skillChecks)
        {
            if (!optionIds.Contains(skill.OptionId)) report.Error("SkillChecks", $"Unknown choice {skill.OptionId}.");
            if (!Abilities.Contains(skill.Actor) || !Abilities.Contains(skill.Defender))
                report.Error("SkillChecks", $"Unknown ability on choice {skill.OptionId}.");
            if (!knownProfiles.Contains(skill.Mode))
                report.Error("SkillChecks", $"Unknown check profile '{skill.Mode}' on choice {skill.OptionId}.");
            if (!double.IsFinite(skill.ActorFactor) || !double.IsFinite(skill.DefenderFactor))
                report.Error("SkillChecks", $"Non-finite skill factor on choice {skill.OptionId}.");
        }
        return report;
    }

    private static List<T> Read<T>(SqliteConnection db, string sql, Func<SqliteDataReader, T> convert)
    {
        using var cmd = new SqliteCommand(sql, db);
        using var reader = cmd.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(convert(reader));
        return result;
    }

    private static string Safe(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? "" : reader.GetString(index);
    private static string? NullableText(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    // Artwork is optional: Godot uses character or neutral portrait fallbacks when
    // a configured reaction/phrase image cannot be found. Report it, but do not block publishing.
    private static void CheckAsset(PublishReport report, string root, string resource, string owner)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            report.Warn(owner, "No image assigned (Godot may show a fallback).");
            return;
        }
        try
        {
            string path = ResolveAsset(root, resource);
            if (!File.Exists(path))
                report.Warn(owner, $"Image is missing: {resource}");
            else
                report.ReferencedArtwork.Add(resource);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Unsafe or malformed res:// paths are still errors, not merely missing assets.
            report.Error(owner, $"Invalid Godot image path '{resource}': {e.Message}");
        }
    }

    private static string ResolveAsset(string root, string resource)
    {
        if (!resource.StartsWith("res://", StringComparison.Ordinal) || resource.Contains('\\'))
            throw new ArgumentException("Expected an exact res:// path using forward slashes.");
        string part = resource[6..];
        if (part.Length == 0 || part.StartsWith('/') || part.Contains(':') ||
            part.Split('/').Any(s => s is "" or "." or ".."))
            throw new ArgumentException("Path is empty, has invalid segments or attempts directory traversal.");
        string resolved = Path.GetFullPath(Path.Combine(root, part.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(root, resolved)) throw new ArgumentException("Path resolves outside the Godot project.");
        return resolved;
    }

    private static bool IsInside(string root, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string CheckGodotProject(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new InvalidOperationException("Locate project.godot first.");
        string root = Path.GetFullPath(projectRoot);
        if (!File.Exists(Path.Combine(root, "project.godot")))
            throw new FileNotFoundException("The selected directory does not contain project.godot.", root);
        return root;
    }

    private static int ReadUserVersion(string sqlitePath)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        using var cmd = new SqliteCommand("PRAGMA user_version;", db);
        return Convert.ToInt32((long)cmd.ExecuteScalar()!);
    }

    private static string FileHash(string filename)
    {
        using var input = File.OpenRead(filename);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static void DeleteTemporary(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch { /* Leave temporary files rather than masking the original publishing error. */ }
    }
}
