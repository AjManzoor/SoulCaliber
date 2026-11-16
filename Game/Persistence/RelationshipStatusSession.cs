namespace SoulCaliber.Dialogue;

/// <summary>
/// Phase 12C prototype boot policy: a fresh process uses master-content starting
/// statuses; repeatedly opening/advancing dialogue scenes does NOT reset them.
/// A future load-game menu must call KeepSavedStatusesForLoad() BEFORE opening
/// the first interaction scene to preserve existing runtime overrides.
/// </summary>
public static class RelationshipStatusSession
{
    private static bool _sessionInitialised;

    public static void EnsureFreshPrototypeSession(SqliteRelationshipStatusStore store)
    {
        if (_sessionInitialised) return;
        store.ResetToDefaults();
        _sessionInitialised = true;
    }

    public static void StartNewGame(SqliteRelationshipStatusStore store)
    {
        store.ResetToDefaults();
        _sessionInitialised = true;
    }

    public static void KeepSavedStatusesForLoad() => _sessionInitialised = true;
}
