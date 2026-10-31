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

        Assert.That((await PostAsync(client, token)).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
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
