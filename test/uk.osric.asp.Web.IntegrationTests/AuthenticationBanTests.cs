// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using uk.osric.asp.Web.Authentication;

/// <summary>A refusal leaves the one line the host's ban matches, with the address in it.</summary>
/// <remarks>
/// The ban is a deploy-time filter reading a runtime line, so the two halves are proven together
/// here: the host under test writes the line, and the filter file `make deploy` installs matches it.
/// </remarks>
[TestFixture]
public sealed class AuthenticationBanTests : IntegrationTest {

    // A distinct address per test: the host is shared by the fixture, and the search below is by
    // address so one test's line cannot answer another's question.
    private const string SignInAddress = "203.0.113.7";
    private const string TokenAddress = "198.51.100.9";
    private const string FormAddress = "192.0.2.44";

    [Test]
    public async Task ARequestSentToSignInIsLoggedWithItsAddressAsync() {
        using HttpClient client = Client(SignInAddress);

        HttpResponseMessage response = await client.GetAsync("/pages?edit=/hello");

        string line = Refusal(SignInAddress).Message;
        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "the editor's own guard sent them to sign in");
            Assert.That(line, Does.StartWith($"Authentication failed: {SignInAddress} GET /pages "),
                "the address follows the words the filter matches");
        });
    }

    [Test]
    public async Task ARefusedViewTokenIsLoggedWithItsAddressAsync() {
        using HttpClient client = Client(TokenAddress);
        using HttpRequestMessage request = new(HttpMethod.Get, "/hello");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            ViewToken.HeaderScheme, Convert.ToBase64String(new byte[32]));

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Refusal(TokenAddress).Message, Does.StartWith($"Authentication failed: {TokenAddress} GET /hello "));
        });
    }

    [Test]
    public async Task ARefusedSignInIsLoggedWithItsAddressAndAccountAsync() {
        using HttpClient client = Client(FormAddress);
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        using HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", UserName),
            ("password", "the wrong password"),
            ("__RequestVerificationToken", token)));

        string line = Refusal(FormAddress).Message;
        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the sign-in answers with its form, so only this line reports it");
            Assert.That(line, Does.StartWith($"Authentication failed: {FormAddress} POST /login "));
            Assert.That(line, Does.Contain(UserName), "\"who tried to get in\" is the question the line answers");
        });
    }

    [Test]
    public async Task TheFilterTheHostInstallsMatchesTheRefusalLineAsync() {
        using HttpClient client = Client(SignInAddress);
        await client.GetAsync("/pages?edit=/hello");

        // The journal holds one JSON object per entry, so the line is rebuilt here with the fields the
        // filter reads in the order the console formatter writes them.
        LogCapture.Entry entry = Refusal(SignInAddress);
        string journalLine = JsonSerializer.Serialize(new {
            LogLevel = entry.Level.ToString(),
            entry.Category,
            entry.Message,
        });

        Assert.That(BanPattern().IsMatch(journalLine), Is.True,
            "deploy/fail2ban/filter.d/uk.osric.asp.conf no longer matches what the app logs");
    }

    /// <summary>A client that arrives through the proxy, as Caddy's forwarded header describes it.</summary>
    private HttpClient Client(string address) {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);

        return client;
    }

    private LogCapture.Entry Refusal(string address) {
        const string prefix = "Authentication failed: ";
        LogCapture.Entry? entry = Logs.Entries.LastOrDefault(entry =>
            entry.Message.StartsWith(prefix + address, StringComparison.Ordinal));
        Assert.That(entry, Is.Not.Null, $"nothing was logged for {address}");

        return entry!;
    }

    // fail2ban expands <HOST> into a long alternation of every address shape; an IPv4 address is the
    // whole of what these tests write, and the rest of the pattern is what is under test.
    private static Regex BanPattern() {
        string filter = File.ReadAllText(Path.Combine(RepoRoot,
            "deploy", "fail2ban", "filter.d", "uk.osric.asp.conf"));
        string failregex = filter.Split('\n')
            .Single(line => line.StartsWith("failregex", StringComparison.Ordinal));
        string pattern = failregex[(failregex.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim();

        return new Regex(pattern.Replace("<HOST>", @"(?:\d{1,3}\.){3}\d{1,3}", StringComparison.Ordinal));
    }

    private static string RepoRoot => typeof(AspApplication).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RepoRoot")
        .Value!;
}
