// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What happens when a page commits its response and writes it while it runs.</summary>
/// <remarks>
/// Every test drives <see cref="PageEndpoint.HandleAsync"/>, because the commit, the chain and the
/// truncation are the endpoint's decisions, not the context's alone.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageStreamingTests {

    private const string DefaultLayout = "/_layout";

    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly FakePageFetcher _fetcher = new();
    private readonly FakePageReadStore _readStore = new();
    private readonly FakePageStateStore _state = new();
    private readonly FakePageStore _storage = new();
    private readonly TestAntiforgery _antiforgery = new();
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [Test]
    public async Task AFlushingPageWritesBeforeTheHandlerReturnsAsync() {
        Store("/stream", """
            <% ctx.Write("first"); await ctx.FlushAsync();
               await ctx.Capabilities.GetRequired<IPageStorage>().TryGetValueAsync<string>("gate");
               ctx.Write("second"); %>
            """);
        _state.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        (Task run, DefaultHttpContext context) = Start("/stream");

        await _state.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
        string first = await WaitForBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo("first"),
                "the first flush must reach the response before the page finishes");
            Assert.That(run.IsCompleted, Is.False);
        });

        _state.ReadGate.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("firstsecond"));
    }

    [Test]
    public async Task APageCanRelayAnOutboundStreamToTheClientAsync() {
        GatedHttpFetchStream upstream = new();
        _fetcher.StreamAnswer = _ => upstream;
        Store("/relay", """
            <% ctx.Write("ready"); await ctx.FlushAsync();
               IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchStream upstream = await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/events"));
               string? line;
               while ((line = await upstream.ReadLineAsync()) is not null) {
                   ctx.Write(line);
                   await ctx.FlushAsync();
               } %>
            """);

        (Task run, DefaultHttpContext context) = Start("/relay");

        string first = await WaitForBodyContainingAsync(context, "first");
        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo("readyfirst"));
            Assert.That(run.IsCompleted, Is.False);
            Assert.That(upstream.IsClosed, Is.False);
        });

        upstream.ReleaseSecondLine();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("readyfirstsecond"));
    }

    [Test]
    [NonParallelizable]
    public async Task AnAbandonedRenderClosesItsOutboundStreamAsync() {
        GatedHttpFetchStream upstream = new();
        _fetcher.StreamAnswer = _ => upstream;
        Store("/relay", """
            <% ctx.Write("ready"); await ctx.FlushAsync();
               IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchStream upstream = await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/events"));
               string? line;
               while ((line = await upstream.ReadLineAsync()) is not null) {
                   ctx.Write(line);
                   await ctx.FlushAsync();
               } %>
            """);

        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 30 };
        StreamOptions stream = TestOptions.Stream with { SilenceSeconds = 2, MaxSeconds = 10 };
        (Task run, DefaultHttpContext context) = Start("/relay", options: options, stream: stream);

        string body = await WaitForBodyContainingAsync(context, "first");
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
        string finalBody = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(body, Is.EqualTo("readyfirst"));
            Assert.That(upstream.IsClosed, Is.True);
            Assert.That(finalBody, Is.EqualTo("readyfirst"));
        });
    }

    [Test]
    public async Task TheFirstFlushAppliesTheDefaultResponseAsync() {
        Store("/stream", """<% ctx.Write("hi"); await ctx.FlushAsync(); %>""");

        (DefaultHttpContext context, string body) = await RenderAsync("/stream");

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(body, Is.EqualTo("hi"));
        });
    }

    [Test]
    public async Task TheFirstFlushAppliesWhatThePageSetAsync() {
        Store("/stream", """
            <% ctx.SetStatus(201); ctx.SetContentType("text/plain; charset=utf-8");
               ctx.SetHeader("X-Stream", "1"); ctx.AllowScripts();
               ctx.Write("hi"); await ctx.FlushAsync(); %>
            """);

        (DefaultHttpContext context, string body) = await RenderAsync("/stream");

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(context.Response.Headers["X-Stream"].ToString(), Is.EqualTo("1"));
            Assert.That(context.Response.Headers.ContentSecurityPolicy.ToString(), Does.Contain("script-src 'self'"));
            Assert.That(body, Is.EqualTo("hi"));
        });
    }

    [Test]
    public async Task AFlushingPageIsRefusedWhileItIsWrappedAsync() {
        StoreLayout();
        Store("/stream", """<% ctx.Write("hi"); await ctx.FlushAsync(); %>""");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/stream", layout: DefaultLayout));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("/stream"));
            Assert.That(refused?.Message, Does.Contain("Set IPageLayout.Layout to null"));
        });
    }

    [Test]
    public async Task ANonHtmlPageMayStreamWithALayoutNameUntouchedAsync() {
        StoreLayout();
        Store("/stream.txt", """<% ctx.Write("first"); await ctx.FlushAsync(); ctx.Write("second"); %>""");

        (DefaultHttpContext context, string body) = await RenderAsync("/stream.txt", layout: DefaultLayout);

        Assert.Multiple(() => {
            Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(body, Is.EqualTo("firstsecond"));
        });
    }

    [Test]
    public async Task APageThatClearsItsLayoutStreamsWithoutChromeAsync() {
        StoreLayout();
        Store("/stream", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               ctx.WriteRaw("<!DOCTYPE html><p>hi</p>"); await ctx.FlushAsync(); %>
            """);

        (_, string body) = await RenderAsync("/stream", layout: DefaultLayout);

        Assert.Multiple(() => {
            Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(body, Does.Not.Contain("<title>"));
        });
    }

    [Test]
    public async Task ALayoutCannotFlushAsync() {
        Store(DefaultLayout, """<% await ctx.FlushAsync(); %>""");
        Store("/page", """<% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Page"; %>hi""");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RenderAsync("/page", layout: DefaultLayout));

        Assert.That(refused?.Message, Does.Contain("layout"));
    }

    [Test]
    public async Task APageThatNamesALayoutAfterFlushingIsServedAloneAndWarnedAsync() {
        StoreLayout();
        Store("/stream", """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>();
               layout.Layout = null;
               ctx.Write("body");
               await ctx.FlushAsync();
               layout.Layout = "/_layout"; %>
            """);

        RecordingLoggerFactory logs = new();
        (_, string body) = await RenderAsync("/stream", layout: DefaultLayout, logs: logs);

        Assert.Multiple(() => {
            Assert.That(body, Is.EqualTo("body"));
            Assert.That(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning && entry.Message.Contains("was served alone", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task AnExceptionAfterTheFlushTruncatesAndLogsAsync() {
        Store("/stream", """
            <% ctx.Write("prefix"); await ctx.FlushAsync();
               throw new System.InvalidOperationException("boom"); %>
            """);

        RecordingLoggerFactory logs = new();
        (DefaultHttpContext context, string body) = await RenderAsync("/stream", logs: logs);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Is.EqualTo("prefix"));
            Assert.That(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains("truncated", StringComparison.Ordinal)
                && entry.Exception is InvalidOperationException), Is.True);
        });
    }

    [Test]
    [NonParallelizable]
    public async Task AStreamOutlivesTheRenderBudgetAsync() {
        Store("/stream", """
            <% ctx.Write("prefix"); await ctx.FlushAsync();
               System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600);
               while (System.DateTime.UtcNow < until) { }
               ctx.Write("done"); await ctx.FlushAsync(); %>
            """);
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 0.25 };
        StreamOptions stream = TestOptions.Stream with { SilenceSeconds = 10, MaxSeconds = 30 };

        (DefaultHttpContext context, string body) = await RenderAsync("/stream", options: options, stream: stream);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Is.EqualTo("prefixdone"),
                "the render budget must stop at the commit; the stream has its own clocks");
        });
    }

    [Test]
    [NonParallelizable]
    public async Task AStreamThatGoesQuietIsTruncatedAsync() {
        Store("/stream", """
            <% ctx.Write("prefix"); await ctx.FlushAsync();
               System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600);
               while (System.DateTime.UtcNow < until) { }
               ctx.Write("done"); await ctx.FlushAsync(); %>
            """);
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 5 };
        StreamOptions stream = TestOptions.Stream with { SilenceSeconds = 0.1, MaxSeconds = 30 };
        RecordingLoggerFactory logs = new();

        (DefaultHttpContext context, string body) = await RenderAsync("/stream", options: options, stream: stream, logs: logs);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Is.EqualTo("prefix"));
            Assert.That(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("went quiet", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    [NonParallelizable]
    public async Task AStreamAtItsCeilingIsTruncatedAsync() {
        Store("/stream", """
            <% ctx.Write("prefix"); await ctx.FlushAsync();
               System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600);
               while (System.DateTime.UtcNow < until) { }
               ctx.Write("done"); await ctx.FlushAsync(); %>
            """);
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 5 };
        StreamOptions stream = TestOptions.Stream with { SilenceSeconds = 10, MaxSeconds = 0.1 };
        RecordingLoggerFactory logs = new();

        (DefaultHttpContext context, string body) = await RenderAsync("/stream", options: options, stream: stream, logs: logs);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Is.EqualTo("prefix"));
            Assert.That(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("stream ceiling", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task AFullStreamPoolRefusesTheFirstFlushWith503Async() {
        Store("/stream", """<% ctx.Write("hi"); await ctx.FlushAsync(); %>""");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 5 };
        StreamOptions stream = TestOptions.Stream with { Slots = 1, SilenceSeconds = 10, MaxSeconds = 30 };
        PageBudget budget = new(options, TestOptions.Compile, stream);

        await budget.StreamSlots.WaitAsync();
        try {
            (DefaultHttpContext context, string body) = await RenderAsync("/stream", options: options, stream: stream, budget: budget);

            Assert.Multiple(() => {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
                Assert.That(body, Does.Contain("live-stream limit"));
            });
        } finally {
            budget.StreamSlots.Release();
            budget.Dispose();
        }
    }

    [Test]
    public async Task AnExceptionBeforeTheFlushLeavesTheBufferedBodyUnsentAsync() {
        Store("/stream", """
            <% ctx.Write("buffered"); throw new System.InvalidOperationException("boom"); %>
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync("/stream"));
    }

    [Test]
    public async Task ABufferedPageKeepsItsContentLengthAsync() {
        Store("/page", "hello");

        (DefaultHttpContext context, string body) = await RenderAsync("/page");

        Assert.Multiple(() => {
            Assert.That(context.Response.ContentLength, Is.EqualTo(body.Length));
            Assert.That(body, Is.EqualTo("hello"));
        });
    }

    [Test]
    public async Task AStreamedPageHasNoContentLengthAsync() {
        Store("/stream", """<% await ctx.FlushAsync(); ctx.Write("hello"); %>""");

        (DefaultHttpContext context, string body) = await RenderAsync("/stream");

        Assert.Multiple(() => {
            Assert.That(context.Response.ContentLength, Is.Null);
            Assert.That(body, Is.EqualTo("hello"));
        });
    }

    [Test]
    public async Task AStreamedHeadWritesNoBodyAndNoLengthAsync() {
        Store("/stream", """<% await ctx.FlushAsync(); ctx.Write("hello"); %>""");

        (DefaultHttpContext context, string body) = await RenderAsync("/stream", method: "HEAD");

        Assert.Multiple(() => {
            Assert.That(context.Response.ContentLength, Is.Null);
            Assert.That(body, Is.Empty);
        });
    }

    [Test]
    public async Task TheBodyCapStopsAtTheFirstFlushAsync() {
        Store("/stream", """
            <% ctx.Write("0123456789"); await ctx.FlushAsync();
               ctx.Write("0123456789"); await ctx.FlushAsync(); %>
            """);
        RenderOptions options = TestOptions.Render with { MaxBodyChars = 10 };

        (_, string body) = await RenderAsync("/stream", options: options);

        Assert.That(body, Is.EqualTo("01234567890123456789"));
    }

    private void Store(string path, string template) {
        _storage[path] = new PageTemplate(PagePath.Normalize(path), template);
        _state.LivePages.Add(PagePath.Normalize(path).Key);
    }

    private void StoreLayout() => Store(DefaultLayout, """
        <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><!DOCTYPE html><html><head><title><%= layout.Title %></title></head><body><%== layout.Body %></body></html>
        """);

    private (Task Run, DefaultHttpContext Context) Start(
        string path,
        string method = "GET",
        RenderOptions? options = null,
        string? layout = null,
        RecordingLoggerFactory? logs = null,
        StreamOptions? stream = null,
        PageBudget? budget = null) {
        RenderOptions render = options ?? TestOptions.Render;
        StreamOptions streaming = stream ?? TestOptions.Stream;
        PageBudget shared = budget ?? new PageBudget(render, TestOptions.Compile, streaming);
        PageSaver saver = CreateSaver(shared);
        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), method, path);
        PageEnvironment environment = new(
            new ReservedPaths(ReservedPaths.Default),
            TestOptions.Editor,
            TestOptions.Layout with { Default = layout },
            render,
            TestContentTypes.Shipped,
            TestOptions.Storage,
            TestOptions.Fetch,
            TestOptions.Read,
            _state,
            _readStore,
            TestOptions.Seeds,
            saver,
            _fetcher,
            _metrics,
            _antiforgery,
            new MarkdownRenderer(),
            new DiffRenderer(),
            TestOptions.Session,
            new FakePageSessionStore(),
            TestSessions.Cookie);

        ILoggerFactory factory = logs is null ? NullLoggerFactory.Instance : logs;

        return (PageEndpoint.HandleAsync(context, CreateRenderer(render, shared, logs), factory, environment), context);
    }

    private async Task<(DefaultHttpContext Context, string Body)> RenderAsync(
        string path,
        string method = "GET",
        RenderOptions? options = null,
        string? layout = null,
        RecordingLoggerFactory? logs = null,
        StreamOptions? stream = null,
        PageBudget? budget = null) {
        (Task run, DefaultHttpContext context) = Start(path, method, options, layout, logs, stream, budget);
        await run;

        return (context, await TestHttp.ReadBodyAsync(context));
    }

    private PageSaver CreateSaver(PageBudget budget) {
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageSaver(
            new FakePageRevisionStore(_storage), images, new PageLibraries(new FakePageReadStore()),
            _metrics, NullLogger<PageSaver>.Instance);
    }

    private PageRenderer CreateRenderer(RenderOptions options, PageBudget budget, RecordingLoggerFactory? logs) {
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageRenderer(
            _storage,
            _compiler,
            images,
            new PageLibraries(new FakePageReadStore()),
            _metrics,
            budget,
            new PageHealth(options.AbandonedLimit),
            logs?.CreateLogger<PageRenderer>() ?? NullLogger<PageRenderer>.Instance);
    }

    private static async Task<string> WaitForBodyAsync(DefaultHttpContext context) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (context.Response.Body.Length == 0) {
            await Task.Delay(1, timeout.Token);
        }

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> WaitForBodyContainingAsync(DefaultHttpContext context, string expected) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (true) {
            string body = await TestHttp.ReadBodyAsync(context);
            if (body.Contains(expected, StringComparison.Ordinal)) {
                return body;
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class GatedHttpFetchStream : HttpFetchStream {
        private readonly TaskCompletionSource _secondLine = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _line;

        public bool IsClosed { get; private set; }

        public override int Status => 200;

        public override IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        public override HttpFetchFailure Failure => HttpFetchFailure.None;

        public override bool Truncated => false;

        public void ReleaseSecondLine() => _secondLine.TrySetResult();

        public override Task<string?> ReadLineAsync() {
            if (IsClosed) {
                return Task.FromResult<string?>(null);
            }

            return _line++ switch {
                0 => Task.FromResult<string?>("first"),
                1 => ReadSecondLineAsync(),
                _ => Task.FromResult<string?>(null),
            };
        }

        public override Task CloseAsync() {
            IsClosed = true;
            ReleaseSecondLine();

            return Task.CompletedTask;
        }

        private async Task<string?> ReadSecondLineAsync() {
            await _secondLine.Task;

            return IsClosed ? null : "second";
        }
    }
}
