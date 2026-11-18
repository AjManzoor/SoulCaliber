using System;

namespace SoulCaliber.Dialogue;

/// <summary>Pure presentation-only tokens; never searches for a different incident.</summary>
public static class MemoryDialogueFormatter
{
    public static string Format(string? text, string initiatorName, string recipientName,
        string? memoryActorName = null, string? affectedName = null)
    {
        string result = (text ?? "")
            .Replace("{initiator}", initiatorName, StringComparison.OrdinalIgnoreCase)
            .Replace("{recipient}", recipientName, StringComparison.OrdinalIgnoreCase)
            .Replace("{actor}", initiatorName, StringComparison.OrdinalIgnoreCase)
            .Replace("{partner}", recipientName, StringComparison.OrdinalIgnoreCase);
        if (result.Contains("{memory.actor}", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(memoryActorName))
                throw new InvalidOperationException("{memory.actor} requires a memory-anchored Special Encounter.");
            result = result.Replace("{memory.actor}", memoryActorName, StringComparison.OrdinalIgnoreCase);
        }
        if (result.Contains("{affected}", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(affectedName))
                throw new InvalidOperationException("{affected} requires a Special Encounter anchored to a memory with a matching affected character.");
            result = result.Replace("{affected}", affectedName, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }
}
