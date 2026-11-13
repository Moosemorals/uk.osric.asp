// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Metrics;

/// <summary>The meter and instruments that describe compiling and rendering pages.</summary>
/// <remarks>
/// Recording is nearly free until something listens, so the instruments are always on. Instrument
/// names and tag values are a contract for dashboards: change them deliberately.
/// </remarks>
internal sealed class PageMetrics : IDisposable {

    /// <summary>The meter every instrument belongs to.</summary>
    public const string MeterName = "uk.osric.asp.Pages";

    /// <summary>Every instrument this meter registers, in the order the page lists them.</summary>
    /// <remarks>
    /// The catalogue maps each of these to exactly one panel, and <c>PageMetricsTests</c> listens to the
    /// meter to prove this list and the registrations below agree. A name renamed in one place and not
    /// the other then fails a test rather than emptying a panel.
    /// </remarks>
    public static IReadOnlyList<MetricInstrument> Instruments { get; } = [
        new("page.render.duration", InstrumentKind.Histogram, "s"),
        new("page.render.wait", InstrumentKind.Histogram, "s"),
        new("page.compile.duration", InstrumentKind.Histogram, "s"),
        new("page.load.duration", InstrumentKind.Histogram, "s"),
        new("page.invoke.duration", InstrumentKind.Histogram, "s"),
        new("page.save.duration", InstrumentKind.Histogram, "s"),
        new("page.fetch.duration", InstrumentKind.Histogram, "s"),
        new("page.import.duration", InstrumentKind.Histogram, "s"),
        new("page.image.size", InstrumentKind.Histogram, "By"),
        new("page.render.abandoned", InstrumentKind.Counter, "1"),
        new("page.cache.lookups", InstrumentKind.Counter, "1"),
        new("page.cache.staleness", InstrumentKind.Counter, "1"),
        new("page.asset.requests", InstrumentKind.Counter, "1"),
        new("page.layout.fallback", InstrumentKind.Counter, "1"),
        new("page.storage.payload.size", InstrumentKind.Gauge, "By"),
        new("page.storage.database.size", InstrumentKind.Gauge, "By"),
        new("page.storage.volume.capacity", InstrumentKind.Gauge, "By"),
    ];

    /// <summary>Buckets for the duration instruments, in seconds.</summary>
    /// <remarks>
    /// One scale for every page duration: they are all page-scale work. The top boundary is past the
    /// longest deadline these instruments run under — the compile deadline of five seconds, which a
    /// save keeps too (DEC-43, DEC-191) — so a sample recorded at its deadline lands in a real bucket
    /// instead of `+Inf`, and a `504` is exactly the sample the status page is looking for (DEC-214).
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

    /// <summary>An asset was served from source.</summary>
    public const string AssetServed = "served";

    /// <summary>A versioned asset answered <c>304 Not Modified</c>.</summary>
    public const string AssetNotModified = "not-modified";

    /// <summary>A versioned URL named no retained revision, or one stored under another path.</summary>
    public const string AssetUnknown = "unknown";

    /// <summary>A versioned URL named a revision that is not a page nothing wraps.</summary>
    public const string AssetNonAsset = "non-asset";

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

    /// <summary>The page was compiled as part of an import batch.</summary>
    public const string ReasonImport = "import";

    /// <summary>Every selected page of the batch was stored.</summary>
    public const string ImportCommitted = "committed";

    /// <summary>The batch was refused or its write failed; nothing was stored.</summary>
    public const string ImportFailed = "failed";

    /// <summary>The batch stopped before it wrote anything.</summary>
    public const string ImportAborted = "aborted";

    /// <summary>The fetch completed with a 2xx.</summary>
    public const string FetchOk = "ok";

    /// <summary>The remote server answered 4xx or 5xx.</summary>
    public const string FetchHttpError = "http-error";

    /// <summary>The per-fetch timeout or the render's deadline fired.</summary>
    public const string FetchTimeout = "timeout";

    /// <summary>DNS, connect, TLS or protocol failure.</summary>
    public const string FetchNetwork = "network";

    /// <summary>The URL, method, body or a header was not acceptable.</summary>
    public const string FetchInvalid = "invalid";

    /// <summary>The render had already made as many requests as it may.</summary>
    public const string FetchLimit = "limit";

    /// <summary>The redirect limit was reached.</summary>
    public const string FetchRedirectLimit = "redirect-limit";

    /// <summary>The visitor left, or the render was abandoned.</summary>
    public const string FetchAborted = "aborted";

    public const string StaleNeverCompiled = "never-compiled";
    public const string StaleTemplateChanged = "template-changed";
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
    private readonly Counter<long> _assetRequests;
    private readonly Counter<long> _layoutFallbacks;
    private readonly Histogram<long> _imageSize;
    private readonly Histogram<double> _fetchDuration;
    private readonly Histogram<double> _importDuration;
    private StorageSizes? _storage;

