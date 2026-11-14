// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>Which media type and script policy each page key gets, and the fallback for the rest.</summary>
/// <remarks>
/// <para>
/// The extension is the suffix after the last <c>.</c> of the key's last <c>/</c> segment, and only
/// when there is something before the dot: <c>/app.js</c> is JavaScript, while <c>/app</c>,
/// <c>/.js</c> and <c>/app.js/child</c> take the fallback. Matching is case-insensitive, so a raw
/// request path gives the same answer as a canonical key.
/// </para>
/// <para>
/// The map is a default, not a constraint: a page can still call <c>ctx.SetContentType</c> to send
/// something else. The script policy deliberately stays with the extension rather than following a
/// changed content type. <see cref="Create(PageContentTypeRule, IEnumerable{KeyValuePair{string, PageContentTypeRule}})"/>
/// validates and throws because the only caller is startup
/// configuration; <see cref="ForPath"/> and <see cref="ScriptsForPath"/> never throw.
/// </para>
/// </remarks>
public sealed class PageContentTypes {

    private readonly IReadOnlyDictionary<string, PageContentTypeRule> _extensions;
    private readonly PageContentTypeRule _fallback;

    private PageContentTypes(PageContentTypeRule fallback, IReadOnlyDictionary<string, PageContentTypeRule> extensions) {
        _fallback = fallback;
        _extensions = extensions;
    }

    /// <summary>What a key with no mapped extension gets.</summary>
    public string Fallback => _fallback.MediaType;

    /// <summary>The whole fallback rule, including its script policy.</summary>
    public PageContentTypeRule FallbackRule => _fallback;

    /// <summary>How many extensions are mapped.</summary>
    public int ExtensionCount => _extensions.Count;

    /// <summary>Builds a map from a fallback media type and one media type per extension.</summary>
    /// <param name="fallback">The media type for a key with no mapped extension.</param>
    /// <param name="extensions">
    /// Extension rows, keyed by the extension including its dot. A row with neither a key nor a value
    /// is absent rather than an error; anything else is validated. A string row's script policy is
    /// <see cref="PageScriptPolicy.None"/>, which is the old spelling of a row.
    /// </param>
    /// <returns>The map to use.</returns>
    /// <exception cref="ArgumentException">The fallback, a key or a media type is invalid.</exception>
    public static PageContentTypes Create(
        string fallback,
        IEnumerable<KeyValuePair<string, string?>> extensions) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        ArgumentNullException.ThrowIfNull(extensions);

        List<KeyValuePair<string, PageContentTypeRule>> rules = [];
        foreach ((string? key, string? mediaType) in extensions) {
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(mediaType)) {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mediaType)) {
                throw new ArgumentException($"The content type for '{key}' is blank.", nameof(extensions));
            }

            rules.Add(new(key, new PageContentTypeRule(mediaType)));
        }

        return Create(new PageContentTypeRule(fallback), rules);
    }

    /// <summary>Builds a map from a fallback rule and one rule per extension.</summary>
    /// <param name="fallback">The rule for a key with no mapped extension.</param>
    /// <param name="extensions">Extension rows, keyed by the extension including its dot.</param>
    /// <returns>The map to use.</returns>
    /// <exception cref="ArgumentException">The fallback, a key, a media type or a script policy is invalid.</exception>
    public static PageContentTypes Create(
        PageContentTypeRule fallback,
        IEnumerable<KeyValuePair<string, PageContentTypeRule>> extensions) {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(extensions);

        Dictionary<string, PageContentTypeRule> rows = [with(StringComparer.OrdinalIgnoreCase)];

        foreach ((string? key, PageContentTypeRule rule) in extensions) {
            string extension = ValidateExtension(key);
            PageContentTypeRule validated = ValidateRule(rule, $"The row for '{extension}'");

            if (!rows.TryAdd(extension, validated)) {
                throw new ArgumentException(
                    $"'{extension}' appears more than once, ignoring case.", nameof(extensions));
            }
        }

        return new PageContentTypes(ValidateRule(fallback, "The fallback content type"), rows);
    }

    /// <summary>The rule a page's key names.</summary>
    /// <param name="path">A page key or raw request path.</param>
    /// <returns>The mapped rule, or <see cref="FallbackRule"/> when the last segment has no mapped
    /// extension.</returns>
    public PageContentTypeRule RuleFor(string? path) {
        string? extension = ExtensionOf(path);

        return extension is not null && _extensions.TryGetValue(extension, out PageContentTypeRule? rule)
            ? rule
            : _fallback;
    }

    /// <summary>The response type a page's key names.</summary>
    /// <param name="path">A page key or raw request path.</param>
    /// <returns>The mapped media type, or <see cref="Fallback"/> when the last segment has no mapped
    /// extension.</returns>
    public string ForPath(string? path) => RuleFor(path).MediaType;

    /// <summary>The script policy a page's key names.</summary>
    /// <param name="path">A page key or raw request path.</param>
    /// <returns>The mapped policy, or the fallback's when the last segment has no mapped extension.</returns>
    public PageScriptPolicy ScriptsForPath(string? path) => RuleFor(path).ScriptPolicy;

    private static string? ExtensionOf(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        ReadOnlySpan<char> segment = path.AsSpan(path.LastIndexOf('/') + 1);
        int dot = segment.LastIndexOf('.');
        if (dot <= 0 || dot == segment.Length - 1) {
            return null;
        }

        return segment[dot..].ToString();
    }

    private static PageContentTypeRule ValidateRule(PageContentTypeRule rule, string source) {
        if (rule is null) {
            throw new ArgumentException($"{source} has no rule.", nameof(rule));
        }

        if (string.IsNullOrWhiteSpace(rule.MediaType)) {
            throw new ArgumentException($"{source} is blank.", nameof(rule));
        }

        if (!Enum.IsDefined(rule.ScriptPolicy)) {
            throw new ArgumentException(
                $"{source} has an unknown script policy '{rule.ScriptPolicy}'.", nameof(rule));
        }

        return new PageContentTypeRule(ValidateMediaType(rule.MediaType, source), rule.ScriptPolicy);
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
