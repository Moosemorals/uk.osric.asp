// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The rule a page's state key obeys, shared by a page's own write and an imported archive.</summary>
/// <remarks>
/// A key is 1 to <see cref="PageStorageOptions.MaxKeyChars"/> characters with no control characters.
/// An archive is validated against this before anything is staged, so an import cannot store a key a
/// page could not have written itself.
/// </remarks>
internal static class PageStateKey {

    /// <summary>Whether <paramref name="key"/> may name a state value.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="maxChars">The configured maximum length.</param>
    public static bool IsValid(string? key, int maxChars) {
        if (string.IsNullOrEmpty(key) || key.Length > maxChars) {
            return false;
        }

        foreach (char character in key) {
            if (char.IsControl(character)) {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when <paramref name="key"/> may not name a state value.</summary>
    /// <param name="key">The key to check.</param>
    /// <param name="maxChars">The configured maximum length.</param>
    /// <exception cref="ArgumentException">The key is empty, too long, or carries a control character.</exception>
    public static void Validate(string? key, int maxChars) {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (key.Length > maxChars) {
            throw new ArgumentException($"A key may be at most {maxChars} characters.", nameof(key));
        }

        foreach (char character in key) {
            if (char.IsControl(character)) {
                throw new ArgumentException("A key may not contain a control character.", nameof(key));
            }
        }
    }
}
