using System;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Guards the terminal Finish action for one launched conversation. A persistence
/// failure leaves it retryable; a committed completion cannot be written twice,
/// even if presentation throws after the save. Recreate/reset for a NEW session.
/// This is session-local; persistent uniqueness lives in SQLite.
/// </summary>
public sealed class SpecialEncounterCompletionGuard
{
    private bool _saving;
    public bool HasFinished { get; private set; }

    public bool Finish(Action recordCompletion)
    {
        if (recordCompletion is null) throw new ArgumentNullException(nameof(recordCompletion));
        if (_saving) throw new InvalidOperationException("Encounter completion is already being saved.");
        if (HasFinished) return false;

        _saving = true;
        try
        {
            recordCompletion();
            HasFinished = true;
            return true;
        }
        finally
        {
            _saving = false;
        }
    }

    public void Reset()
    {
        if (_saving) throw new InvalidOperationException("Cannot reset while completion is being saved.");
        HasFinished = false;
    }
}
