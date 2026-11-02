// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core;

/// <summary>The meter and instruments that describe compiling and rendering pages.</summary>
/// <remarks>
/// Recording is nearly free until something listens, so the instruments are always on. Instrument
/// names and tag values are a contract for dashboards: change them deliberately.
/// </remarks>
internal sealed class PageMetrics : IDisposable {

    /// <summary>The meter every instrument belongs to.</summary>
    public const string MeterName = "uk.osric.asp";

    /// <summary>Buckets for the duration instruments, in seconds.</summary>
    /// <remarks>
    /// One scale for every duration: they are all page-scale work. The top boundary is past the
    /// render budget on purpose, so a render recorded at its deadline lands in a real bucket instead
    /// of `+Inf` — a `504` is exactly the sample the status page is looking for.
    /// </remarks>
    public static readonly double[] SecondsBuckets =
        [0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    /// <summary>Buckets for <c>page.image.size</c>, in bytes.</summary>
    /// <remarks>Emitting an assembly costs tens of kilobytes, so the scale starts above a kilobyte.</remarks>
    public static readonly double[] ImageBuckets =
        [1024, 4096, 16384, 32768, 65536, 131072, 262144, 524288, 1048576];

    /// <summary>The page was rendered.</summary>
    public const string RenderOk = "ok";

    /// <summary>The page did not compile, so its diagnostics were served instead.</summary>
    public const string RenderCompileError = "compile-error";

    /// <summary>An exception escaped the handler.</summary>
    public const string RenderError = "error";

    /// <summary>No page is stored under the requested key.</summary>
    public const string RenderNotFound = "not-found";

    /// <summary>The key a request that named no stored page is recorded under.</summary>
    /// <remarks>
    /// A miss is unauthenticated and its path is whatever the caller typed, so tagging it verbatim
    /// lets a scan mint one time series per URL. The real path is in the access log.
    /// </remarks>
    public const string NotFoundPath = "(not-found)";

    /// <summary>The page outran its execution budget and was abandoned.</summary>
    public const string RenderTimeout = "timeout";

    /// <summary>The stored image was used.</summary>
    public const string CacheHit = "hit";

    /// <summary>The page had to be compiled to be served.</summary>
    public const string CacheMiss = "miss";

    /// <summary>There was no page to look up.</summary>
    public const string CacheNone = "none";

    public const string LookupHit = "hit";
    public const string LookupStale = "stale";
    public const string LookupAbsent = "absent";

    public const string ReasonSave = "save";
    public const string ReasonLazy = "lazy";

    public const string StaleNeverCompiled = "never-compiled";
    public const string StaleContentChanged = "content-changed";
    public const string StaleRuntimeChanged = "runtime-changed";

    private readonly Meter _meter;
    private readonly Histogram<double> _renderDuration;
    private readonly Histogram<double> _renderWait;
    private readonly Histogram<double> _compileDuration;
    private readonly Histogram<double> _loadDuration;
    private readonly Histogram<double> _invokeDuration;
    private readonly Histogram<double> _saveDuration;
    private readonly Counter<long> _renderAbandoned;
    private readonly Counter<long> _cacheLookups;
    private readonly Counter<long> _staleness;
    private readonly Histogram<long> _imageSize;

    public PageMetrics() {
        _meter = new Meter(MeterName, typeof(PageMetrics).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        _renderDuration = _meter.CreateHistogram<double>(
            "page.render.duration", "s", "Time to serve one page request, including a lazy compile.");
        _renderWait = _meter.CreateHistogram<double>(
            "page.render.wait", "s", "Time a render spends waiting for a slot before it starts.");
        _compileDuration = _meter.CreateHistogram<double>(
            "page.compile.duration", "s", "Time Roslyn spends compiling one page.");
        _loadDuration = _meter.CreateHistogram<double>(
            "page.handler.load.duration", "s", "Time to load a stored image into an assembly load context.");
        _invokeDuration = _meter.CreateHistogram<double>(
            "page.handler.invoke.duration", "s", "Time the page's own code spends running.");
        _saveDuration = _meter.CreateHistogram<double>(
            "page.save.duration", "s", "Time to compile and persist an edited page.");
        _renderAbandoned = _meter.CreateCounter<long>(
            "page.render.abandoned", null, "Renders given up on because they outran their budget.");
        _cacheLookups = _meter.CreateCounter<long>(
            "page.cache.lookups", null, "Compiled images the render path could use, could not use, or did not find.");
        _staleness = _meter.CreateCounter<long>(
            "page.cache.staleness", null, "Why a stored compiled image was not reused.");
        _imageSize = _meter.CreateHistogram<long>(
            "page.image.size", "By", "Size of an emitted page assembly.");
    }

    public void RecordRender(PagePath path, string outcome, string cache, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", string.Equals(outcome, RenderNotFound, StringComparison.Ordinal) ? NotFoundPath : path.Path },
            { "outcome", outcome },
            { "cache", cache },
        };

        _renderDuration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>Records time spent queueing for a render slot.</summary>
    /// <param name="duration">How long the request waited.</param>
    /// <remarks>
    /// Queueing is a property of the site rather than of one page, and the wait is measured before the
    /// page is looked up — so this instrument carries no path tag, which is also what keeps a scan
    /// from creating a series per URL.
    /// </remarks>
    public void RecordWait(TimeSpan duration) {
        _renderWait.Record(duration.TotalSeconds);
    }

    /// <summary>Records a render that was given up on; the thread it was using is lost.</summary>
    /// <param name="path">The page that outran its budget.</param>
    public void RecordAbandoned(PagePath path) {
        TagList tags = new() { { "page.path", path.Path } };

        _renderAbandoned.Add(1, tags);
    }

    public void RecordCompile(PagePath path, bool success, string reason, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Path },
            { "outcome", success ? RenderOk : RenderCompileError },
            { "reason", reason },
        };

        _compileDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordLoad(PagePath path, TimeSpan duration) {
        TagList tags = new() { { "page.path", path.Path } };

        _loadDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordInvoke(PagePath path, bool success, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Path },
            { "outcome", success ? RenderOk : RenderError },
        };

        _invokeDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordSave(PagePath path, bool success, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Path },
            { "outcome", success ? RenderOk : RenderCompileError },
        };

        _saveDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordCacheLookup(string result) {
        TagList tags = new() { { "result", result } };

        _cacheLookups.Add(1, tags);
    }

    public void RecordStaleness(PagePath path, string reason) {
        TagList tags = new() {
            { "page.path", path.Path },
            { "reason", reason },
        };

        _staleness.Add(1, tags);
    }

    public void RecordImageSize(PagePath path, int bytes) {
        TagList tags = new() { { "page.path", path.Path } };

        _imageSize.Record(bytes, tags);
    }

    public void Dispose() {
        _meter.Dispose();
    }
}
