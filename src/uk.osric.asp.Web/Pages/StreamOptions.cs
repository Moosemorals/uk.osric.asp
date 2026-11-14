// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds a response a page keeps writing after its first flush.</summary>
/// <remarks>
/// <para>
/// The render budget before the first flush is <see cref="RenderOptions.TimeoutSeconds"/>; from the
/// commit the question changes from "how long may this page run" to "is this stream still alive", and
/// these are the two clocks that answer it. Every value comes from <c>Pages:Stream</c>.
/// </para>
/// <para>
/// The silence clock is what catches a stall and is re-armed by every flush; the ceiling is what
/// stops a page that never stops producing. A stream that reaches either is truncated.
/// </para>
/// </remarks>
internal sealed record StreamOptions {

    /// <summary>Streams allowed to be open at once.</summary>
    /// <remarks>
    /// Separate from <see cref="RenderOptions.Slots"/> because a stream is mostly waiting while a
    /// render is mostly CPU. A stream releases its render slot at the commit and holds one of these
    /// until it ends.
    /// </remarks>
    public required int Slots { get; init; }

    /// <summary>How long a stream may go without a flush before it is cut.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public required double SilenceSeconds { get; init; }

    /// <summary>The longest a stream may stay open, measured from its commit.</summary>
    public required double MaxSeconds { get; init; }

    /// <summary>The silence clock as a time span.</summary>
    public TimeSpan Silence => TimeSpan.FromSeconds(SilenceSeconds);

    /// <summary>The ceiling as a time span.</summary>
    public TimeSpan Max => TimeSpan.FromSeconds(MaxSeconds);

    /// <summary>Reads <c>Pages:Stream</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static StreamOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Stream");
        StreamOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Stream</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Stream</c>, which the caller completes.</param>
    internal static StreamOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        StreamOptions options = new() {
            Slots = reader.Number("Slots", recommended: 8, minimum: 1, maximum: 64),
            SilenceSeconds = reader.Decimal("SilenceSeconds", recommended: 60,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 3_600),
            MaxSeconds = reader.Decimal("MaxSeconds", recommended: 1_800,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 86_400),
        };
        return options;
    }
}