    public PageMetrics() {
        _meter = new Meter(MeterName, typeof(PageMetrics).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        _renderDuration = _meter.CreateHistogram<double>(
            "page.render.duration", "s", "Time to serve one page request, including a lazy compile.");
        _renderWait = _meter.CreateHistogram<double>(
            "page.render.wait", "s", "Time a render spends waiting for a slot before it starts.");
        _compileDuration = _meter.CreateHistogram<double>(
            "page.compile.duration", "s", "Time Roslyn spends compiling one page.");
        _loadDuration = _meter.CreateHistogram<double>(
            "page.load.duration", "s", "Time to load a stored image into an assembly load context.");
        _invokeDuration = _meter.CreateHistogram<double>(
            "page.invoke.duration", "s", "Time the page's own code spends running.");
        _saveDuration = _meter.CreateHistogram<double>(
            "page.save.duration", "s", "Time to compile and persist an edited page.");
        _renderAbandoned = _meter.CreateCounter<long>(
            "page.render.abandoned", null, "Renders given up on because they outran their budget.");
        _cacheLookups = _meter.CreateCounter<long>(
            "page.cache.lookups", null, "Compiled images the render path could use, could not use, or did not find.");
        _staleness = _meter.CreateCounter<long>(
            "page.cache.staleness", null, "Why a stored compiled image was not reused.");
        _assetRequests = _meter.CreateCounter<long>(
            "page.asset.requests", null, "Requests for an asset, by whether they were served, unchanged or refused.");
        _layoutFallbacks = _meter.CreateCounter<long>(
            "page.layout.fallback", null, "Pages served without the default layout because it could not be served.");
        _imageSize = _meter.CreateHistogram<long>(
            "page.image.size", "By", "Size of an emitted page assembly.");
        _fetchDuration = _meter.CreateHistogram<double>(
            "page.fetch.duration", "s", "Time one outbound fetch took, including its redirects.");
        _importDuration = _meter.CreateHistogram<double>(
            "page.import.duration", "s", "Time to compile and commit one imported archive.");
        _meter.CreateObservableGauge("page.storage.payload.size",
            () => ObserveStorage(value => value.Payload), "By",
            "Logical PageRevisions TEXT bytes and image/symbol blob bytes, plus PageState key and value bytes; excludes indexes and SQLite overhead.");
        _meter.CreateObservableGauge("page.storage.database.size",
            () => ObserveStorage(value => value.Database), "By",
            "Whole SQLite database page_count times page_size, including indexes and freelist; excludes separate WAL.");
        _meter.CreateObservableGauge("page.storage.volume.capacity",
            () => ObserveStorage(value => value.Capacity), "By",
            "Total capacity of the filesystem volume containing the SQLite database.");
    }

    public void RecordStorage(long payload, long database, long? capacity) {
        Volatile.Write(ref _storage, new StorageSizes(payload, database, capacity));
    }

    private IEnumerable<Measurement<long>> ObserveStorage(Func<StorageSizes, long?> select) {
        StorageSizes? snapshot = Volatile.Read(ref _storage);
        if (snapshot is not null && select(snapshot) is long value) {
            yield return new Measurement<long>(value);
        }
    }

    public void RecordRender(PagePath path, string outcome, string cache, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", string.Equals(outcome, RenderNotFound, StringComparison.Ordinal) ? NotFoundPath : path.Key },
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

    /// <summary>Records a page served unwrapped because the default layout could not be served.</summary>
    /// <param name="path">The page that was served instead.</param>
    /// <remarks>
    /// A fallback is a degradation rather than a fault, so it is counted rather than raised: one
    /// deleted page must not take the site down, and this is how an operator notices it anyway.
    /// </remarks>
    public void RecordLayoutFallback(PagePath path) {
        TagList tags = new() { { "page.path", path.Key } };

        _layoutFallbacks.Add(1, tags);
    }

    /// <summary>Records a render that was given up on; it may still be running.</summary>
    /// <param name="path">The page that outran its budget.</param>
    public void RecordAbandoned(PagePath path) {
        TagList tags = new() { { "page.path", path.Key } };

        _renderAbandoned.Add(1, tags);
    }

    public void RecordCompile(PagePath path, bool success, string reason, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Key },
            { "outcome", success ? RenderOk : RenderCompileError },
            { "reason", reason },
        };

        _compileDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordLoad(PagePath path, TimeSpan duration) {
        TagList tags = new() { { "page.path", path.Key } };

        _loadDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordInvoke(PagePath path, bool success, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Key },
            { "outcome", success ? RenderOk : RenderError },
        };

        _invokeDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordSave(PagePath path, bool success, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Key },
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
            { "page.path", path.Key },
            { "reason", reason },
        };

        _staleness.Add(1, tags);
    }

    /// <summary>Records one request for an asset.</summary>
    /// <param name="outcome">One of <see cref="AssetServed"/>, <see cref="AssetNotModified"/>,
    /// <see cref="AssetUnknown"/> or <see cref="AssetNonAsset"/>.</param>
    /// <remarks>
    /// No path label: the URL is public, so tagging it would let a scan mint a series per address.
    /// The access log names the path.
    /// </remarks>
    public void RecordAsset(string outcome) {
        TagList tags = new() { { "outcome", outcome } };

        _assetRequests.Add(1, tags);
    }

    /// <summary>Records one outbound fetch.</summary>
    /// <param name="path">The page that made it.</param>
    /// <param name="outcome">One of the <c>Fetch*</c> values.</param>
    /// <param name="duration">How long it took, including any redirect hops.</param>
    public void RecordFetch(PagePath path, string outcome, TimeSpan duration) {
        TagList tags = new() {
            { "page.path", path.Key },
            { "outcome", outcome },
        };

        _fetchDuration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>Records one import batch.</summary>
    /// <param name="outcome">One of <see cref="ImportCommitted"/>, <see cref="ImportFailed"/> or <see cref="ImportAborted"/>.</param>
    /// <param name="duration">How long compiling and committing the batch took.</param>
    /// <remarks>
    /// The batch is one operation, so this carries no per-page timing: it is the archive's work as
    /// a whole that an operator watches.
    /// </remarks>
    public void RecordImport(string outcome, TimeSpan duration) {
        TagList tags = new() { { "outcome", outcome } };

        _importDuration.Record(duration.TotalSeconds, tags);
    }

    public void RecordImageSize(PagePath path, int bytes) {
        TagList tags = new() { { "page.path", path.Key } };

        _imageSize.Record(bytes, tags);
    }

    public void Dispose() {
        _meter.Dispose();
    }

    private sealed record StorageSizes(long Payload, long Database, long? Capacity);
}
