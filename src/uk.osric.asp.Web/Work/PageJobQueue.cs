// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>One job waiting for a runner.</summary>
/// <param name="Id">The job's id, which is also how the registry finds it.</param>
/// <param name="Worker">The worker's canonical key.</param>
/// <param name="Page">The starting page's canonical key.</param>
/// <param name="Authority">The credential the job acts with.</param>
internal sealed record PageJobRequest(string Id, PagePath Worker, PagePath Page, PageJobAuthority Authority);

/// <summary>The queue a start is written to and the runner reads.</summary>
internal sealed class PageJobQueue(PageJobOptions options)
    : BoundedWorkQueue<PageJobRequest>(options.QueueCapacity);
