// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

/// <summary><c>ctx.Stores.*.Objects</c> and the <c>/o/{sha}</c> route through the real host.</summary>
[TestFixture]
public sealed partial class ObjectRouteTests : IntegrationTest {

    private const string PublishPageObject = """
        <% byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello object");
           PageObjectRef reference = await ctx.Stores.Page.Objects.PutAsync(bytes, "text/plain; charset=utf-8");
           ctx.Response.WriteRaw($"<p id=\"sha\">{reference.Sha}</p>"); %>
        """;

    private const string PublishVisitorObject = """
        <% byte[] bytes = System.Text.Encoding.UTF8.GetBytes("private bytes");
           PageObjectRef reference = await ctx.Stores.Visitor.Objects.PutAsync(bytes, "application/octet-stream");
           ctx.Response.WriteRaw($"<p id=\"sha\">{reference.Sha}</p>"); %>
        """;

    private const string ReadPageObject = """
        <% PageObjectRef reference = await ctx.Stores.Page.Objects.PutAsync(
               System.Text.Encoding.UTF8.GetBytes("read me"), "application/octet-stream");
           await using PageObjectStream stream = (await ctx.Stores.Page.Objects.OpenAsync(reference.Sha))!;
           byte[] buffer = new byte[4];
           int read;
           while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ctx.Aborted)) > 0) {
               ctx.Response.WriteRaw(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
           } %>
        """;

    [Test]
    public async Task APageObjectIsServedAsPublicAndImmutableAsync() {
        const string path = "/object-public";
        await SavePageAsync(path, PublishPageObject);
        using HttpClient client = CreateClient();
        string sha = await PublishAsync(client, path);

        HttpResponseMessage response = await client.GetAsync("/o/" + sha);
        using HttpClient stranger = CreateClient();
        HttpResponseMessage publicRead = await stranger.GetAsync("/o/" + sha);

        await Assert.MultipleAsync(async () => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("hello object"));
            Assert.That(publicRead.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a page object is public to every visitor");
            Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(response.Headers.CacheControl?.ToString(), Is.EqualTo("public, max-age=31536000, immutable"));
            Assert.That(response.Headers.ETag?.Tag, Is.EqualTo("\"" + sha + "\""));
            Assert.That(response.Headers.AcceptRanges, Contains.Item("bytes"));
        });
    }

    [Test]
    public async Task AHeadAnswersTheHeadersAndNoBodyAsync() {
        const string path = "/object-head";
        await SavePageAsync(path, PublishPageObject);
        using HttpClient client = CreateClient();
        string sha = await PublishAsync(client, path);

        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/o/" + sha));

        await Assert.MultipleAsync(async () => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(12));
            Assert.That(response.Headers.ETag?.Tag, Is.EqualTo("\"" + sha + "\""));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task ARangeAnswers206WithTheSliceAndItsContentRangeAsync() {
        const string path = "/object-range";
        await SavePageAsync(path, PublishPageObject);
        using HttpClient client = CreateClient();
        string sha = await PublishAsync(client, path);

        using HttpRequestMessage request = new(HttpMethod.Get, "/o/" + sha);
        request.Headers.Range = new RangeHeaderValue(0, 4);
        HttpResponseMessage response = await client.SendAsync(request);

        await Assert.MultipleAsync(async () => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("hello"));
            Assert.That(response.Content.Headers.ContentRange?.ToString(), Is.EqualTo("bytes 0-4/12"));
        });
    }

    [Test]
    public async Task AnIfNoneMatchAnswers304Async() {
        const string path = "/object-etag";
        await SavePageAsync(path, PublishPageObject);
        using HttpClient client = CreateClient();
        string sha = await PublishAsync(client, path);

        using HttpRequestMessage request = new(HttpMethod.Get, "/o/" + sha);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"" + sha + "\""));
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
    }

    [Test]
    public async Task AMissingDigestIs404Async() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/o/" + new string('0', 64));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AVisitorObjectIsPrivateToTheReaderAsync() {
        const string path = "/object-private";
        await SavePageAsync(path, PublishVisitorObject);
        using HttpClient owner = CreateClient();
        using HttpClient stranger = CreateClient();
        string sha = await PublishAsync(owner, path);

        HttpResponseMessage ownerRead = await owner.GetAsync("/o/" + sha);
        HttpResponseMessage strangerRead = await stranger.GetAsync("/o/" + sha);

        await Assert.MultipleAsync(async () => {
            Assert.That(ownerRead.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await ownerRead.Content.ReadAsStringAsync(), Is.EqualTo("private bytes"));
            Assert.That(ownerRead.Headers.CacheControl?.ToString(), Does.Contain("private").And.Contain("no-store"));
            Assert.That(strangerRead.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task APageReadsItsOwnObjectInChunksAsync() {
        const string path = "/object-read";
        await SavePageAsync(path, ReadPageObject);
        using HttpClient client = CreateClient();

        string body = await client.GetStringAsync(path);

        Assert.That(body, Is.EqualTo("read me"));
    }

    private static async Task<string> PublishAsync(HttpClient client, string path) {
        string body = await client.GetStringAsync(path);
        Match match = ShaPattern().Match(body);

        Assert.That(match.Success, Is.True, $"the page did not publish a digest: {body}");

        return match.Groups[1].Value;
    }

    [GeneratedRegex("<p id=\"sha\">([0-9a-f]{64})</p>")]
    private static partial Regex ShaPattern();
}
