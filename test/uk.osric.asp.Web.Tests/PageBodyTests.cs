// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageBodyTests {

    [Test]
    public async Task TheFirstFlushStartsTheResponseOnceAndWritesTheBuffer() {
        PageBody body = CreateBody(out FakePageBodySink sink);

        body.Write("first");
        await body.FlushAsync(PageResponse.Default);

        Assert.Multiple(() => {
            Assert.That(body.IsCommitted, Is.True);
            Assert.That(sink.Started, Has.Count.EqualTo(1));
            Assert.That(sink.Started[0].Status, Is.EqualTo(200));
            Assert.That(sink.Chunks, Is.EqualTo(["first"]));
        });
    }

    [Test]
    public async Task TheSecondFlushWritesOnlyWhatCameAfter() {
        PageBody body = CreateBody(out FakePageBodySink sink);

        body.Write("one");
        await body.FlushAsync(PageResponse.Default);
        body.Write("two");
        await body.FlushAsync(PageResponse.Default);

        Assert.Multiple(() => {
            Assert.That(sink.Started, Has.Count.EqualTo(1), "the response is committed once");
            Assert.That(sink.Chunks, Is.EqualTo(["one", "two"]));
        });
    }

    [Test]
    public async Task TheTailAfterTheLastFlushIsSentAtCompletion() {
        PageBody body = CreateBody(out FakePageBodySink sink);

        body.Write("one");
        await body.FlushAsync(PageResponse.Default);
        body.Write("two");
        await body.CompleteAsync();

        Assert.Multiple(() => {
            Assert.That(sink.Started, Has.Count.EqualTo(1));
            Assert.That(sink.Chunks, Is.EqualTo(["one", "two"]));
        });
    }

    [Test]
    public async Task AnEmptyFirstFlushStartsTheResponseWithoutWritingABody() {
        PageBody body = CreateBody(out FakePageBodySink sink);

        await body.FlushAsync(PageResponse.Default);

        Assert.Multiple(() => {
            Assert.That(body.IsCommitted, Is.True);
            Assert.That(sink.Started, Has.Count.EqualTo(1));
            Assert.That(sink.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task AFlushAfterAbandonWritesNothing() {
        PageBody body = CreateBody(out FakePageBodySink sink);

        body.Write("buffered");
        body.Abandon();
        await body.FlushAsync(PageResponse.Default);

        Assert.Multiple(() => {
            Assert.That(body.IsCommitted, Is.False);
            Assert.That(sink.Started, Is.Empty);
            Assert.That(sink.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task TheBodyLimitStopsAtTheFirstFlush() {
        PageBody body = CreateBody(out _, maxBodyChars: 10);

        body.Write("0123456789");
        await body.FlushAsync(PageResponse.Default);
        body.Write("0123456789");
        await body.FlushAsync(PageResponse.Default);
        body.Write("0123456789");

        Assert.That(body.IsCommitted, Is.True);
    }

    [Test]
    public void TheBodyLimitStillRefusesAnOversizedBufferBeforeTheFlush() {
        PageBody body = CreateBody(out _, maxBodyChars: 10);

        Assert.That(() => body.Write("01234567890"), Throws.InvalidOperationException.With.Message.Contains("10"));
        Assert.That(body.IsCommitted, Is.False);
    }

    private static PageBody CreateBody(out FakePageBodySink sink, int maxBodyChars = 4 * 1024 * 1024) {
        sink = new FakePageBodySink();

        return new PageBody(
            new StringWriter(),
            PageHarness.Key("/page"),
            new BodyBudget(maxBodyChars),
            new PageCommitState(),
            sink,
            CancellationToken.None);
    }
}
