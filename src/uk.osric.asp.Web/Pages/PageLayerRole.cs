// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>What one layer of a render is, which decides what it may do.</summary>
/// <remarks>
/// One value rather than a flag beside a special case: the response gates, the stream refusals, the
/// offers of the write, the jobs and a writable session, and the layout slot all read it.
/// </remarks>
internal enum PageLayerRole {

    /// <summary>The page the visitor asked for: it owns the response and may be wrapped.</summary>
    Content,

    /// <summary>A page wrapping another: markup, with no response of its own.</summary>
    Layout,

    /// <summary>A page rendered inside another: a content page that owns no response.</summary>
    Embedded,
}
