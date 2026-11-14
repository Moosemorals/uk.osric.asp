// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.Logging;
using System.Net;

/// <summary>Proves a streamed page through the real host.</summary>
[TestFixture]
public sealed class PageStreamingTests : IntegrationTest {

    private const string Layout = """
        <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><!DOCTYPE html><html><head><title><%= layout.Title %></title></head><body><%== layout.Body %></body></html>
        """;

    internal override AspApplication CreateApplication() => new(pageLayout: "/_layout");

    [SetUp]
    public Task StoreLayoutAsync() => SavePageAsync("/_layout", Layout);

    [Test]
    public async Task AStreamedPageIsServedWithoutTheLayoutAsync() {
        await SavePageAsync("/_layout", Layout);
        await SavePageAsync("/stream", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               ctx.WriteRaw("<!DOCTYPE html><p>hi</p>"); await ctx.FlushAsync(); %>
            """);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/stream");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(body, Does.Not.Contain("<title>"));
        });
    }

    [Test]
    public async Task AStreamedExceptionTruncatesWithoutAnErrorPageAsync() {
        await SavePageAsync("/stream", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               ctx.Write("prefix"); await ctx.FlushAsync();
               throw new System.InvalidOperationException("boom"); %>
            """);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/stream", HttpCompletionOption.ResponseHeadersRead);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        string? body = null;
        try {
            body = await response.Content.ReadAsStringAsync();
        } catch (HttpRequestException) {
            // The connection was aborted at the truncation point, which is the point.
        }

        Assert.Multiple(() => {
            Assert.That(body, Is.Null.Or.EqualTo("prefix"));
            Assert.That(Logs.Entries.Any(entry =>
                entry.Level == LogLevel.Error
                && entry.Scope.GetValueOrDefault("Path") as string == "/stream"
                && entry.Message.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(Logs.Entries.Any(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains("truncated", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task AStreamedHeadHasNoBodyAndNoLengthAsync() {
        await SavePageAsync("/stream", """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               await ctx.FlushAsync(); ctx.Write("hello"); %>
            """);
        using HttpClient client = CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/stream");
        HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // The endpoint sets no length; the test host reports 0 for a HEAD with no body.
            Assert.That(response.Content.Headers.ContentLength, Is.Null.Or.EqualTo(0L));
            Assert.That(body, Is.Empty);
        });
    }
}
