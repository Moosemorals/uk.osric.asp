// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>Keeps a login redirect on this site.</summary>
internal static class LocalRedirect {

    /// <summary>Returns the requested local path, or the editor when it is not safe.</summary>
    /// <param name="returnUrl">The value the browser supplied.</param>
    public static string Safe(string? returnUrl) {
        if (returnUrl is null || !IsSafe(returnUrl)) {
            return "/edit";
        }

        return returnUrl;
    }

    /// <summary>Whether a value is a path on this site rather than an absolute or protocol-relative URL.</summary>
    /// <param name="returnUrl">The value to test.</param>
    public static bool IsSafe(string? returnUrl) {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/') {
            return false;
        }

        // A second slash or a backslash lets a browser interpret the value as an authority.
        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\')) {
            return false;
        }

        foreach (char character in returnUrl) {
            if (character == '\\' || char.IsControl(character)) {
                return false;
            }
        }

        return true;
    }
}
