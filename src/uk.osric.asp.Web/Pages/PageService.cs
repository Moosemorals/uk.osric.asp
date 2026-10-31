// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

/// <summary>Compiles, stores and renders pages.</summary>
/// <remarks>
/// A page is compiled when it is saved. A page whose stored image is missing, was built from other
/// content, or was built by another build is compiled on its next request and stored, so a page is
/// never unrunnable just because it did not arrive through the editor.
/// </remarks>
public sealed class PageService(
    IStorage storage,
    ITemplateCompiler compiler,
    PageMetrics metrics,
    ILogger<PageService> logger) {

    // A page can be saved while its previous version is compiling; each attempt re-reads the newer row.
    private const int MaxCompileAttempts = 3;

    /// <summary>Reads a page for the editor without compiling it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<StoredPage?> GetAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetPageAsync(path, cancellationToken);
    }

    /// <summary>Compiles a page and stores its source and result together.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="content">The template source.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The compiler's diagnostics; empty when the page compiled.</returns>
    public async Task<IReadOnlyList<string>> SaveAsync(PagePath path, string content, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();

        PageCompilation compilation = Compile(path, content, PageMetrics.ReasonSave);
        await storage.SavePageAsync(new StoredPage(path, content, compilation), cancellationToken);

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, compilation.Image is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Path, duration.TotalMilliseconds,
            compilation.Image is null ? "with errors" : "compiled");

        return compilation.Diagnostics;
    }

    /// <summary>Runs a page, compiling and storing it first when its stored image cannot be used.</summary>
    /// <param name="path">The canonical key to render.</param>
    /// <param name="context">The context the page writes to.</param>
    /// <param name="cancellationToken">Cancels the read and any store.</param>
    public async Task<RenderResult> RenderAsync(PagePath path, IContext context, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();
        string outcome = PageMetrics.RenderError;
        string cache = PageMetrics.CacheNone;

        try {
            LoadedPage? loaded = await LoadAsync(path, cancellationToken);
            if (loaded is null) {
                outcome = PageMetrics.RenderNotFound;
                return RenderResult.NotFound;
            }

            cache = loaded.Cache;

            if (loaded.Compilation.Image is null) {
                outcome = PageMetrics.RenderCompileError;
                return RenderResult.CompileFailed(loaded.Compilation.Diagnostics);
            }

            Run(path, loaded.Compilation.Image, context);
            outcome = PageMetrics.RenderOk;

            return RenderResult.Rendered;
        } finally {
            // Reaching here by exception leaves the outcome as `error`, which is what a dashboard
            // should see: the page was neither served nor reported as a compile failure.
            metrics.RecordRender(path, outcome, cache, Stopwatch.GetElapsedTime(start));
        }
    }

    private async Task<LoadedPage?> LoadAsync(PagePath path, CancellationToken cancellationToken) {
        LoadedPage? fallback = null;

        for (int attempt = 1; attempt <= MaxCompileAttempts; attempt++) {
            StoredPage? page = await storage.GetPageAsync(path, cancellationToken);
            if (page is null) {
                metrics.RecordCacheLookup(PageMetrics.LookupAbsent);
                return null;
            }

            string contentHash = ContentFingerprint.Of(page.Content);
            if (page.Compilation is { } stored && IsCurrent(stored, contentHash)) {
                metrics.RecordCacheLookup(PageMetrics.LookupHit);
                return new LoadedPage(stored, PageMetrics.CacheHit);
            }

            metrics.RecordStaleness(path, StalenessReason(page.Compilation));
            metrics.RecordCacheLookup(PageMetrics.LookupStale);

            PageCompilation compilation = Compile(path, page.Content, PageMetrics.ReasonLazy);
            fallback = new LoadedPage(compilation, PageMetrics.CacheMiss);

            if (await storage.TrySetCompilationAsync(path, page.Content, compilation, cancellationToken)) {
                return fallback;
            }

            logger.LogDebug("Discarded a compile of {Path} because the page changed while it compiled", path.Path);
        }

        // The page kept changing; serving the last compile is better than failing the request.
        return fallback;
    }

    private bool IsCurrent(PageCompilation compilation, string contentHash) {
        return string.Equals(compilation.ContentHash, contentHash, StringComparison.Ordinal)
            && string.Equals(compilation.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal);
    }

    private string StalenessReason(PageCompilation? compilation) {
        if (compilation is null) {
            return PageMetrics.StaleNeverCompiled;
        }

        return string.Equals(compilation.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal)
            ? PageMetrics.StaleContentChanged
            : PageMetrics.StaleRuntimeChanged;
    }

    private PageCompilation Compile(PagePath path, string content, string reason) {
        long start = Stopwatch.GetTimestamp();
        CompileResult result = compiler.Compile(TemplateSourceGenerator.GenerateTemplateSource(content));

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordCompile(path, result.Success, reason, duration);
        if (result.Image is { } image) {
            metrics.RecordImageSize(path, image.Length);
        }

        logger.LogDebug("Compiled {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Path, duration.TotalMilliseconds,
            result.Success ? "ok" : "error");

        return new PageCompilation(ContentFingerprint.Of(content), compiler.RuntimeId, result.Image, result.Diagnostics);
    }

    private void Run(PagePath path, byte[] image, IContext context) {
        long start = Stopwatch.GetTimestamp();
        using CompiledTemplate template = compiler.Load(image);

        metrics.RecordLoad(path, Stopwatch.GetElapsedTime(start));

        long invokeStart = Stopwatch.GetTimestamp();
        try {
            template.Invoke(context);
            metrics.RecordInvoke(path, true, Stopwatch.GetElapsedTime(invokeStart));
        } catch {
            metrics.RecordInvoke(path, false, Stopwatch.GetElapsedTime(invokeStart));
            throw;
        }
    }

    private sealed record LoadedPage(PageCompilation Compilation, string Cache);
}
