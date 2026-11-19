// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using System.Net;

/// <summary>The one HTML encoder the shell writes text through.</summary>
/// <remarks>
/// A page's own text is escaped by the template and a response's headers by the framework; this is
/// for the shell's own markup, where a value from storage, a request or the provider is written
/// between tags or into an attribute. It is a name for the rule rather than a second implementation:
/// every call site writes <c>Encode(value)</c> and cannot reach for an unescaped string by accident.
/// </remarks>
internal static class Markup {

    /// <summary>Escapes a value for HTML text or a double-quoted attribute.</summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The value with markup characters replaced by entities.</returns>
    public static string Encode(string value) => WebUtility.HtmlEncode(value);
}
