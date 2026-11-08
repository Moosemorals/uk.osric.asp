// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
/// A missing <c>Fallback</c> takes <see cref="PageResponse.Default"/>'s content type, so the SDK's
/// document default and the site's fallback cannot drift apart. A blank or malformed one is fatal:
/// "unset" and "wrong" are different mistakes.
/// </para>
/// </remarks>
internal static class PageContentTypesConfiguration {

    public static PageContentTypes FromConfiguration(IConfiguration configuration, ILogger logger) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        IConfigurationSection section = configuration.GetSection("Pages:ContentTypes");
        string fallback = section["Fallback"] ?? PageResponse.Default.ContentType;
        KeyValuePair<string, string?>[] extensions = [
            .. section.GetSection("Extensions").GetChildren()
                .Select(child => new KeyValuePair<string, string?>(child.Key, child.Value)),
        ];

        PageContentTypes contentTypes = PageContentTypes.Create(fallback, extensions);
        logger.LogInformation(
            "Pages serve {FallbackContentType} by default and map {ExtensionCount} extensions",
            contentTypes.Fallback.MediaType,
            contentTypes.ExtensionCount);
        return contentTypes;
    }
}
