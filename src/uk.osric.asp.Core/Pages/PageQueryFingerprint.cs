// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using System.Text;

/// <summary>A stable fingerprint of a request's query, for telling one render of a page from another.</summary>
/// <remarks>
/// <para>
/// A page that embeds itself with a different query is a recursive component, not a cycle, so the
/// ancestor check compares a page key together with this fingerprint. Two orders of names that a
/// browser could send differently must not collide, and a repeated name keeps the order of its
/// values, so the fingerprint is computed over the ordered list rather than over a dictionary.
/// </para>
/// <para>
/// Every element is written with its length, so no name or value can be confused with the separators
/// between them.
/// </para>
/// </remarks>
public static class PageQueryFingerprint {

    /// <summary>Fingerprints the query, in the order its names and values arrived.</summary>
    /// <param name="query">Every query value, per name, in order.</param>
    /// <returns>A string that is equal for the same query and different for a different one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    public static string Of(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> query) {
        ArgumentNullException.ThrowIfNull(query);

        StringBuilder text = new();

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in query) {
            Append(text, entry.Key);

            foreach (string value in entry.Value) {
                Append(text, value);
            }

            text.Append(';');
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string value) =>
        _ = text.Append(value.Length).Append(':').Append(value).Append(',');
}
