using System.IO;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    public void EnsureChoiceRequirementVisibilitySchema()
    {
        if (Exists(_db, "SELECT 1 FROM pragma_table_info('Options') WHERE name='UnmetRequirementBehaviour'"))
            return;

        _backupSource.Backup(); // One-time schema migration only; no backup for ordinary choice edits.
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.014_ChoiceRequirementVisibility.sql")
            ?? throw new InvalidOperationException("Missing choice-visibility migration resource.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public string ChoiceRequirementVisibility(int optionId)
    {
        using var cmd = Command("SELECT UnmetRequirementBehaviour FROM Options WHERE Id=$id");
        Add(cmd, "$id", optionId);
        return cmd.ExecuteScalar() as string
            ?? throw new InvalidOperationException("The dialogue choice no longer exists.");
    }

    public void SaveChoiceRequirementVisibility(int optionId, string behaviour)
    {
        if (behaviour is not ("LOCK" or "HIDE"))
            throw new InvalidOperationException("Choose 'Show as locked' or 'Hide completely'.");
        Write(tx =>
        {
            using var cmd = Command(
                "UPDATE Options SET UnmetRequirementBehaviour=$mode WHERE Id=$id", tx);
            Add(cmd, "$mode", behaviour);
            Add(cmd, "$id", optionId);
            Changed(cmd, update: true);
        });
    }
}
