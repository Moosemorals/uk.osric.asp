// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>What kind of note a memory is.</summary>
/// <remarks>
/// Four values, not a taxonomy: enough to group the block the model reads and to make the owner's
/// page (IDEA-023) useful, and small enough that the model is not choosing between neighbours.
/// </remarks>
public enum MemoryKind {

    /// <summary>How the owner wants things done.</summary>
    Preference,

    /// <summary>Something durable about this site or the setup it runs on.</summary>
    Fact,

    /// <summary>A correction the owner gave.</summary>
    Feedback,

    /// <summary>Where something lives.</summary>
    Reference,
}

/// <summary>Names and parses the four <see cref="MemoryKind"/> values.</summary>
public static class MemoryKinds {

    private static readonly MemoryKind[] s_all = Enum.GetValues<MemoryKind>();

    /// <summary>Every kind, in the order the prompt lists them.</summary>
    public static IReadOnlyList<MemoryKind> All => s_all;

    /// <summary>The stored and provider-visible name of a kind: its lower-case name.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The name the database column and the tool schema use.</returns>
    public static string Name(MemoryKind kind) => kind switch {
        MemoryKind.Preference => "preference",
        MemoryKind.Fact => "fact",
        MemoryKind.Feedback => "feedback",
        MemoryKind.Reference => "reference",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Parses a model-supplied or stored kind name.</summary>
    /// <param name="text">The name, matched case-insensitively.</param>
    /// <param name="kind">The kind, when the name is one of the four.</param>
    /// <returns>Whether the name was a kind.</returns>
    public static bool TryParse(string? text, out MemoryKind kind) {
        foreach (MemoryKind candidate in s_all) {
            if (string.Equals(Name(candidate), text?.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase)) {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>Parses a stored kind name, falling back to <see cref="MemoryKind.Fact"/>.</summary>
    /// <param name="text">The stored name.</param>
    /// <returns>The kind the row names, or <see cref="MemoryKind.Fact"/> when it names none.</returns>
    public static MemoryKind Parse(string? text) => TryParse(text, out MemoryKind kind) ? kind : MemoryKind.Fact;
}
