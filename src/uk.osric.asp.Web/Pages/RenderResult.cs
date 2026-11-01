// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core;

/// <summary>The outcome of rendering a page.</summary>
/// <param name="Status">What rendering did.</param>
/// <param name="Diagnostics">
/// The compiler's author-facing diagnostics when <paramref name="Status"/> is
/// <see cref="RenderStatus.CompileFailed"/>; empty otherwise.
/// </param>
public sealed record RenderResult(RenderStatus Status, IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>No page is stored under the requested key.</summary>
    public static RenderResult NotFound { get; } = new(RenderStatus.NotFound, []);

    /// <summary>The page's handler ran.</summary>
    public static RenderResult Rendered { get; } = new(RenderStatus.Rendered, []);

    /// <summary>The page outran its budget; its output was discarded.</summary>
    public static RenderResult TimedOut { get; } = new(RenderStatus.Timeout, []);

    /// <summary>The page does not compile.</summary>
    /// <param name="diagnostics">The compiler's author-facing diagnostics.</param>
    public static RenderResult CompileFailed(IReadOnlyList<PageDiagnostic> diagnostics) =>
        new(RenderStatus.CompileFailed, diagnostics);
}
