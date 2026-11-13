// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Text;

[TestFixture]
public sealed class PageRequestBodyTests : IntegrationTest {

    [Test]
    public async Task ARepeatedQueryValueReachesThePage() {
        await SavePageAsync("/echo", """
            <% foreach (string tag in ctx.Request.QueryValues["tag"]) { ctx.Write(tag + ";"); } %>
            """);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("https://localhost/echo?tag=a&tag=b");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("a;b;"), "every value, in the order sent");
        });
    }

    [Test]
    public async Task ARepeatedFormValueReachesThePage() {
        await SavePageAsync("/echo-form", """
            <% foreach (string tag in ctx.Request.FormValues["tag"]) { ctx.Write(tag + ";"); } %>
            """);
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "https://localhost/echo-form",
            new StringContent("tag=a&tag=b", Encoding.UTF8, "application/x-www-form-urlencoded"));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("a;b;"), "every value, in the order sent");
        });
    }

    [Test]
    public async Task AChunkedFormPastTheCapIsRejected() {
        await SavePageAsync("/big.cs", "<p>ok</p>");
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "https://localhost/big.cs") {
            Content = new StringContent("field=" + new string('a', 2 * 1024 * 1024), Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        // No Content-Length is what steps around the header check, so this is the case under test.
        request.Headers.TransferEncodingChunked = true;

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    [Test]
    public async Task ASizedFormPastTheCapIsRejected() {
        await SavePageAsync("/big.cs", "<p>ok</p>");
        using HttpClient client = CreateClient();

        using HttpResponseMessage response = await client.PostAsync(
            "https://localhost/big.cs",
            new StringContent("field=" + new string('a', 2 * 1024 * 1024), Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }
}
