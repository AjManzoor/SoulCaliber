using System;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Prevents duplicate commits for one presented dialogue choice opportunity.
/// A failed persistence attempt retains its already-calculated outcome for retry;
/// a successful attempt returns the original result without saving again.
/// Cleared only when the conversation starts over or advances to the next node.
/// This is session-local protection, NOT durable idempotency after restarting the game.
/// </summary>
internal sealed class ChoiceResolutionGuard<TPrepared, TResult> where TResult : class
{
    private sealed record Attempt(string NodeKey, string OptionKey, TPrepared Prepared);

    private Attempt? _attempt;
    private TResult? _committedResult;
    private bool _committed;
    private bool _resolving;

    public string? PendingOptionKey => !_committed ? _attempt?.OptionKey : null;
    public bool HasCommittedChoice => _committed;
    public bool IsResolving => _resolving;

    public TResult Execute(string nodeKey, string optionKey,
        Func<TPrepared> prepare, Func<TPrepared, TResult> commit)
    {
        if (_resolving)
            throw new InvalidOperationException("The dialogue choice is already being resolved.");

        if (string.IsNullOrWhiteSpace(nodeKey) || string.IsNullOrWhiteSpace(optionKey))
            throw new ArgumentException("A dialogue node and option are required.");

        if (_attempt is not null &&
            (!string.Equals(_attempt.NodeKey, nodeKey, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(_attempt.OptionKey, optionKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(_committed
                ? "This dialogue round has already committed a different choice."
                : "A failed save must retry the same resolved choice; it cannot be rerolled or replaced.");
        }

        if (_committed)
            return _committedResult!; // Previously committed: NEVER reapply effects.

        _resolving = true;
        try
        {
            // Prepare once: random roll, outcome, lines, trait adjustments and memory targets.
            // If persistence throws, the prepared choice is intentionally retained.
            _attempt ??= new Attempt(nodeKey, optionKey, prepare());
            TResult result = commit(_attempt.Prepared);
            _committedResult = result;
            _committed = true;
            return result;
        }
        finally
        {
            _resolving = false;
        }
    }

    /// <summary>Only for an authored next node or a deliberately new conversation.</summary>
    public void Clear()
    {
        if (_resolving)
            throw new InvalidOperationException("Cannot leave a dialogue choice while it is resolving.");
        _attempt = null;
        _committedResult = null;
        _committed = false;
    }
}
