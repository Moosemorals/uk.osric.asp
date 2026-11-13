// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The script policy a page key's extension gives its response.</summary>
/// <remarks>
/// The policy follows the key's extension, not the response's final content type: a page that calls
/// <c>SetContentType</c> keeps the policy its row declares. The shell writes it as
/// <c>script-src 'none'</c> or <c>script-src 'self'</c>; inline script and event-handler attributes
/// stay blocked either way.
/// </remarks>
public enum PageScriptPolicy {
    /// <summary>No script may load.</summary>
    None = 0,

    /// <summary>A same-origin external script may load.</summary>
    Self = 1,
}
