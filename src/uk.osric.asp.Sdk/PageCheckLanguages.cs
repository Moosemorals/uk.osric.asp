// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The names a caller passes to <see cref="IPageCheck.CheckAsync"/>.</summary>
/// <remarks>
/// The names are rendered from <see cref="PageCheckLanguage"/> rather than written twice, so the
/// manual, the assistant's tool schema and the host's refusal all read one declaration.
/// </remarks>
public static class PageCheckLanguages {

    /// <summary>Every language a check answers for, by the name a caller passes, in declaration order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [.. Enum.GetValues<PageCheckLanguage>().Select(NameOf)];

    /// <summary>Reads the language a caller named.</summary>
    /// <param name="language">The name the caller passed, compared without regard to case.</param>
    /// <param name="value">The language the name declares.</param>
    /// <returns>Whether the name is one of <see cref="Names"/>.</returns>
    public static bool TryParse(string? language, out PageCheckLanguage value) {
        foreach (PageCheckLanguage candidate in Enum.GetValues<PageCheckLanguage>()) {
            if (string.Equals(language, NameOf(candidate), StringComparison.OrdinalIgnoreCase)) {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>The name a caller passes for a language.</summary>
    /// <param name="language">The language to name.</param>
    /// <returns>The wire name, in lower case.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The language declares no name.</exception>
    public static string NameOf(PageCheckLanguage language) => language switch {
        PageCheckLanguage.Template => "template",
        _ => throw new ArgumentOutOfRangeException(
            nameof(language), language, "No name is declared for that language."),
    };
}
