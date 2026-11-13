// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What one tool call may use: who asked, the policy, the options, and the services it reaches.</summary>
/// <remarks>
/// Built once per call from the executor's singletons and the one scope it opens. The page editor and
/// the memory store are resolved only when a tool asks for them, so a tool that needs neither does not
/// instantiate one.
/// </remarks>
/// <param name="scope">The per-call scope the scoped services come from.</param>
/// <param name="context">The owner, conversation and run the call belongs to.</param>
/// <param name="paths">The path policy.</param>
/// <param name="options">The assistant's configured limits.</param>
/// <param name="metrics">Where the run's counters go.</param>
/// <param name="fetcher">The HTTP fetch behind <c>fetch</c>.</param>
/// <param name="diff">The renderer behind <c>diff_page</c>.</param>
internal sealed class ToolServices(
    IServiceProvider scope,
    ToolContext context,
    PagePathPolicy paths,
    AssistantOptions options,
    AssistantMetrics metrics,
    IAssistantFetcher fetcher,
    DiffRenderer diff) {

    /// <summary>The owner, conversation and run the call belongs to.</summary>
    public ToolContext Context { get; } = context;

    /// <summary>The path policy.</summary>
    public PagePathPolicy Paths { get; } = paths;

    /// <summary>The assistant's configured limits.</summary>
    public AssistantOptions Options { get; } = options;

    /// <summary>Where the run's counters go.</summary>
    public AssistantMetrics Metrics { get; } = metrics;

    /// <summary>The HTTP fetch behind <c>fetch</c>.</summary>
    public IAssistantFetcher Fetcher { get; } = fetcher;

    /// <summary>The renderer behind <c>diff_page</c>.</summary>
    public DiffRenderer Diff { get; } = diff;

    /// <summary>The page editor, resolved from the call's scope: the writes, and nothing else.</summary>
    public PageEditor Pages => scope.GetRequiredService<PageEditor>();

    /// <summary>The one read seam over the page store, made once for the call.</summary>
    public IPageStoreReader Store => _store ??= new PageStoreReader(Pages, Paths);

    /// <summary>The editor's own key, so a tool's link lands on the page the owner moved it to.</summary>
    public string EditorPath => EditorPaths.Of(scope);

    /// <summary>The owner's memories, resolved from the call's scope.</summary>
    public IMemoryStore Memories => scope.GetRequiredService<IMemoryStore>();

    /// <summary>Shapes and bounds what a tool answers with.</summary>
    public ToolResults Results { get; } = new(options);

    private IPageStoreReader? _store;
}
