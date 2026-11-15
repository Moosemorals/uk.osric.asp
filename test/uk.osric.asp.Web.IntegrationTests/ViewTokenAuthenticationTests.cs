// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using uk.osric.asp.Web.Authentication;

/// <summary>A run's token, presented to the real host: what it may see, and what it may not do.</summary>
[TestFixture]
public sealed class ViewTokenAuthenticationTests : IntegrationTest {

    [Test]
    public async Task ATokenViewsTheEditorInsteadOfBeingSentToSignInAsync() {
        using HttpClient client = CreateClient();
        ViewToken token = await MintViewTokenAsync();

        HttpResponseMessage response = await GetAsync(client, "/pages?edit=/hello", token);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the page's own guard sees a signed-in owner");
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(body, Does.Contain("textarea"), "the editor itself, not the sign-in form");
        });
    }

    [Test]
    public async Task ATokenViewsTheAssistantPagesAsync() {
        using HttpClient client = CreateClient();
        ViewToken token = await MintViewTokenAsync();

        HttpResponseMessage conversations = await GetAsync(client, "/assistant", token);
        HttpResponseMessage memory = await GetAsync(client, "/assistant/memory", token);

        Assert.Multiple(() => {
            Assert.That(conversations.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(memory.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task ATokenMayNotPostAsync() {
        using HttpClient client = CreateClient();
        ViewToken token = await MintViewTokenAsync();
        HttpRequestMessage request = new(HttpMethod.Post, "/pages?edit=/posted") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["template"] = "hijacked" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(ViewToken.HeaderScheme, token.Secret);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(response.Content.Headers.Allow.ToString(), Is.EqualTo("GET, HEAD"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        });
        Assert.That(await ReadPageAsync("/posted"), Is.Null, "a view writes nothing, however it is asked");
    }

    [Test]
    public async Task AnUnknownTokenIsRefusedOnAPublicPageRatherThanShowingTheAnonymousViewAsync() {
        using HttpClient client = CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/hello");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            ViewToken.HeaderScheme, Convert.ToBase64String(new byte[32]));

        HttpResponseMessage response = await client.SendAsync(request);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain(ViewToken.HeaderScheme));
            Assert.That(body, Does.Contain("auth.required"), "the refusal, not a page that ran as a stranger");
        });
    }

    [Test]
    public async Task ATokenWhoseRunHasEndedIsRefusedAsync() {
        using HttpClient client = CreateClient();
        ViewToken token = await MintViewTokenAsync(runId: "ended-run");
        RevokeViewToken(token);

        HttpResponseMessage response = await GetAsync(client, "/pages", token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AnAnonymousRequestIsUnchangedAsync() {
        using HttpClient client = CreateClient();

        HttpResponseMessage editor = await client.GetAsync("/pages");

        Assert.Multiple(() => {
            Assert.That(editor.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(editor.Headers.Location?.ToString(), Does.StartWith("/login?returnUrl="));
        });
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, ViewToken token) {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(ViewToken.HeaderScheme, token.Secret);

        return client.SendAsync(request);
    }
}
