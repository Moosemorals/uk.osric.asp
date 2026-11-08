// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Bounds what one page render may cost the process that runs it.</summary>
/// <remarks>
/// The deadline is the whole budget for a request: queueing for a slot counts against it, because a
/// visitor is waiting either way. The defaults are sized against a page render of a few milliseconds,
/// so the timeout can only fire on something pathological.
/// </remarks>
internal sealed record RenderOptions {

    /// <summary>The budget for one render, in seconds, when none is configured.</summary>
    public const double DefaultTimeoutSeconds = 5;

    /// <summary>Renders allowed to run at once when none is configured.</summary>
    public const int DefaultSlots = 2;

    /// <summary>The buffered body, in characters, when none is configured.</summary>
    public const int DefaultMaxBodyChars = 4 * 1024 * 1024;

    /// <summary>Abandoned renders alive at once before the health check fails.</summary>
    public const int DefaultAbandonedLimit = 2;

    /// <summary>How many layouts one page may be wrapped in, when none is configured.</summary>
    public const int DefaultMaxLayoutDepth = 4;

    /// <summary>The deepest chain a configuration may ask for.</summary>
    public const int MaximumLayoutDepth = 16;

    /// <summary>The whole budget for one render, including the wait for a slot.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public double TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>Page renders allowed to run at once; further requests wait without a thread.</summary>
    public int Slots { get; init; } = DefaultSlots;

    /// <summary>How much body a page may buffer before it is refused.</summary>
    public int MaxBodyChars { get; init; } = DefaultMaxBodyChars;

    /// <summary>Abandoned renders alive at once before the health check reports unhealthy.</summary>
    public int AbandonedLimit { get; init; } = DefaultAbandonedLimit;

    /// <summary>How many layouts one page may be wrapped in, from <c>Pages:Render:MaxLayoutDepth</c>.</summary>
    public int MaxLayoutDepth { get; init; } = DefaultMaxLayoutDepth;

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Render</c>, falling back to the defaults when it is missing or silly.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never values that would disable the budget.</returns>
    public static RenderOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Render");

        return new RenderOptions {
            TimeoutSeconds = Math.Max(PageBudget.MinimumTimeoutSeconds, section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds)),
            Slots = Math.Max(1, section.GetValue("Slots", DefaultSlots)),
            MaxBodyChars = Math.Max(1024, section.GetValue("MaxBodyChars", DefaultMaxBodyChars)),
            AbandonedLimit = Math.Max(1, section.GetValue("AbandonedLimit", DefaultAbandonedLimit)),
            MaxLayoutDepth = Math.Clamp(
                section.GetValue("MaxLayoutDepth", DefaultMaxLayoutDepth), 1, MaximumLayoutDepth),
        };
    }
}
