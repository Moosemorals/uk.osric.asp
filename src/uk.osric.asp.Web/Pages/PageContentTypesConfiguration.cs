// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

/// <summary>Reads <c>Pages:ContentTypes</c> into the pure map a page's response is shaped from.</summary>
/// <remarks>
/// <para>
/// The section is ordinary configuration beside the other page options, so a host changes the map in
/// its own <c>appsettings.Production.json</c> and restarts rather than rebuilding the image. It is
/// read once; nothing in the shell observes changes while the process runs.
/// </para>
/// <para>
/// A row is either the shorter spelling — a media type string, which by definition has script policy
/// <c>none</c> — or an object with <c>ContentType</c> and <c>ScriptPolicy</c>. The policy belongs to
/// the extension, not to the final response type. The fallback and at least one extension are
/// required.
/// </para>
/// </remarks>
internal static class PageContentTypesConfiguration {

    /// <summary>Reads the section.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">Where the map is reported once.</param>
    /// <exception cref="ConfigurationException">A row is missing or unusable.</exception>
    public static PageContentTypes FromConfiguration(IConfiguration configuration, ILogger logger) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        ConfigurationReader reader = new(configuration, "Pages:ContentTypes");
        reader.FailIf(!configuration.GetSection("Pages:ContentTypes:Fallback").Exists(), "Fallback",
            "is not set. Add ContentType and ScriptPolicy for a key with no mapped extension");

        IConfigurationSection extensions = configuration.GetSection("Pages:ContentTypes:Extensions");
        reader.FailIf(!extensions.GetChildren().Any(), "Extensions",
            "is empty. Add at least one extension such as .html");

        PageContentTypeRule fallback = ReadRule(reader.For("Fallback"));
        KeyValuePair<string, PageContentTypeRule>[] rules = [
            .. extensions.GetChildren()
                .Select(child => new KeyValuePair<string, PageContentTypeRule>(
                    child.Key,
                    ReadRule(reader.For("Extensions").For(child.Key)))),
        ];
        reader.Complete();

        PageContentTypes contentTypes;
        try {
            contentTypes = PageContentTypes.Create(fallback, rules);
        } catch (ArgumentException exception) {
            // The map's own rules — an unknown extension, a row repeated in another case, a media
            // type that is not one — are configuration mistakes too, so they read the same way.
            throw new ConfigurationException(exception.Message);
        }

        logger.LogInformation(
            "Pages serve {FallbackContentType} with script policy {FallbackScriptPolicy} by default and map {ExtensionCount} extensions",
            contentTypes.Fallback,
            contentTypes.FallbackRule.ScriptPolicy,
            contentTypes.ExtensionCount);
        return contentTypes;
    }

    // A rule that could not be read is an empty rule: the map is never built, because Complete throws
    // first, so the placeholder only satisfies the compiler.
    private static PageContentTypeRule ReadRule(ConfigurationReader reader) {
        if (reader.ValueOrNull() is not null) {
            string mediaType = reader.Value("a media type such as text/html; charset=utf-8");
            return new PageContentTypeRule(mediaType);
        }

        string content = reader.Text("ContentType", "a media type such as text/html; charset=utf-8");

        return new PageContentTypeRule(content, ParseScriptPolicy(reader));
    }

    private static PageScriptPolicy ParseScriptPolicy(ConfigurationReader reader) {
        string policy = reader.Text("ScriptPolicy", "'none' or 'self'");

        return policy.Equals("none", StringComparison.OrdinalIgnoreCase) ? PageScriptPolicy.None
            : policy.Equals("self", StringComparison.OrdinalIgnoreCase) ? PageScriptPolicy.Self
            : Unreadable(reader, policy);
    }

    private static PageScriptPolicy Unreadable(ConfigurationReader reader, string policy) {
        reader.Fail("ScriptPolicy", $"is \"{policy}\", which is not 'none' or 'self'");
        return PageScriptPolicy.None;
    }
}
