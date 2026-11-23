// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what the page stores may cost, by subject.</summary>
/// <remarks>
/// One section, <c>Pages:Stores</c>, because the limits are chosen together: a person's values draw on
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

    /// <summary>The Page subject's limits: shared by every visitor.</summary>
    public required SubjectLimits Page { get; init; }

    /// <summary>The Visitor subject's limits and identity bounds.</summary>
    public required VisitorOptions Visitor { get; init; }

    /// <summary>The record shape's limits.</summary>
    public required PageRecordOptions Records { get; init; }

    /// <summary>The message shape's limits.</summary>
    public required PageMessageOptions Messages { get; init; }

    /// <summary>The object shape's limits.</summary>
    public required PageObjectOptions Objects { get; init; }

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
            Page = SubjectLimits.Read(reader.For("Page"), SubjectLimits.Page),
            Visitor = VisitorOptions.Read(reader.For("Visitor")),
            Records = PageRecordOptions.Read(reader.For("Records")),
            Messages = PageMessageOptions.Read(reader.For("Messages")),
            Objects = PageObjectOptions.Read(reader.For("Objects")),
        };
        return options;
    }
}

/// <summary>The limits one subject instance's values obey.</summary>
/// <param name="MaxValueBytes">The largest one JSON value may be, in bytes.</param>
/// <param name="MaxKeys">The most keys the subject may hold.</param>
/// <param name="MaxTotalBytes">The most bytes the subject's keys and values may total.</param>
internal sealed record SubjectLimits(int MaxValueBytes, int MaxKeys, int MaxTotalBytes) {

    /// <summary>The Page subject's shipped limits.</summary>
    public static SubjectLimits Page { get; } = new(64 * 1024, 256, 256 * 1024);

    /// <summary>An anonymous visitor's shipped limits.</summary>
    public static SubjectLimits Anonymous { get; } = new(4 * 1024, 8, 16 * 1024);

    /// <summary>A browser's shipped limits.</summary>
    public static SubjectLimits Browser { get; } = new(4 * 1024, 8, 16 * 1024);

    /// <summary>A signed-in visitor's shipped limits.</summary>
    public static SubjectLimits SignedIn { get; } = new(16 * 1024, 32, 64 * 1024);

    /// <summary>Reads one subject's limits.</summary>
    /// <param name="reader">The subject's configuration, opened on its own section.</param>
    /// <param name="recommended">The limits the shipped configuration carries, for the messages.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static SubjectLimits Read(ConfigurationReader reader, SubjectLimits recommended) {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(recommended);

        int maxValueBytes = reader.Number("MaxValueBytes", recommended.MaxValueBytes,
            minimum: 1024, maximum: 1024 * 1024);
        int maxTotalBytes = reader.Number("MaxTotalBytes", recommended.MaxTotalBytes,
            minimum: 1024, maximum: 16 * 1024 * 1024);
        reader.FailIf(maxTotalBytes < maxValueBytes, "MaxTotalBytes",
            $"is {maxTotalBytes}, which is smaller than MaxValueBytes ({maxValueBytes})");

        return new SubjectLimits(
            maxValueBytes,
            reader.Number("MaxKeys", recommended.MaxKeys, minimum: 1, maximum: 1_024),
            maxTotalBytes);
    }
}

