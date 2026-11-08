// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.AspNetCore.Antiforgery;
using uk.osric.asp.Core.Pages;

/// <summary>What the page endpoint needs beyond the renderer, gathered in one place.</summary>
/// <remarks>
/// Every member is a singleton, so the record is one too; it exists so the endpoint's signature stays
/// readable as the page contract grows a capability.
/// </remarks>
/// <param name="Reserved">The keys the site's own routes answer for.</param>
/// <param name="Layout">The page every HTML page is wrapped in unless it says otherwise.</param>
/// <param name="Render">The render budget and body cap.</param>
/// <param name="ContentTypes">The media type each page key's extension names.</param>
/// <param name="Storage">The storage caps.</param>
/// <param name="Fetch">The fetch allow-list and caps.</param>
/// <param name="StateStore">Where a page's state is read and written.</param>
/// <param name="Fetcher">Where a page's outbound request goes.</param>
/// <param name="Metrics">Where the endpoint's own refusals are recorded.</param>
/// <param name="Antiforgery">Validates a page's post and mints the field a form carries.</param>
internal sealed record PageEnvironment(
    ReservedPaths Reserved,
    PageLayoutConfiguration Layout,
    RenderOptions Render,
    PageContentTypes ContentTypes,
    PageStorageOptions Storage,
    PageFetchOptions Fetch,
    IPageStateStore StateStore,
    IPageFetcher Fetcher,
    PageMetrics Metrics,
    IAntiforgery Antiforgery);
