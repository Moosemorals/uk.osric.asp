// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The content types the object store serves, and the one spelling each is stored with.</summary>
/// <remarks>
/// The list is fixed in code rather than configuration because it is a security decision as much as a
/// formatting one: an SVG is markup and a script vector, and the site already has a page kind for
/// markup. Case and whitespace are tolerated and the canonical spelling is what a put answers and a
/// row stores, so <c>IMAGE/PNG</c> and <c>image/png</c> are one content type.
/// </remarks>
public static class PageObjectTypes {

    private static readonly string[] s_allowed = [
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "text/plain; charset=utf-8",
        "application/octet-stream",
    ];

    /// <summary>The content types a page may store, in their canonical spelling.</summary>
    public static IReadOnlyList<string> Allowed { get; } = Array.AsReadOnly(s_allowed);

    /// <summary>Whether a content type is one the store serves.</summary>
    /// <param name="contentType">The content type a page asked for.</param>
    /// <returns>Whether it is allowed, ignoring case and whitespace.</returns>
    public static bool IsAllowed(string? contentType) => CanonicalOrNull(contentType) is not null;

    /// <summary>The canonical spelling of an allowed content type.</summary>
    /// <param name="contentType">The content type a page asked for.</param>
    /// <returns>The spelling the store writes.</returns>
    /// <exception cref="ArgumentException">The content type is not one the store serves.</exception>
    public static string Canonical(string contentType) =>
        CanonicalOrNull(contentType)
            ?? throw new ArgumentException(
                $"'{contentType}' is not a content type the object store serves; use {string.Join(", ", s_allowed)}.",
                nameof(contentType));

    // RFC media types are case-insensitive and parameters may be spaced either way, so the two are
    // squeezed to compare. The canonical spelling that is returned is one this list wrote.
    private static string? CanonicalOrNull(string? contentType) {
        if (string.IsNullOrWhiteSpace(contentType)) {
            return null;
        }

        string squeezed = string.Concat(contentType.Where(character => !char.IsWhiteSpace(character)));

        foreach (string allowed in s_allowed) {
            if (string.Equals(Squeeze(allowed), squeezed, StringComparison.OrdinalIgnoreCase)) {
                return allowed;
            }
        }

        return null;
    }

    private static string Squeeze(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
}