/// <summary>The Visitor subject's limits, its identity bounds and its browser lifetime.</summary>
/// <param name="Anonymous">The limits an anonymous visitor's session obeys.</param>
/// <param name="Browser">The limits a browser-scoped entry obeys.</param>
/// <param name="SignedIn">The limits a signed-in visitor obeys.</param>
/// <param name="AnonymousIdle">How long an anonymous session survives without a visit.</param>
/// <param name="BrowserLifetimeDays">How long a browser's rows survive after their last write.</param>
/// <param name="MaxLiveSessions">The most live sessions the site keeps.</param>
internal sealed record VisitorOptions(
    SubjectLimits Anonymous,
    SubjectLimits Browser,
    SubjectLimits SignedIn,
    TimeSpan AnonymousIdle,
    int BrowserLifetimeDays,
    int MaxLiveSessions) {

    /// <summary>The shipped visitor bounds.</summary>
    public static VisitorOptions Shipped { get; } = new(
        SubjectLimits.Anonymous,
        SubjectLimits.Browser,
        SubjectLimits.SignedIn,
        TimeSpan.FromMinutes(30),
        365,
        2_048);

    /// <summary>Reads <c>Pages:Stores:Visitor</c>.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores:Visitor</c>, which the caller completes.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static VisitorOptions Read(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        return new VisitorOptions(
            SubjectLimits.Read(reader.For("Anonymous"), SubjectLimits.Anonymous),
            SubjectLimits.Read(reader.For("Browser"), SubjectLimits.Browser),
            SubjectLimits.Read(reader.For("SignedIn"), SubjectLimits.SignedIn),
            reader.For("Anonymous").Minutes("IdleMinutes", recommended: 30, minimum: 1, maximum: 525_600),
            reader.For("Browser").Number("LifetimeDays", recommended: 365, minimum: 1, maximum: 3_650),
            reader.Number("MaxLiveSessions", recommended: 2_048, minimum: 1, maximum: 1_000_000));
    }
}

/// <summary>The record shape's limits: what a declaration may declare and what a statement may cost.</summary>
/// <param name="MaxTablesPerSource">The most tables one source may declare.</param>
/// <param name="MaxColumnsPerTable">The most columns one table may declare.</param>
/// <param name="MaxNameChars">The longest table or column name.</param>
/// <param name="MaxRowsPerSubject">The most rows one subject may hold in one table.</param>
/// <param name="MaxBytesPerSubject">The most bytes one subject may hold in one table.</param>
/// <param name="MaxResultRows">The most rows one statement may answer.</param>
/// <param name="MaxResultChars">The most text one statement may answer.</param>
/// <param name="StatementMaxSeconds">How long one statement may run before it is interrupted.</param>
/// <param name="TransactionMaxSeconds">How long a page's transaction may stay open.</param>
internal sealed record PageRecordOptions(
    int MaxTablesPerSource,
    int MaxColumnsPerTable,
    int MaxNameChars,
    int MaxRowsPerSubject,
    int MaxBytesPerSubject,
    int MaxResultRows,
    int MaxResultChars,
    int StatementMaxSeconds,
    int TransactionMaxSeconds) {

    /// <summary>The shipped record limits.</summary>
    public static PageRecordOptions Shipped { get; } = new(8, 24, 64, 10_000, 64 * 1024 * 1024, 500, 256 * 1024, 5, 10);

    /// <summary>How long one statement may run.</summary>
    public TimeSpan StatementTimeout => TimeSpan.FromSeconds(StatementMaxSeconds);

    /// <summary>How long a transaction may stay open.</summary>
    public TimeSpan TransactionTimeout => TimeSpan.FromSeconds(TransactionMaxSeconds);

    /// <summary>The declaration limits, as the schema value states them.</summary>
    public PageSchemaLimits SchemaLimits => new(MaxTablesPerSource, MaxColumnsPerTable, MaxNameChars);

    /// <summary>Reads <c>Pages:Stores:Records</c>.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores:Records</c>, which the caller completes.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageRecordOptions Read(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        PageRecordOptions shipped = Shipped;
        PageRecordOptions options = new(
            reader.Number("MaxTablesPerSource", shipped.MaxTablesPerSource, minimum: 1, maximum: 64),
            reader.Number("MaxColumnsPerTable", shipped.MaxColumnsPerTable, minimum: 1, maximum: 256),
            reader.Number("MaxNameChars", shipped.MaxNameChars, minimum: 8, maximum: 512),
            reader.Number("MaxRowsPerSubject", shipped.MaxRowsPerSubject, minimum: 1, maximum: 10_000_000),
            reader.Number("MaxBytesPerSubject", shipped.MaxBytesPerSubject, minimum: 1024, maximum: int.MaxValue),
            reader.Number("MaxResultRows", shipped.MaxResultRows, minimum: 1, maximum: 100_000),
            reader.Number("MaxResultChars", shipped.MaxResultChars, minimum: 1024, maximum: 64 * 1024 * 1024),
            reader.Number("StatementMaxSeconds", shipped.StatementMaxSeconds, minimum: 1, maximum: 300),
            reader.Number("TransactionMaxSeconds", shipped.TransactionMaxSeconds, minimum: 1, maximum: 300));

        reader.FailIf(options.MaxResultRows > options.MaxRowsPerSubject, "MaxResultRows",
            $"is {options.MaxResultRows}, which is more than MaxRowsPerSubject ({options.MaxRowsPerSubject})");

        return options;
    }
}

