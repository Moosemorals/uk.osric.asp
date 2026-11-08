// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The source language a page's key names, derived from its media type.</summary>
/// <remarks>
/// The kind is a second reading of the media type rather than a separate setting, so a configuration
/// row cannot pair a JavaScript kind with an HTML type. It exists for tools that read a page's source
/// — the assistant's validator — not for the response, which is written from the media type.
/// </remarks>
public enum PageContentKind {
    /// <summary>HTML, the document the site is built around.</summary>
    Html,

    /// <summary>JavaScript source.</summary>
    JavaScript,

    /// <summary>Cascading style sheets.</summary>
    Css,

    /// <summary>JSON data.</summary>
    Json,

    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Any other type: an image, a font, something the site does not read as source.</summary>
    Other,
}

/// <summary>What one page's response should be: a media type and the source language it implies.</summary>
public sealed record PageContentType {

    private PageContentType(string mediaType) {
        MediaType = mediaType;
        Kind = KindOf(mediaType);
    }

    /// <summary>The media type to write, including any parameters it was configured with.</summary>
    public string MediaType { get; }

    /// <summary>The source language <see cref="MediaType"/> implies.</summary>
    public PageContentKind Kind { get; }

    /// <summary>Creates a row from a configured media type.</summary>
    /// <param name="mediaType">A media type, such as <c>text/css; charset=utf-8</c>.</param>
    /// <returns>The media type with the kind it implies.</returns>
    /// <exception cref="ArgumentException"><paramref name="mediaType"/> is blank or is not a media
    /// type.</exception>
    public static PageContentType FromMediaType(string mediaType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return FromMediaType(mediaType, "The content type");
    }

    internal static PageContentType FromMediaType(string mediaType, string source) {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return new PageContentType(NormalizeMediaType(mediaType, source));
    }

    // One parser for the fallback and for a row, so "type/subtype with optional parameters" means the
    // same thing wherever a media type is configured.
    internal static string NormalizeMediaType(string mediaType, string source) {
        string value = mediaType.Trim();
        int semicolon = value.IndexOf(';', StringComparison.Ordinal);
        string typeAndSubtype = (semicolon < 0 ? value : value[..semicolon]).Trim();

        int slash = typeAndSubtype.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0
            || slash != typeAndSubtype.LastIndexOf('/')
            || slash == typeAndSubtype.Length - 1
            || !IsToken(typeAndSubtype.AsSpan(0, slash))
            || !IsToken(typeAndSubtype.AsSpan(slash + 1))
            || (semicolon >= 0 && !HasParameters(value.AsSpan(semicolon + 1)))) {
            throw new ArgumentException(
                $"{source} must be a media type such as text/html, but was '{value}'.", nameof(mediaType));
        }

