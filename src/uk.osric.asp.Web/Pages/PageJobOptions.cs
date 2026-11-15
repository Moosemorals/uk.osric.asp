// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds a job that runs a stored worker after the request that started it has gone.</summary>
/// <remarks>
/// <para>
/// A job answers nobody, so the render budget is the wrong question. It has its own lifetime — a
/// silence clock that catches a stall, re-armed by everything the worker does, and a ceiling that
/// ends work which never stops — its own slots, and its own bounds on the messages it takes and the
/// events it publishes. Every value comes from <c>Pages:Jobs</c>.
/// </para>
/// <para>
/// The shipped ceiling is nine hours, because a worker that runs for a working day is the point; the
/// silence clock is what notices one that has wedged.
/// </para>
/// </remarks>
internal sealed record PageJobOptions {

    /// <summary>Jobs waiting for a runner.</summary>
    public required int QueueCapacity { get; init; }

    /// <summary>Jobs allowed to run at once.</summary>
    public required int MaxConcurrent { get; init; }

    /// <summary>How long a job may go without publishing or receiving before it is cut.</summary>
    public required double SilenceSeconds { get; init; }

    /// <summary>The longest a job may run, measured from its start.</summary>
    public required double MaxDurationSeconds { get; init; }

    /// <summary>Events one job's journal keeps before it drops the oldest.</summary>
    public required int MaxEvents { get; init; }

    /// <summary>The most bytes one job's journal holds before it drops the oldest.</summary>
    public required int MaxEventBytes { get; init; }

    /// <summary>How long a finished job's journal is kept before the record goes.</summary>
    public required double RetentionMinutes { get; init; }

    /// <summary>The longest a viewer's read may wait for an event that has not arrived.</summary>
    public required double MaxWaitSeconds { get; init; }

    /// <summary>The most bytes one message to or from a worker may carry.</summary>
    public required int MaxMessageBytes { get; init; }

    /// <summary>Messages a running job may hold before another is refused.</summary>
    public required int MaxPendingMessages { get; init; }

    /// <summary>The silence clock as a time span.</summary>
    public TimeSpan Silence => TimeSpan.FromSeconds(SilenceSeconds);

    /// <summary>The ceiling as a time span.</summary>
    public TimeSpan MaxDuration => TimeSpan.FromSeconds(MaxDurationSeconds);

    /// <summary>The retention as a time span.</summary>
    public TimeSpan Retention => TimeSpan.FromMinutes(RetentionMinutes);

    /// <summary>The wait cap as a time span.</summary>
    public TimeSpan MaxWait => TimeSpan.FromSeconds(MaxWaitSeconds);

    /// <summary>Reads <c>Pages:Jobs</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageJobOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Jobs");
        PageJobOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Jobs</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Jobs</c>, which the caller completes.</param>
    internal static PageJobOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        PageJobOptions options = new() {
            QueueCapacity = reader.Number("QueueCapacity", recommended: 8, minimum: 1, maximum: 256),
            MaxConcurrent = reader.Number("MaxConcurrent", recommended: 2, minimum: 1, maximum: 32),
            SilenceSeconds = reader.Decimal("SilenceSeconds", recommended: 120,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 3_600),
            MaxDurationSeconds = reader.Decimal("MaxDurationSeconds", recommended: 32_400,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 604_800),
            MaxEvents = reader.Number("MaxEvents", recommended: 512, minimum: 1, maximum: 100_000),
            MaxEventBytes = reader.Number("MaxEventBytes", recommended: 256 * 1024,
                minimum: 1024, maximum: 16 * 1024 * 1024),
            RetentionMinutes = reader.Decimal("RetentionMinutes", recommended: 30,
                minimum: 0.01, maximum: 1_440),
            MaxWaitSeconds = reader.Decimal("MaxWaitSeconds", recommended: 30,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 300),
            MaxMessageBytes = reader.Number("MaxMessageBytes", recommended: 16 * 1024,
                minimum: 1024, maximum: 1024 * 1024),
            MaxPendingMessages = reader.Number("MaxPendingMessages", recommended: 32,
                minimum: 1, maximum: 1_000),
        };
        return options;
    }
}
