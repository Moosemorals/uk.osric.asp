// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Configuration;

/// <summary>Bounds what one page render may cost the process that runs it.</summary>
/// <remarks>
/// The deadline is the whole budget for a request: queueing for a slot counts against it, because a
/// visitor is waiting either way. The defaults are sized against a page render of a few milliseconds,
/// so the timeout can only fire on something pathological.
/// </remarks>
public sealed record RenderOptions {

    /// <summary>The budget for one render, in seconds, when none is configured.</summary>
    public const int DefaultTimeoutSeconds = 5;

    /// <summary>Renders allowed to run at once when none is configured.</summary>
    public const int DefaultSlots = 2;

    /// <summary>The buffered body, in characters, when none is configured.</summary>
    public const int DefaultMaxBodyChars = 4 * 1024 * 1024;

    /// <summary>Abandoned renders alive at once before the health check fails.</summary>
    public const int DefaultAbandonedLimit = 2;

    /// <summary>The whole budget for one render, including the wait for a slot.</summary>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>Page renders allowed to run at once; further requests wait without a thread.</summary>
    public int Slots { get; init; } = DefaultSlots;

    /// <summary>How much body a page may buffer before it is refused.</summary>
    public int MaxBodyChars { get; init; } = DefaultMaxBodyChars;

    /// <summary>Abandoned renders alive at once before the health check reports unhealthy.</summary>
    public int AbandonedLimit { get; init; } = DefaultAbandonedLimit;

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Render</c>, falling back to the defaults when it is missing or silly.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never values that would disable the budget.</returns>
    public static RenderOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Render");

        return new RenderOptions {
            TimeoutSeconds = Math.Max(1, section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds)),
            Slots = Math.Max(1, section.GetValue("Slots", DefaultSlots)),
            MaxBodyChars = Math.Max(1024, section.GetValue("MaxBodyChars", DefaultMaxBodyChars)),
            AbandonedLimit = Math.Max(1, section.GetValue("AbandonedLimit", DefaultAbandonedLimit)),
        };
    }
}