        return value;
    }

    private static bool HasParameters(ReadOnlySpan<char> parameters) {
        foreach (Range range in parameters.Split(';')) {
            ReadOnlySpan<char> parameter = parameters[range].Trim();
            int equals = parameter.IndexOf('=');
            if (equals <= 0 || equals == parameter.Length - 1) {
                return false;
            }
        }

        return true;
    }

    // RFC 9110 token characters: the media type and subtype are tokens, so a value with a space, a
    // second slash or a parameter accidentally left before the semicolon is rejected rather than
    // written to a response header.
    private static bool IsToken(ReadOnlySpan<char> value) {
        if (value.IsEmpty) {
            return false;
        }

        foreach (char character in value) {
            if (!char.IsAsciiLetterOrDigit(character) && !"!#$%&'*+-.^_`|~".Contains(character, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static PageContentKind KindOf(string mediaType) {
        int semicolon = mediaType.IndexOf(';', StringComparison.Ordinal);
        string type = (semicolon < 0 ? mediaType : mediaType[..semicolon]).Trim().ToLowerInvariant();

        return type switch {
            "text/html" => PageContentKind.Html,
            "text/javascript" or "application/javascript" => PageContentKind.JavaScript,
            "text/css" => PageContentKind.Css,
            "text/plain" => PageContentKind.Text,
            "application/json" => PageContentKind.Json,
            _ => Subtype(type).EndsWith("+json", StringComparison.Ordinal)
                ? PageContentKind.Json
                : PageContentKind.Other,
        };
    }

    private static string Subtype(string type) {
        int slash = type.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? type : type[(slash + 1)..];
    }
}

/// <summary>Which media type each page key gets, and what an unmapped key falls back to.</summary>
/// <remarks>
/// <para>
/// The extension is the suffix after the last <c>.</c> of the key's last <c>/</c> segment, and only
/// when there is something before the dot: <c>/app.js</c> is JavaScript, while <c>/app</c>,
/// <c>/.js</c> and <c>/app.js/child</c> take the fallback. Matching is case-insensitive, so a raw
/// request path gives the same answer as a canonical key.
/// </para>
/// <para>
/// The map is a default, not a constraint: a page can still call <c>ctx.SetContentType</c> to send
/// something else, and nothing here is enforced on the response. <see cref="Create"/> validates and
/// throws because the only caller is startup configuration; <see cref="ForPath"/> never throws.
/// </para>
/// </remarks>
public sealed class PageContentTypes {

    private readonly IReadOnlyDictionary<string, PageContentType> _extensions;
    private readonly PageContentType _fallback;

    private PageContentTypes(PageContentType fallback, IReadOnlyDictionary<string, PageContentType> extensions) {
        _fallback = fallback;
        _extensions = extensions;
    }

    /// <summary>What a key with no mapped extension gets.</summary>
    public PageContentType Fallback => _fallback;

    /// <summary>How many extensions are mapped.</summary>
    public int ExtensionCount => _extensions.Count;

    /// <summary>Builds a map from a fallback media type and one row per extension.</summary>
    /// <param name="fallback">The media type for a key with no mapped extension.</param>
    /// <param name="extensions">
    /// Extension rows, keyed by the extension including its dot. A row with neither a key nor a value
    /// is absent rather than an error; anything else is validated.
    /// </param>
    /// <returns>The map to use.</returns>
    /// <exception cref="ArgumentException">The fallback, a key or a media type is invalid.</exception>
    public static PageContentTypes Create(
        string fallback,
        IEnumerable<KeyValuePair<string, string?>> extensions) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        ArgumentNullException.ThrowIfNull(extensions);

        Dictionary<string, PageContentType> rows = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string? key, string? mediaType) in extensions) {
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(mediaType)) {
                continue;
            }

            string extension = ValidateExtension(key);
            if (string.IsNullOrWhiteSpace(mediaType)) {
                throw new ArgumentException($"The content type for '{extension}' is blank.", nameof(extensions));
            }

            PageContentType type = PageContentType.FromMediaType(mediaType, $"The content type for '{extension}'");
            if (!rows.TryAdd(extension, type)) {
                throw new ArgumentException(
                    $"'{extension}' appears more than once, ignoring case.", nameof(extensions));
            }
        }

        return new PageContentTypes(
            PageContentType.FromMediaType(fallback, "The fallback content type"),
            rows);
    }

    /// <summary>The response type a page's key names.</summary>
    /// <param name="path">A page key or raw request path.</param>
    /// <returns>The mapped type, or <see cref="Fallback"/> when the last segment has no mapped
    /// extension.</returns>
    public PageContentType ForPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return _fallback;
        }

        ReadOnlySpan<char> segment = path.AsSpan(path.LastIndexOf('/') + 1);
        int dot = segment.LastIndexOf('.');
        if (dot <= 0 || dot == segment.Length - 1) {
            return _fallback;
        }

        return _extensions.TryGetValue(segment[dot..].ToString(), out PageContentType? type)
            ? type
            : _fallback;
    }

    private static string ValidateExtension(string? key) {
        string extension = key?.Trim() ?? string.Empty;

        if (extension.Length < 2
            || extension[0] != '.'
            || extension.AsSpan(1).Contains('.')
            || extension.AsSpan(1).Contains('/')
            || extension.AsSpan(1).ContainsAny(' ', '\t')) {
            throw new ArgumentException(
                $"'{extension}' is not an extension: it must start with '.' and name one extension.", nameof(key));
        }

        return extension;
    }
}
