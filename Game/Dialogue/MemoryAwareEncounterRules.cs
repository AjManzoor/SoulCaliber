using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Resolve a specific affected character from one selected occurrence.
/// This never combines relationships from unrelated incidents.
/// </summary>
public static class MemoryAwareEncounterRules
{
    public static string? BindAffected(
        IReadOnlyList<string> affectedCharacterIds,
        string recipientId,
        string? requiredRecipientToAffectedStatus,
        Func<string, string, string> currentStatus)
    {
        ArgumentNullException.ThrowIfNull(affectedCharacterIds);
        ArgumentNullException.ThrowIfNull(currentStatus);
        if (string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Recipient is required.", nameof(recipientId));

        var candidates = affectedCharacterIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (string.IsNullOrWhiteSpace(requiredRecipientToAffectedStatus))
        {
            // For a direct encounter, prefer the actual recipient if affected.
            return candidates.FirstOrDefault(id => string.Equals(id, recipientId, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault();
        }
        return candidates.FirstOrDefault(id =>
            !string.Equals(id, recipientId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentStatus(recipientId, id), requiredRecipientToAffectedStatus,
                StringComparison.OrdinalIgnoreCase));
    }
}
