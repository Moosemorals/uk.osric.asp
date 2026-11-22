// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Text;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>The one set of JSON serializer options for every JSON the site writes.</summary>
/// <remarks>
/// <para>
/// A call site names <see cref="Options"/>; it never constructs, copies or mutates an instance of its
/// own. A second setting is added here only when a shape has to differ, and the reason is stated on
/// the member.
/// </para>
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
/// An enum is written by its name rather than its ordinal, so a stored diagnostic or a value a page
/// keeps survives a build that reorders the enumeration. The provider body's field names are
/// dictionary keys, written verbatim and untouched by a naming policy; <c>ChatRequestJsonTests</c>
/// pins the body's names, which is what keeps the snake_case ones snake_case.
/// </para>
/// </remarks>
public static class JsonSerialization {

    /// <summary>The encoder for every string the site writes as JSON, including the log writer.</summary>
    public static JavaScriptEncoder Encoder { get; } = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>The one set: web defaults, the relaxed encoder, and enums by name.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) {
        Encoder = Encoder,
        Converters = { new JsonStringEnumConverter() },
    };
}
