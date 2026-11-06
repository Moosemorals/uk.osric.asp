// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Html;

using Microsoft.AspNetCore.Antiforgery;
using System.Net;

/// <summary>The one renderer of the antiforgery field every mutating form carries.</summary>
internal static class AntiforgeryField {

    /// <summary>Renders the hidden input that carries the antiforgery pair in a form.</summary>
    /// <param name="tokens">The tokens for this request.</param>
    /// <remarks>
    /// SEC-02 put this field on every mutating form. One renderer means a change to that contract
    /// cannot reach six views and miss the seventh.
    /// </remarks>
    public static string Hidden(AntiforgeryTokenSet tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        return $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(tokens.FormFieldName)}\" value=\"{WebUtility.HtmlEncode(tokens.RequestToken)}\">";
    }
}
