// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Configuration;

/// <summary>Bounds what one page's key/value store may cost.</summary>
/// <remarks>
/// Every cap is clamped to a safe minimum, and <see cref="MaxPageBytes"/> is never allowed below
/// <see cref="MaxValueBytes"/>, so a single value at the per-value limit still fits an empty page.
/// </remarks>
internal sealed record PageStorageOptions {

    /// <summary>The longest key, in characters, when none is configured.</summary>
    public const int DefaultMaxKeyChars = 200;

    /// <summary>One value, in bytes, when none is configured.</summary>
    public const int DefaultMaxValueBytes = 64 * 1024;

    /// <summary>Keys one page may hold, when none is configured.</summary>
    public const int DefaultMaxKeysPerPage = 256;

    /// <summary>One page's keys and values together, in bytes, when none is configured.</summary>
    public const int DefaultMaxPageBytes = 256 * 1024;

    /// <summary>One storage command, in seconds, when none is configured.</summary>
    public const int DefaultCommandTimeoutSeconds = 5;

    /// <summary>The longest key a page may use.</summary>
    public int MaxKeyChars { get; init; } = DefaultMaxKeyChars;

    /// <summary>The largest one JSON value may be, in bytes.</summary>
    public int MaxValueBytes { get; init; } = DefaultMaxValueBytes;

    /// <summary>The most keys one page may hold.</summary>
    public int MaxKeysPerPage { get; init; } = DefaultMaxKeysPerPage;

    /// <summary>The most bytes one page's keys and values may total.</summary>
    public int MaxPageBytes { get; init; } = DefaultMaxPageBytes;

    /// <summary>How long one storage command may run.</summary>
    public int CommandTimeoutSeconds { get; init; } = DefaultCommandTimeoutSeconds;

    /// <summary>The command timeout as a time span.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

    /// <summary>Reads <c>Pages:Storage</c>, falling back to the defaults when it is missing or silly.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never values that would disable a cap.</returns>
    public static PageStorageOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Storage");
        int maxValueBytes = Math.Max(1024, section.GetValue("MaxValueBytes", DefaultMaxValueBytes));
        int maxPageBytes = Math.Max(1024, section.GetValue("MaxPageBytes", DefaultMaxPageBytes));

        return new PageStorageOptions {
            MaxKeyChars = Math.Max(16, section.GetValue("MaxKeyChars", DefaultMaxKeyChars)),
            MaxValueBytes = maxValueBytes,
            MaxKeysPerPage = Math.Max(1, section.GetValue("MaxKeysPerPage", DefaultMaxKeysPerPage)),
            MaxPageBytes = Math.Max(maxPageBytes, maxValueBytes),
            CommandTimeoutSeconds = Math.Max(1, section.GetValue("CommandTimeoutSeconds", DefaultCommandTimeoutSeconds)),
        };
    }
}
