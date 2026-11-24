// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

using System.Text;

/// <summary>The name of a message queue that has no page behind it.</summary>
/// <remarks>
/// <para>
/// A topic is a name, not a key: it names one queue per reader, and any page may post to it or take
/// from it. A malformed name is refused when the page constructs it, before a store is touched, so a
/// typo cannot quietly become a second queue.
/// </para>
/// <para>
/// A name is canonicalised the way a page key is — lowercased with the invariant culture and composed
/// to Unicode NFC — so <c>Job</c> and <c>job</c> are one queue. This is not compatibility
/// normalisation, and the text is not trimmed; only the case and the composition are the store's.
/// </para>
/// </remarks>
public sealed record PageTopic {

    private const int MaxNameBytes = 64;

    private PageTopic(string name) => Name = name;

    /// <summary>The queue's canonical name: 1 to 64 characters, lowercase, NFC, with no control characters.</summary>
    public string Name { get; }

    /// <summary>Names a queue.</summary>
    /// <param name="name">The queue's name, which is compared ordinally.</param>
    /// <returns>The topic a post or a take addresses.</returns>
    /// <exception cref="ArgumentException">The name is empty, longer than 64 characters, or holds a control character.</exception>
    public static PageTopic Named(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A topic needs a name.", nameof(name));
        }

        // The page-key rule: lowercasing first and composing last is what makes the result NFC by
        // construction. Sdk has no reference to Core, so the two calls are spelled here rather than
        // shared, and a name is validated after it is canonicalised because that is the stored text.
        string canonical = name.ToLowerInvariant().Normalize();

        if (Encoding.UTF8.GetByteCount(canonical) > MaxNameBytes) {
            throw new ArgumentException(
                $"The topic name {name} is longer than the {MaxNameBytes}-byte limit.", nameof(name));
        }

        foreach (char character in canonical) {
            if (char.IsControl(character)) {
                throw new ArgumentException($"The topic name {name} holds a control character.", nameof(name));
            }
        }

        return new PageTopic(canonical);
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
