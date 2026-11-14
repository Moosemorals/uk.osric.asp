// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Configuration;

/// <summary>Bounds what one visitor's session may cost.</summary>
/// <remarks>
/// The caps are per session, not per page, so one page's keys can use the room another would have
/// had. An anonymous visitor gets the smaller set because a public page that writes on every visit
/// lets any client mint a row; a signed-in visitor has already been identified. Every value comes
/// from <c>Pages:Session</c>, and the key length is <c>Pages:Storage:MaxKeyChars</c>, the same rule
/// page state uses.
/// </remarks>
internal sealed record PageSessionOptions {

    /// <summary>The longest key a page may use.</summary>
    public required int MaxKeyChars { get; init; }

    /// <summary>The caps an anonymous visitor's session obeys.</summary>
    public required PageSessionCaps Anonymous { get; init; }

    /// <summary>The caps a signed-in visitor's session obeys.</summary>
    public required PageSessionCaps SignedIn { get; init; }

    /// <summary>How long an anonymous session survives without a visit.</summary>
    public required TimeSpan AnonymousIdle { get; init; }

    /// <summary>How long a signed-in session survives without a visit.</summary>
    public required TimeSpan SignedInIdle { get; init; }

    /// <summary>The most live sessions the site keeps.</summary>
    public required int MaxLiveSessions { get; init; }

    /// <summary>How long one session command may run.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>The command timeout as a time span.</summary>
    public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

    /// <summary>The caps for a session belonging to <paramref name="account"/>.</summary>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    public PageSessionCaps For(string? account) => account is null ? Anonymous : SignedIn;

    /// <summary>How long a session belonging to <paramref name="account"/> survives without a visit.</summary>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    public TimeSpan IdleFor(string? account) => account is null ? AnonymousIdle : SignedInIdle;

    /// <summary>Reads <c>Pages:Session</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static PageSessionOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Session");
        PageSessionOptions options = new() {
            MaxKeyChars = reader.At("Pages:Storage").Number("MaxKeyChars", recommended: 200, minimum: 16, maximum: 4_096),
            Anonymous = PageSessionCaps.Read(reader.For("Anonymous"), PageSessionCaps.Anonymous),
            SignedIn = PageSessionCaps.Read(reader.For("SignedIn"), PageSessionCaps.SignedIn),
            AnonymousIdle = reader.For("Anonymous").Minutes("IdleMinutes", recommended: 30, minimum: 1, maximum: 525_600),
            SignedInIdle = reader.For("SignedIn").Minutes("IdleMinutes", recommended: 480, minimum: 1, maximum: 525_600),
            MaxLiveSessions = reader.Number("MaxLiveSessions", recommended: 2_048, minimum: 1, maximum: 1_000_000),
            CommandTimeoutSeconds = reader.Number("CommandTimeoutSeconds", recommended: 5, minimum: 1, maximum: 120),
        };
        reader.Complete();
        return options;
    }
}

/// <summary>The per-value and whole-session caps one kind of visitor's session obeys.</summary>
/// <param name="MaxValueBytes">The largest one JSON value may be, in bytes.</param>
/// <param name="MaxKeys">The most keys one session may hold.</param>
/// <param name="MaxTotalBytes">The most bytes a session's keys and values may total.</param>
internal sealed record PageSessionCaps(int MaxValueBytes, int MaxKeys, int MaxTotalBytes) {

    /// <summary>The anonymous caps the shipped configuration carries.</summary>
    public static PageSessionCaps Anonymous { get; } = new(4 * 1024, 8, 16 * 1024);

    /// <summary>The signed-in caps the shipped configuration carries.</summary>
    public static PageSessionCaps SignedIn { get; } = new(16 * 1024, 32, 64 * 1024);

    /// <summary>Reads one kind's caps.</summary>
    /// <param name="reader">The kind's configuration, opened on its own section.</param>
    /// <param name="recommended">The caps the shipped configuration carries, for the messages.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    internal static PageSessionCaps Read(ConfigurationReader reader, PageSessionCaps recommended) {
        int maxValueBytes = reader.Number("MaxValueBytes", recommended.MaxValueBytes,
            minimum: 1024, maximum: 1024 * 1024);
        int maxTotalBytes = reader.Number("MaxTotalBytes", recommended.MaxTotalBytes,
            minimum: 1024, maximum: 16 * 1024 * 1024);
        reader.FailIf(maxTotalBytes < maxValueBytes, "MaxTotalBytes",
            $"is {maxTotalBytes}, which is smaller than MaxValueBytes ({maxValueBytes})");

        return new PageSessionCaps(
            maxValueBytes,
            reader.Number("MaxKeys", recommended.MaxKeys, minimum: 1, maximum: 1_024),
            maxTotalBytes);
    }
}
