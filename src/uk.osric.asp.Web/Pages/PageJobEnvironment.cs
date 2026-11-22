// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Work;

/// <summary>What a job needs from the shell, gathered in one place.</summary>
/// <remarks>
/// It is the job's half of <see cref="PageEnvironment"/>: the same stores and caps a render uses, minus
/// everything about a response, a visitor's session or a layout, because a job has none of those.
/// </remarks>
/// <param name="Jobs">The job bounds.</param>
/// <param name="Store">Where a job reads its worker and stores a rebuilt image.</param>
/// <param name="StoreValues">Where the job's values are read and written.</param>
/// <param name="Stores">The page stores' caps and identity bounds.</param>
/// <param name="SessionStore">The session identity, which a job reads its starter through.</param>
/// <param name="Fetcher">Where a worker's outbound request goes.</param>
/// <param name="Fetch">The outbound fetch caps.</param>
/// <param name="Seeds">The pack this release ships, so a worker's catalog can describe it.</param>
/// <param name="ReadStore">Where a worker's catalog reads the page store.</param>
/// <param name="Read">The caps on that read.</param>
/// <param name="Search">The allowance, limits and snippet width a worker's search runs with.</param>
/// <param name="Reserved">The keys the site's own routes answer for.</param>
/// <param name="ContentTypes">The media type each page key's extension names.</param>
/// <param name="Images">Builds a worker's image when its library has moved.</param>
/// <param name="Libraries">The live libraries a worker may be compiled with.</param>
/// <param name="Compiler">Loads the worker's image and finds its entry point.</param>
/// <param name="Metrics">Where a worker's own fetches are recorded.</param>
/// <param name="Scopes">
/// Where a job's own scope comes from, so its writer has the scoped store a render's writer has.
/// </param>
internal sealed record PageJobEnvironment(
    PageJobOptions Jobs,
    IWorkerStore Store,
    IStoreValues StoreValues,
    PageStoresOptions Stores,
    IPageSessionStore SessionStore,
    IPageFetcher Fetcher,
    PageFetchOptions Fetch,
    IPageReadStore ReadStore,
    PageSeeds Seeds,
    PageReadOptions Read,
    PageSourceScan Search,
    ReservedPaths Reserved,
    PageContentTypes ContentTypes,
    PageImageBuilder Images,
    PageLibraries Libraries,
    IPageCompiler Compiler,
    PageMetrics Metrics,
    IServiceScopeFactory Scopes);
