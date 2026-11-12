// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Bounds what one visitor's session may cost.</summary>
/// <remarks>
/// The caps are per session, not per page, so one page's keys can use the room another would have
/// had. An anonymous visitor gets the smaller set because a public page that writes on every visit
/// lets any client mint a row; a signed-in visitor has already been identified.
/// </remarks>
internal sealed record PageSessionOptions {

    /// <summary>The longest key, in characters, when none is configured.</summary>
    public const int DefaultMaxKeyChars = 200;

    /// <summary>The anonymous value cap, in bytes, when none is configured.</summary>
    public const int DefaultAnonymousMaxValueBytes = 4 * 1024;

    /// <summary>The anonymous key cap when none is configured.</summary>
    public const int DefaultAnonymousMaxKeys = 8;

    /// <summary>The anonymous total cap, in bytes, when none is configured.</summary>
    public const int DefaultAnonymousMaxTotalBytes = 16 * 1024;

    /// <summary>The signed-in value cap, in bytes, when none is configured.</summary>
    public const int DefaultSignedInMaxValueBytes = 16 * 1024;

    /// <summary>The signed-in key cap when none is configured.</summary>
    public const int DefaultSignedInMaxKeys = 32;

    /// <summary>The signed-in total cap, in bytes, when none is configured.</summary>
    public const int DefaultSignedInMaxTotalBytes = 64 * 1024;

    /// <summary>How long an anonymous session survives without a visit, when none is configured.</summary>
    public static readonly TimeSpan DefaultAnonymousIdle = TimeSpan.FromMinutes(30);

    /// <summary>How long a signed-in session survives without a visit, when none is configured.</summary>
    public static readonly TimeSpan DefaultSignedInIdle = TimeSpan.FromHours(8);

    /// <summary>The most live sessions the site keeps, when none is configured.</summary>
    public const int DefaultMaxLiveSessions = 2048;

    /// <summary>How long one session command may run, in seconds, when none is configured.</summary>
    public const int DefaultCommandTimeoutSeconds = 5;

    /// <summary>The longest key a page may use.</summary>
    public int MaxKeyChars { get; init; } = DefaultMaxKeyChars;

    /// <summary>The caps an anonymous visitor's session obeys.</summary>
    public PageSessionCaps Anonymous { get; init; } = PageSessionCaps.Anonymous;

    /// <summary>The caps a signed-in visitor's session obeys.</summary>
    public PageSessionCaps SignedIn { get; init; } = PageSessionCaps.SignedIn;

    /// <summary>How long an anonymous session survives without a visit.</summary>
    public TimeSpan AnonymousIdle { get; init; } = DefaultAnonymousIdle;

    /// <summary>How long a signed-in session survives without a visit.</summary>
    public TimeSpan SignedInIdle { get; init; } = DefaultSignedInIdle;

    /// <summary>The most live sessions the site keeps.</summary>
    public int MaxLiveSessions { get; init; } = DefaultMaxLiveSessions;

    /// <summary>How long one session command may run.</summary>
    public int CommandTimeoutSeconds { get; init; } = DefaultCommandTimeoutSeconds;

    /// <summary>The command timeout as a time span.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

    /// <summary>The caps for a session belonging to <paramref name="account"/>.</summary>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    public PageSessionCaps For(string? account) => account is null ? Anonymous : SignedIn;

    /// <summary>How long a session belonging to <paramref name="account"/> survives without a visit.</summary>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    public TimeSpan IdleFor(string? account) => account is null ? AnonymousIdle : SignedInIdle;

    /// <summary>Reads <c>Pages:Session</c>, falling back to the defaults when it is missing or silly.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The options to use; never values that would disable a cap.</returns>
    /// <remarks>The key length is <c>Pages:Storage:MaxKeyChars</c>, the same rule page state uses.</remarks>
    public static PageSessionOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Session");
        int maxKeyChars = Math.Max(16, configuration.GetSection("Pages:Storage").GetValue("MaxKeyChars", DefaultMaxKeyChars));

        return new PageSessionOptions {
            MaxKeyChars = maxKeyChars,
            Anonymous = PageSessionCaps.Read(section.GetSection("Anonymous"), PageSessionCaps.Anonymous),
            SignedIn = PageSessionCaps.Read(section.GetSection("SignedIn"), PageSessionCaps.SignedIn),
            AnonymousIdle = TimeSpan.FromMinutes(Math.Max(1, section.GetSection("Anonymous").GetValue("IdleMinutes", DefaultAnonymousIdle.TotalMinutes))),
            SignedInIdle = TimeSpan.FromMinutes(Math.Max(1, section.GetSection("SignedIn").GetValue("IdleMinutes", DefaultSignedInIdle.TotalMinutes))),
            MaxLiveSessions = Math.Max(1, section.GetValue("MaxLiveSessions", DefaultMaxLiveSessions)),
            CommandTimeoutSeconds = Math.Max(1, section.GetValue("CommandTimeoutSeconds", DefaultCommandTimeoutSeconds)),
        };
    }
}

/// <summary>The per-value and whole-session caps one kind of visitor's session obeys.</summary>
/// <param name="MaxValueBytes">The largest one JSON value may be, in bytes.</param>
/// <param name="MaxKeys">The most keys one session may hold.</param>
/// <param name="MaxTotalBytes">The most bytes a session's keys and values may total.</param>
internal sealed record PageSessionCaps(int MaxValueBytes, int MaxKeys, int MaxTotalBytes) {

    /// <summary>The anonymous caps used when none are configured.</summary>
    public static PageSessionCaps Anonymous { get; } = new(
        PageSessionOptions.DefaultAnonymousMaxValueBytes,
        PageSessionOptions.DefaultAnonymousMaxKeys,
        PageSessionOptions.DefaultAnonymousMaxTotalBytes);

    /// <summary>The signed-in caps used when none are configured.</summary>
    public static PageSessionCaps SignedIn { get; } = new(
        PageSessionOptions.DefaultSignedInMaxValueBytes,
        PageSessionOptions.DefaultSignedInMaxKeys,
        PageSessionOptions.DefaultSignedInMaxTotalBytes);

    /// <summary>Reads one kind's caps, clamping every value to what still bounds the session.</summary>
    /// <param name="section">The kind's configuration section.</param>
    /// <param name="fallback">The values to use when the section is missing.</param>
    internal static PageSessionCaps Read(IConfigurationSection section, PageSessionCaps fallback) {
        int maxValueBytes = Math.Max(1024, section.GetValue("MaxValueBytes", fallback.MaxValueBytes));
        int maxTotalBytes = Math.Max(1024, section.GetValue("MaxTotalBytes", fallback.MaxTotalBytes));

        return new PageSessionCaps(
            maxValueBytes,
            Math.Max(1, section.GetValue("MaxKeys", fallback.MaxKeys)),
            Math.Max(maxTotalBytes, maxValueBytes));
    }
}
