// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

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

    private readonly IReadOnlyDictionary<string, string> _extensions;
    private readonly string _fallback;

    private PageContentTypes(string fallback, IReadOnlyDictionary<string, string> extensions) {
        _fallback = fallback;
        _extensions = extensions;
    }

    /// <summary>What a key with no mapped extension gets.</summary>
    public string Fallback => _fallback;

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

        Dictionary<string, string> rows = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string? key, string? mediaType) in extensions) {
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(mediaType)) {
                continue;
            }

            string extension = ValidateExtension(key);
            if (string.IsNullOrWhiteSpace(mediaType)) {
                throw new ArgumentException($"The content type for '{extension}' is blank.", nameof(extensions));
            }

            string type = ValidateMediaType(mediaType, $"The content type for '{extension}'");
            if (!rows.TryAdd(extension, type)) {
                throw new ArgumentException(
                    $"'{extension}' appears more than once, ignoring case.", nameof(extensions));
            }
        }

        return new PageContentTypes(ValidateMediaType(fallback, "The fallback content type"), rows);
    }

    /// <summary>The response type a page's key names.</summary>
    /// <param name="path">A page key or raw request path.</param>
    /// <returns>The mapped media type, or <see cref="Fallback"/> when the last segment has no mapped
    /// extension.</returns>
    public string ForPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return _fallback;
        }

        ReadOnlySpan<char> segment = path.AsSpan(path.LastIndexOf('/') + 1);
        int dot = segment.LastIndexOf('.');
        if (dot <= 0 || dot == segment.Length - 1) {
            return _fallback;
        }

        return _extensions.TryGetValue(segment[dot..].ToString(), out string? mediaType)
            ? mediaType
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

    // One parser for the fallback and for a row, so "type/subtype with optional parameters" means the
    // same thing wherever a media type is configured.
    private static string ValidateMediaType(string mediaType, string source) {
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
}
