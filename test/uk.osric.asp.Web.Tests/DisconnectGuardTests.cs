// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using uk.osric.asp.Web.Http;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DisconnectGuardTests {

    private ServiceProvider? _provider;

    [TearDown]
    public void DisposeProvider() {
        _provider?.Dispose();
    }

    [Test]
    public async Task AClientDisconnectIsLoggedAtDebugAndNotRethrownAsync() {
        RecordingLoggerFactory logs = new();
        DefaultHttpContext context = Context(logs);
        context.RequestAborted = new CancellationToken(canceled: true);

        await Pipeline(context, () => throw new NotSupportedException("the client is gone"));

        Assert.Multiple(() => {
            Assert.That(logs.Entries.Any(entry =>
                entry.Level == LogLevel.Debug && entry.Message.Contains("client left", StringComparison.Ordinal)), Is.True);
            Assert.That(logs.Entries.Any(entry => entry.Level == LogLevel.Error), Is.False);
        });
    }

    [Test]
    public async Task AWrappedWriteFailureIsStillADisconnectAsync() {
        RecordingLoggerFactory logs = new();
        DefaultHttpContext context = Context(logs);

        await Pipeline(context, () => throw new OperationCanceledException(
            "the page stopped", new NotSupportedException("the body is gone")));

        Assert.That(logs.Entries.Any(entry => entry.Level == LogLevel.Debug), Is.True);
    }

    [Test]
    public async Task ALiveExceptionStillReachesTheHandlerAsync() {
        RecordingLoggerFactory logs = new();
        DefaultHttpContext context = Context(logs);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(context, () => throw new InvalidOperationException("boom")));

        Assert.Multiple(() => {
            Assert.That(thrown?.Message, Is.EqualTo("boom"));
            Assert.That(logs.Entries, Is.Empty);
        });
    }

    private DefaultHttpContext Context(RecordingLoggerFactory logs) {
        ServiceCollection services = new();
        _ = services.AddSingleton<ILoggerFactory>(logs);
        _provider = services.BuildServiceProvider();
        DefaultHttpContext context = new() {
            RequestServices = _provider,
        };
        context.Request.Path = "/gone";
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());

        return context;
    }

    private static async Task Pipeline(DefaultHttpContext context, Action next) {
        ApplicationBuilder app = new(context.RequestServices);
        _ = app.UseDisconnectGuard();
        app.Run(_ => {
            next();
            return Task.CompletedTask;
        });

        await app.Build()(context);
    }

    private sealed class StartedResponse : IHttpResponseFeature {

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
