// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one compilation may cost.</summary>
/// <remarks>
/// Roslyn honours a cancellation token between operations rather than inside one, so the timeout is a
/// coarse bound on a pathological template, not a hard one. The slot count is the part that holds.
/// Every value comes from <c>Pages:Compile</c>.
/// </remarks>
internal sealed record CompileOptions {

    /// <summary>Compiles allowed to run at once.</summary>
    public required int Slots { get; init; }

    /// <summary>The budget for one compile, in seconds.</summary>
    /// <remarks>May be a fraction of a second, down to <see cref="PageBudget.MinimumTimeoutSeconds"/>.</remarks>
    public required double TimeoutSeconds { get; init; }

    /// <summary>The budget as a time span.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>Reads <c>Pages:Compile</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static CompileOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Compile");
        CompileOptions options = new() {
            Slots = reader.Number("Slots", recommended: 1, minimum: 1, maximum: 32),
            TimeoutSeconds = reader.Decimal("TimeoutSeconds", recommended: 5,
                minimum: PageBudget.MinimumTimeoutSeconds, maximum: 60),
        };
        reader.Complete();
        return options;
    }
}
