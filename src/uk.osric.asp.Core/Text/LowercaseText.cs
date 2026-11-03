// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Text;

/// <summary>The canonical form of text whose case a key ignores: lowercased with the invariant culture
/// and composed to Unicode NFC.</summary>
/// <remarks>
/// <para>
/// The case mapping runs first so that composing runs last: a case mapping can, in principle, produce
/// a decomposed sequence, and composing afterwards is what makes the result NFC by construction.
/// .NET's invariant mapping is one code point to one code point for every code point it defines, so
/// the reverse order gives the same answer today — but that is a property of the current data rather
/// than of the rule, and the safe order costs nothing.
/// </para>
/// <para>
/// This is lowercasing, not case folding: <c>ß</c> and <c>ss</c> stay different. Nor is it
/// compatibility normalisation, so <c>ﬀ</c> stays <c>ﬀ</c> and a fullwidth <c>ａ</c> stays
/// fullwidth — the text that comes back is the text that went in, re-encoded. For a path that
/// matters, because NFKC would turn <c>／</c> into a segment separator and <c>？</c> into a query
/// delimiter that no page key may contain.
/// </para>
/// </remarks>
public static class LowercaseText {

    /// <summary>Lowercases <paramref name="value"/> with the invariant culture and composes the result
    /// to NFC.</summary>
    /// <param name="value">The text to canonicalise. It is not trimmed or validated; the caller owns
    /// what a valid key is.</param>
    /// <returns>The canonical lowercase form of <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not valid Unicode.</exception>
    public static string Normalize(string value) {
        ArgumentNullException.ThrowIfNull(value);

        return value.ToLowerInvariant().Normalize();
    }
}
