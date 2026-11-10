// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>Who asked for an outbound request, for its <c>User-Agent</c>.</summary>
internal enum OutboundOrigin {
    /// <summary>Stored page code calling <c>IHttpFetch</c>.</summary>
    Page,

    /// <summary>The assistant's <c>fetch</c> tool or its provider request.</summary>
    Assistant,
}

/// <summary>Builds the short <c>User-Agent</c> every outbound request carries.</summary>
/// <remarks>
/// The host and the origin let a remote service see who is calling without a version, a contact URL or
/// anything ambient. The host sets it after a page's own headers, so a page cannot claim to be a
/// different client.
/// </remarks>
internal static class OutboundUserAgent {

    /// <summary>The site's hostname, which identifies the caller.</summary>
    public const string Host = "moosemorals.com";

    /// <summary>The value sent for one origin.</summary>
    /// <param name="origin">Whether page code or the assistant asked.</param>
    public static string For(OutboundOrigin origin) => origin switch {
        OutboundOrigin.Assistant => $"{Host} (assistant)",
        _ => $"{Host} (page)",
    };
}
