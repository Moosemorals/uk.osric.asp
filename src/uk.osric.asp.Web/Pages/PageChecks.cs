// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>The compile tier a check spends, gathered so the environment grows one member.</summary>
/// <remarks>
/// A check needs the compiler, the live libraries and the compile budget that a save already has;
/// this is the one place they are held together for a check. A host without one offers no check,
/// exactly as a host without jobs offers none.
/// </remarks>
internal sealed class PageChecks(
    IPageCompiler compiler,
    PageLibraries libraries,
    PageBudget budget,
    PageMetrics metrics,
    ILoggerFactory loggerFactory) {

    /// <summary>Binds the tier to one layer of a render, or to a tool call with no page.</summary>
    /// <param name="path">The layer's key, which its logs and metrics are tagged with, or null.</param>
    /// <returns>The check that caller uses.</returns>
    public PageCheckScope For(PagePath? path) =>
        new(path, compiler, libraries, budget, metrics, loggerFactory.CreateLogger<PageCheckScope>());
}
