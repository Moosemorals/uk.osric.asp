// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Configuration;

/// <summary>Bounds what one compilation may cost.</summary>
/// <remarks>
/// Roslyn honours a cancellation token between operations rather than inside one, so the timeout is a
/// coarse bound on a pathological template, not a hard one. The slot count is the part that holds.
/// </remarks>
public sealed record CompileOptions {

    /// <summary>Compiles allowed to run at once when none is configured.</summary>
    public const int DefaultSlots = 1;

    /// <summary>The budget for one compile, in seconds, when none is configured.</summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>Compiles allowed to run at once.</summary>
    public int Slots { get; init; } = DefaultSlots;

    /// <summary>The budget for one compile.</summary>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Compile</c>, falling back to the defaults.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use.</returns>
    public static CompileOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Compile");

        return new CompileOptions {
            Slots = Math.Max(1, section.GetValue("Slots", DefaultSlots)),
            TimeoutSeconds = Math.Max(1, section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds)),
        };
    }
}
