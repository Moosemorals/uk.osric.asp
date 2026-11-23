// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.AspNetCore.Antiforgery;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Work;

/// <summary>What the page endpoint needs beyond the renderer, gathered in one place.</summary>
/// <remarks>
/// Every member is a singleton, so the record is one too; it exists so the endpoint's signature stays
/// readable as the page contract grows a capability.
/// </remarks>
/// <param name="Reserved">The keys the site's own routes answer for.</param>
/// <param name="Editor">The one key offered the editor capability.</param>
/// <param name="Layout">The page every HTML page with code is wrapped in unless it says otherwise.</param>
/// <param name="Render">The render budget and body limit.</param>
/// <param name="ContentTypes">The media type and script policy each page key's extension names.</param>
/// <param name="Stores">The page stores' limits and identity bounds.</param>
/// <param name="Fetch">The outbound fetch limits.</param>
/// <param name="Read">The limits on a page's read of the page store.</param>
/// <param name="Search">The allowance, limits and snippet width a page's search runs with.</param>
/// <param name="StoreValues">Where a page's values are read and written.</param>
/// <param name="Messages">Where a page's messages are queued, taken and peeked.</param>
/// <param name="Objects">Where a page's objects are stored and read.</param>
/// <param name="ReadStore">Where a page's catalog reads the page store.</param>
/// <param name="Seeds">The pack this release ships, read once.</param>
/// <param name="Saver">Where a page's save, and the editor's, compiles and stores a revision.</param>
/// <param name="Fetcher">Where a page's outbound request goes.</param>
/// <param name="Metrics">Where the endpoint's own refusals are recorded.</param>
/// <param name="Antiforgery">Validates a page's post and mints the field a form carries.</param>
/// <param name="Markdown">The renderer every page layer offers as <c>IMarkdownRenderer</c>.</param>
/// <param name="Diff">The renderer every page layer offers as <c>IDiffRenderer</c>.</param>
/// <param name="SessionStore">Where a visitor's session identity is read and written.</param>
/// <param name="SessionCookie">The cookie that names the visitor's session.</param>
/// <param name="BrowserCookie">The cookie that names the visitor's browser.</param>
/// <param name="Clock">The clock a render's fetch and read allowances are measured against.</param>
/// <param name="Jobs">
/// Where a page's jobs are started and read, or <see langword="null"/> when this host offers none. A
/// host without one simply does not offer the capability, which is what a page is already prepared for.
/// </param>
/// <param name="Checks">
/// Where a page's checks compile, or <see langword="null"/> when this host offers none. A host
/// without one offers no <c>IPageCheck</c>, and a render without a signed-in visitor is offered none
/// either way.
/// </param>
/// <param name="Records">
/// Where a record handle finds its declared tables and the database, or <see langword="null"/> when
/// this host has no store: a handle then reports that it has none rather than reading a table that is
/// not there.
/// </param>
/// <param name="JobAuthorities">
/// Where a render's credential is captured for the jobs it starts, or <see langword="null"/> when this
/// host offers none. A host without one starts no job from a render, because a job must act with its
/// starter's credential or not at all.
/// </param>
internal sealed record PageEnvironment(
    ReservedPaths Reserved,
    PageEditorOptions Editor,
    PageLayoutOptions Layout,
    RenderOptions Render,
    PageContentTypes ContentTypes,
    PageStoresOptions Stores,
    PageFetchOptions Fetch,
    PageReadOptions Read,
    PageSourceScan Search,
    IStoreValues StoreValues,
    IStoreMessages Messages,
    IStoreObjects Objects,
    IPageReadStore ReadStore,
    PageSeeds Seeds,
    PageSaver Saver,
    IPageFetcher Fetcher,
    PageMetrics Metrics,
    IAntiforgery Antiforgery,
    MarkdownRenderer Markdown,
    DiffRenderer Diff,
    IPageSessionStore SessionStore,
    PageSessionCookieOptions SessionCookie,
    PageBrowserCookieOptions BrowserCookie,
    TimeProvider Clock,
    PageJobs? Jobs = null,
    PageChecks? Checks = null,
    PageJobAuthorityFactory? JobAuthorities = null,
    IRecordCatalog? Records = null);
