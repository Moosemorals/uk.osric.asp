// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>What rendering a page did.</summary>
internal enum RenderStatus {

    /// <summary>The page's handler ran; its output is in the request's context.</summary>
    Rendered,

    /// <summary>The page does not compile; its diagnostics explain why.</summary>
    CompileFailed,

    /// <summary>No page is stored under the requested key.</summary>
    NotFound,

    /// <summary>The page outran its execution budget, so the answer was given up on.</summary>
    Timeout,
}
