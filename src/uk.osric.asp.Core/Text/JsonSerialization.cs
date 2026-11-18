// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Text;

using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>The one escaping policy for every JSON body the site writes.</summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> escapes by default for a document: every HTML-significant character
/// (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c> and <c>+</c>) and every non-ASCII character becomes
/// a <c>\uXXXX</c> escape. That guards JSON written into a web page or a script block, which this site
/// never does — it writes JSON as a JSON body to a client that parses it, or stores it in SQLite — so
/// the default only made a page source or a stored value larger than the text it holds.
/// </para>
/// <para>
/// The relaxed encoder still escapes what JSON itself needs: the quote, the backslash and every
/// control character. "Unsafe" in its name is about embedding the result in HTML, not about JSON
/// validity, and nothing here puts JSON in an HTML context.
/// </para>
/// <para>
/// <see cref="Options"/> is a template, not an instance to configure: copy it with
/// <c>new JsonSerializerOptions(JsonSerialization.Options)</c> before adding a converter or a naming
/// policy, because the template becomes read-only the first time it is used.
/// </para>
/// </remarks>
public static class JsonSerialization {

    /// <summary>The encoder for every string the site writes as JSON.</summary>
    public static JavaScriptEncoder Encoder { get; } = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>A template carrying the framework's web defaults and <see cref="Encoder"/>.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) {
        Encoder = Encoder,
    };
}
