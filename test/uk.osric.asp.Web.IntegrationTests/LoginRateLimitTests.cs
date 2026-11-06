// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;
using System.Net;
using System.Text.RegularExpressions;

[TestFixture]
public sealed class LoginRateLimitTests {

    [Test]
    public async Task RepeatedFailedLoginsAreRejected() {
        using AspApplication application = new(loginPermitLimit: 3);
        using HttpClient client = application.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });

        string token = await GetTokenAsync(client);

        for (int attempt = 0; attempt < 3; attempt++) {
            HttpResponseMessage response = await PostAsync(client, token);
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests), $"attempt {attempt + 1} was throttled too early");
        }

        HttpResponseMessage rejected = await PostAsync(client, token);
        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(rejected.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task OneClientExhaustingItsBucketDoesNotLockOutAnotherAsync() {
        using AspApplication application = new(loginPermitLimit: 2);
        using HttpClient attacker = Client(application, "203.0.113.7");
        using HttpClient owner = Client(application, "198.51.100.9");

        string attackerToken = await GetTokenAsync(attacker);
        for (int attempt = 0; attempt < 3; attempt++) {
            await PostAsync(attacker, attackerToken);
        }

        HttpResponseMessage throttled = await PostAsync(attacker, attackerToken);
        Assert.That(throttled.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));

        string ownerToken = await GetTokenAsync(owner);
        HttpResponseMessage others = await PostAsync(owner, ownerToken);

        Assert.That(others.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
    }

    private static HttpClient Client(AspApplication application, string address) {
        HttpClient client = application.CreateClient(new WebApplicationFactoryClientOptions {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });

        // Caddy writes this header for every request; the app trusts it from the loopback hop.
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        return client;
    }

    private static async Task<string> GetTokenAsync(HttpClient client) {
        string body = await (await client.GetAsync("/login")).Content.ReadAsStringAsync();
        Match match = Regex.Match(body, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.That(match.Success, Is.True);

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string token) {
        return client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["username"] = "nobody",
            ["password"] = "not the password",
            ["__RequestVerificationToken"] = token,
        }));
    }
}
