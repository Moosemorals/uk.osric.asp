// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one page render may cost the process that runs it.</summary>
/// <remarks>
/// Every value is read from <c>Pages:Render</c> and none has a fallback: a missing or unusable value
/// stops the site at startup. The deadline is the whole budget for a request: queueing for a slot
/// counts against it, because a visitor is waiting either way.
/// </remarks>
internal sealed record RenderOptions {

    /// <summary>The key that sets a render's whole budget, named in a page's bounds.</summary>
    public const string TimeoutSecondsKey = "Pages:Render:TimeoutSeconds";

    /// <summary>The key that sets how much body one page may buffer, named in a page's bounds.</summary>
    public const string MaxBodyCharsKey = "Pages:Render:MaxBodyChars";

    /// <summary>The key that sets how many embeds one render may make, named in a page's bounds.</summary>
    public const string MaxEmbedsKey = "Pages:Render:MaxEmbeds";

    /// <summary>The deepest chain a configuration may ask for.</summary>
    public const int MaximumLayoutDepth = 16;

    /// <summary>The deepest embed a configuration may ask for.</summary>
    public const int MaximumEmbedDepth = 16;

    /// <summary>The whole budget for one render, including the wait for a slot.</summary>
    public required double TimeoutSeconds { get; init; }

    /// <summary>Page renders allowed to run at once; further requests wait without a thread.</summary>
    public required int Slots { get; init; }

    /// <summary>
    /// How much body a page may buffer before it is refused. It stops at the first flush: a streaming
    /// page's body is on the wire and is not charged after its response has been committed.
    /// </summary>
    public required int MaxBodyChars { get; init; }

    /// <summary>Abandoned renders alive at once before the health check reports unhealthy.</summary>
    public required int AbandonedLimit { get; init; }

    /// <summary>How many layouts one page may be wrapped in, up to <see cref="MaximumLayoutDepth"/>.</summary>
    public required int MaxLayoutDepth { get; init; }

    /// <summary>How deep embeds may nest below the layer that is running, up to <see cref="MaximumEmbedDepth"/>.</summary>
    /// <remarks>
    /// Chosen apart from <see cref="MaxLayoutDepth"/> because a widget is not chrome: a listing that
    /// draws each row through a page needs depth the layout chain should not grant.
    /// </remarks>
    public required int MaxEmbedDepth { get; init; }

    /// <summary>How many embeds one request may make, which bounds a listing's fan-out.</summary>
    /// <remarks>
    /// Depth alone lets one listing embed a thousand siblings under one slot and one deadline.
    /// </remarks>
    public required int MaxEmbeds { get; init; }

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>The count a rate spends in a whole render, never less than one.</summary>
    /// <param name="perSecond">A configured rate: a fetch rate or a read rate.</param>
    /// <returns>How many whole units the rate buys in one deadline, at least one.</returns>
    /// <remarks>
    /// <para>
    /// It is the one derivation of a burst. An allowance starts full at that many units, so nothing a
    /// deployment could spend before is refused now, and no second key can disagree with the rate
    /// that sets it.
    /// </para>
    /// <para>
    /// The floor of one unit is what keeps the derivation from switching the allowance off: a rate
    /// that spends less than a unit in the deadline would otherwise start empty of the thing it is
    /// meant to meter, and the two keys it reads — a fractional render deadline and a modest rate —
    /// are each valid on their own. One unit is the least that can always be spent once.
    /// </para>
    /// </remarks>
    public int BurstFor(double perSecond) => Math.Max(1, (int)Math.Floor(perSecond * TimeoutSeconds));

    /// <summary>Reads <c>Pages:Render</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static RenderOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Render");
        RenderOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Render</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Render</c>, which the caller completes.</param>
    internal static RenderOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        RenderOptions options = new() {
            TimeoutSeconds = reader.Decimal("TimeoutSeconds", recommended: 5,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 60),
            Slots = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64),
            MaxBodyChars = reader.Number("MaxBodyChars", recommended: 4 * 1024 * 1024,
                minimum: 1024, maximum: 64 * 1024 * 1024),
            AbandonedLimit = reader.Number("AbandonedLimit", recommended: 2, minimum: 1, maximum: 64),
            MaxLayoutDepth = reader.Number("MaxLayoutDepth", recommended: 4, minimum: 1, maximum: MaximumLayoutDepth),
            MaxEmbedDepth = reader.Number("MaxEmbedDepth", recommended: 4, minimum: 1, maximum: MaximumEmbedDepth),
            MaxEmbeds = reader.Number("MaxEmbeds", recommended: 32, minimum: 1, maximum: 256),
        };
        return options;
    }
}