/// <summary>The message shape's limits: one body, and what one subject may hold.</summary>
/// <param name="MaxMessageBytes">The largest one message body may be, in bytes.</param>
/// <param name="MaxMessagesPerSubject">The most messages one subject may hold.</param>
/// <param name="MaxSubjectBytes">The most bytes one subject's message bodies may total.</param>
internal sealed record PageMessageOptions(int MaxMessageBytes, int MaxMessagesPerSubject, int MaxSubjectBytes) {

    /// <summary>The shipped message limits.</summary>
    public static PageMessageOptions Shipped { get; } = new(16 * 1024, 8, 64 * 1024);

    /// <summary>Reads <c>Pages:Stores:Messages</c>.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores:Messages</c>, which the caller completes.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static PageMessageOptions Read(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        int maxMessageBytes = reader.Number("MaxMessageBytes", Shipped.MaxMessageBytes, minimum: 256, maximum: 4 * 1024 * 1024);
        int maxSubjectBytes = reader.Number("MaxSubjectBytes", Shipped.MaxSubjectBytes, minimum: 1024, maximum: 64 * 1024 * 1024);
        reader.FailIf(maxSubjectBytes < maxMessageBytes, "MaxSubjectBytes",
            $"is {maxSubjectBytes}, which is smaller than MaxMessageBytes ({maxMessageBytes})");

        return new PageMessageOptions(
            maxMessageBytes,
            reader.Number("MaxMessagesPerSubject", Shipped.MaxMessagesPerSubject, minimum: 1, maximum: 1_024),
            maxSubjectBytes);
    }
}

/// <summary>The object shape's limits: one object, and what one subject may hold.</summary>
/// <param name="MaxObjectBytes">The largest one object may be, in bytes.</param>
/// <param name="MaxSubjectBytes">The most bytes one subject's objects may total.</param>
internal sealed record PageObjectOptions(int MaxObjectBytes, long MaxSubjectBytes) {

    /// <summary>The shipped object limits.</summary>
    public static PageObjectOptions Shipped { get; } = new(8 * 1024 * 1024, 64L * 1024 * 1024);

    /// <summary>Reads <c>Pages:Stores:Objects</c>.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stores:Objects</c>, which the caller completes.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static PageObjectOptions Read(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        int maxObjectBytes = reader.Number("MaxObjectBytes", Shipped.MaxObjectBytes, minimum: 1024, maximum: 256 * 1024 * 1024);
        long maxSubjectBytes = reader.Number("MaxSubjectBytes", (int)Shipped.MaxSubjectBytes, minimum: 1024, maximum: int.MaxValue);
        reader.FailIf(maxSubjectBytes < maxObjectBytes, "MaxSubjectBytes",
            $"is {maxSubjectBytes}, which is smaller than MaxObjectBytes ({maxObjectBytes})");

        return new PageObjectOptions(maxObjectBytes, maxSubjectBytes);
    }
}
