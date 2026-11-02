// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Net;

/// <summary>Escaping for the markup the charts generate.</summary>
/// <remarks>
/// <see cref="WebUtility.HtmlEncode(string?)"/> already lives in the base class library; naming it
/// here keeps every escape in one place, next to the markup it guards.
/// </remarks>
public static class SvgText {

    /// <summary>Encodes text for an element body or an attribute value.</summary>
    /// <param name="value">The text, which may be null.</param>
    public static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
