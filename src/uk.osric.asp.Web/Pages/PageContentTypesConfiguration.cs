// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>Reads <c>Pages:ContentTypes</c> into the pure map a page's response is shaped from.</summary>
/// <remarks>
/// <para>
/// The section is ordinary configuration beside the other page options, so a host changes the map in
/// its own <c>appsettings.Production.json</c> and restarts rather than rebuilding the image. It is
/// read once; nothing in the shell observes changes while the process runs.
/// </para>
/// <para>
/// A row is either the old spelling — a media type string, with script policy <c>none</c> — or an
/// object with <c>ContentType</c> and <c>ScriptPolicy</c>. The policy belongs to the extension, not to
/// the final response type, and an unknown policy is fatal rather than silently becoming <c>none</c>.
/// A missing <c>Fallback</c> takes <see cref="PageResponse.Default"/>'s content type, so the SDK's
/// default and the site's fallback cannot drift apart.
/// </para>
/// </remarks>
internal static class PageContentTypesConfiguration {

    public static PageContentTypes FromConfiguration(IConfiguration configuration, ILogger logger) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        IConfigurationSection section = configuration.GetSection("Pages:ContentTypes");
        PageContentTypeRule fallback = ReadRule(
            section.GetSection("Fallback"),
            PageResponse.Default.ContentType,
            "The fallback content type");

        KeyValuePair<string, PageContentTypeRule>[] extensions = [
            .. section.GetSection("Extensions").GetChildren()
                .Select(child => new KeyValuePair<string, PageContentTypeRule>(
                    child.Key,
                    ReadRule(child, defaultMediaType: null, $"The content type for '{child.Key}'"))),
        ];

        PageContentTypes contentTypes = PageContentTypes.Create(fallback, extensions);
        logger.LogInformation(
            "Pages serve {FallbackContentType} with script policy {FallbackScriptPolicy} by default and map {ExtensionCount} extensions",
            contentTypes.Fallback,
            contentTypes.FallbackRule.ScriptPolicy,
            contentTypes.ExtensionCount);
        return contentTypes;
    }

    private static PageContentTypeRule ReadRule(
        IConfigurationSection section,
        string? defaultMediaType,
        string source) {
        if (section.Value is { } value) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException($"{source} is blank.", nameof(section));
            }

            return new PageContentTypeRule(value);
        }

        string? mediaType = section["ContentType"];
        if (string.IsNullOrWhiteSpace(mediaType)) {
            if (defaultMediaType is not null) {
                return new PageContentTypeRule(defaultMediaType);
            }

            throw new ArgumentException($"{source} has no ContentType.", nameof(section));
        }

        return new PageContentTypeRule(mediaType, ParseScriptPolicy(section["ScriptPolicy"], source));
    }

    private static PageScriptPolicy ParseScriptPolicy(string? value, string source) {
        if (string.IsNullOrWhiteSpace(value)) {
            return PageScriptPolicy.None;
        }

        string policy = value.Trim();

        return policy.Equals("none", StringComparison.OrdinalIgnoreCase) ? PageScriptPolicy.None
            : policy.Equals("self", StringComparison.OrdinalIgnoreCase) ? PageScriptPolicy.Self
            : throw new ArgumentException(
                $"{source} has an unknown ScriptPolicy '{value}'; use 'none' or 'self'.", nameof(value));
    }
}
