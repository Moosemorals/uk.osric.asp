// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what the page stores may cost, by subject.</summary>
/// <remarks>
/// One section, <c>Pages:Stores</c>, because the caps are chosen together: a person's values draw on
/// one budget per subject, and the two retired sections — <c>Pages:Storage</c> and
/// <c>Pages:Session</c> — are refused at startup rather than ignored. A section for a shape is added
/// by the slice that enforces it.
/// </remarks>
internal sealed record PageStoresOptions {

    /// <summary>The longest key any store accepts.</summary>
    public required int MaxKeyChars { get; init; }

    /// <summary>How long one storage command may run.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>How long a write waits for another writer before it answers that it is busy.</summary>
    public required int BusyTimeoutSeconds { get; init; }

    /// <summary>The Page subject's caps: shared by every visitor.</summary>
    public required PageStoreCaps Page { get; init; }

    /// <summary>The Visitor subject's caps and identity bounds.</summary>
    public required PageVisitorStores Visitor { get; init; }

    /// <summary>The command timeout as a time span.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

    /// <summary>Reads <c>Pages:Stores</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageStoresOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Stores");
        PageStoresOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Stores</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores</c>, which the caller completes.</param>
    internal static PageStoresOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        PageStoresOptions options = new() {
            MaxKeyChars = reader.Number("MaxKeyChars", recommended: 200, minimum: 16, maximum: 4_096),
            CommandTimeoutSeconds = reader.Number("CommandTimeoutSeconds", recommended: 5, minimum: 1, maximum: 120),
            BusyTimeoutSeconds = reader.Number("BusyTimeoutSeconds", recommended: 5, minimum: 1, maximum: 120),
            Page = PageStoreCaps.Read(reader.For("Page"), PageStoreCaps.Page),
            Visitor = PageVisitorStores.Read(reader.For("Visitor")),
        };
        return options;
    }
}

/// <summary>The caps one subject instance's values obey.</summary>
/// <param name="MaxValueBytes">The largest one JSON value may be, in bytes.</param>
/// <param name="MaxKeys">The most keys the subject may hold.</param>
/// <param name="MaxTotalBytes">The most bytes the subject's keys and values may total.</param>
internal sealed record PageStoreCaps(int MaxValueBytes, int MaxKeys, int MaxTotalBytes) {

    /// <summary>The Page subject's shipped caps.</summary>
    public static PageStoreCaps Page { get; } = new(64 * 1024, 256, 256 * 1024);

    /// <summary>An anonymous visitor's shipped caps.</summary>
    public static PageStoreCaps Anonymous { get; } = new(4 * 1024, 8, 16 * 1024);

    /// <summary>A browser's shipped caps.</summary>
    public static PageStoreCaps Browser { get; } = new(4 * 1024, 8, 16 * 1024);

    /// <summary>A signed-in visitor's shipped caps.</summary>
    public static PageStoreCaps SignedIn { get; } = new(16 * 1024, 32, 64 * 1024);

    /// <summary>Reads one subject's caps.</summary>
    /// <param name="reader">The subject's configuration, opened on its own section.</param>
    /// <param name="recommended">The caps the shipped configuration carries, for the messages.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static PageStoreCaps Read(ConfigurationReader reader, PageStoreCaps recommended) {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(recommended);

        int maxValueBytes = reader.Number("MaxValueBytes", recommended.MaxValueBytes,
            minimum: 1024, maximum: 1024 * 1024);
        int maxTotalBytes = reader.Number("MaxTotalBytes", recommended.MaxTotalBytes,
            minimum: 1024, maximum: 16 * 1024 * 1024);
        reader.FailIf(maxTotalBytes < maxValueBytes, "MaxTotalBytes",
            $"is {maxTotalBytes}, which is smaller than MaxValueBytes ({maxValueBytes})");

        return new PageStoreCaps(
            maxValueBytes,
            reader.Number("MaxKeys", recommended.MaxKeys, minimum: 1, maximum: 1_024),
            maxTotalBytes);
    }
}

/// <summary>The Visitor subject's caps, its identity bounds and its browser lifetime.</summary>
/// <param name="Anonymous">The caps an anonymous visitor's session obeys.</param>
/// <param name="Browser">The caps a browser-scoped entry obeys.</param>
/// <param name="SignedIn">The caps a signed-in visitor obeys.</param>
/// <param name="AnonymousIdle">How long an anonymous session survives without a visit.</param>
/// <param name="BrowserLifetimeDays">How long a browser's rows survive after their last write.</param>
/// <param name="MaxLiveSessions">The most live sessions the site keeps.</param>
internal sealed record PageVisitorStores(
    PageStoreCaps Anonymous,
    PageStoreCaps Browser,
    PageStoreCaps SignedIn,
    TimeSpan AnonymousIdle,
    int BrowserLifetimeDays,
    int MaxLiveSessions) {

    /// <summary>The shipped visitor bounds.</summary>
    public static PageVisitorStores Shipped { get; } = new(
        PageStoreCaps.Anonymous,
        PageStoreCaps.Browser,
        PageStoreCaps.SignedIn,
        TimeSpan.FromMinutes(30),
        365,
        2_048);

    /// <summary>Reads <c>Pages:Stores:Visitor</c>.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores:Visitor</c>, which the caller completes.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static PageVisitorStores Read(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        return new PageVisitorStores(
            PageStoreCaps.Read(reader.For("Anonymous"), PageStoreCaps.Anonymous),
            PageStoreCaps.Read(reader.For("Browser"), PageStoreCaps.Browser),
            PageStoreCaps.Read(reader.For("SignedIn"), PageStoreCaps.SignedIn),
            reader.For("Anonymous").Minutes("IdleMinutes", recommended: 30, minimum: 1, maximum: 525_600),
            reader.For("Browser").Number("LifetimeDays", recommended: 365, minimum: 1, maximum: 3_650),
            reader.Number("MaxLiveSessions", recommended: 2_048, minimum: 1, maximum: 1_000_000));
    }
}
