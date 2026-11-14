// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page's key/value store may cost.</summary>
/// <remarks>
/// Every value comes from <c>Pages:Storage</c>, and <see cref="MaxPageBytes"/> must be at least
/// <see cref="MaxValueBytes"/> so a single value at the per-value limit still fits an empty page.
/// </remarks>
internal sealed record PageStorageOptions {

    /// <summary>The longest key a page may use.</summary>
    public required int MaxKeyChars { get; init; }

    /// <summary>The largest one JSON value may be, in bytes.</summary>
    public required int MaxValueBytes { get; init; }

    /// <summary>The most keys one page may hold.</summary>
    public required int MaxKeysPerPage { get; init; }

    /// <summary>The most bytes one page's keys and values may total.</summary>
    public required int MaxPageBytes { get; init; }

    /// <summary>How long one storage command may run.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>The command timeout as a time span.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

    /// <summary>Reads <c>Pages:Storage</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageStorageOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Storage");
        int maxValueBytes = reader.Number("MaxValueBytes", recommended: 64 * 1024,
            minimum: 1024, maximum: 16 * 1024 * 1024);
        int maxPageBytes = reader.Number("MaxPageBytes", recommended: 256 * 1024,
            minimum: 1024, maximum: 64 * 1024 * 1024);

        reader.FailIf(maxPageBytes < maxValueBytes, "MaxPageBytes",
            $"is {maxPageBytes}, which is smaller than MaxValueBytes ({maxValueBytes})");

        PageStorageOptions options = new() {
            MaxKeyChars = reader.Number("MaxKeyChars", recommended: 200, minimum: 16, maximum: 4_096),
            MaxValueBytes = maxValueBytes,
            MaxKeysPerPage = reader.Number("MaxKeysPerPage", recommended: 256, minimum: 1, maximum: 65_536),
            MaxPageBytes = maxPageBytes,
            CommandTimeoutSeconds = reader.Number("CommandTimeoutSeconds", recommended: 5, minimum: 1, maximum: 120),
        };
        reader.Complete();
        return options;
    }
}
