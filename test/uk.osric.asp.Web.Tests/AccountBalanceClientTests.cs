// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AccountBalanceClientTests {

    private const string Body = """
        {
            "is_available": true,
            "balance_infos": [
                { "currency": "CNY", "total_balance": "110.00", "granted_balance": "10.00", "topped_up_balance": "100.00" }
            ]
        }
        """;

    [Test]
    public async Task ReadsTheBalanceWithTheConfiguredKeyAsync() {
        HttpRequestMessage? request = null;
        using HttpClient client = Client((message, _) => {
            request = message;
            return Task.FromResult(Response(HttpStatusCode.OK, Body));
        });
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { ApiKey = "private-key" });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);
        AccountBalance read = result.Balance!;

        Assert.Multiple(() => {
            Assert.That(request!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo("https://api.deepseek.com/user/balance"));
            Assert.That(request.Headers.Accept, Does.Contain(new MediaTypeWithQualityHeaderValue("application/json")));
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("moosemorals.com (assistant)"));
            Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "private-key")));
            Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Ok));
            Assert.That(read.IsAvailable, Is.True);
            Assert.That(read.Currencies[0], Is.EqualTo(new CurrencyBalance("CNY", "110.00", "10.00", "100.00")));
        });
    }

    [Test]
    public async Task AConfiguredProviderWithoutAKeySendsNoAuthorizationAsync() {
        HttpRequestMessage? request = null;
        using HttpClient client = Client((message, _) => {
            request = message;
            return Task.FromResult(Response(HttpStatusCode.OK, Body));
        });
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { RequireApiKey = false });

        _ = await balance.GetAsync(CancellationToken.None);

        Assert.That(request!.Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task AProviderWithoutABalanceRouteIsUnsupportedAsync() {
        using HttpClient client = Client((_, _) => Task.FromResult(Response(HttpStatusCode.NotFound, "no such route")));
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { RequireApiKey = false });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Unsupported));
    }

    [Test]
    public async Task AnErrorStatusIsAnErrorAsync() {
        using HttpClient client = Client((_, _) => Task.FromResult(Response(HttpStatusCode.Unauthorized, "private provider text")));
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { ApiKey = "private-key" });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Error));
    }

    [Test]
    public async Task ABodyThatIsNotTheBalanceJsonIsInvalidAsync() {
        using HttpClient client = Client((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "not the balance")));
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { RequireApiKey = false });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Invalid));
    }

    [Test]
    public async Task AnUnreachableProviderIsUnreachableAsync() {
        using HttpClient client = Client((_, _) => throw new HttpRequestException("the socket is closed"));
        AccountBalanceClient balance = new(client, TestOptions.Assistant with { RequireApiKey = false });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Unreachable));
    }

    [Test]
    public async Task AProviderThatDoesNotAnswerBeforeTheDeadlineTimesOutAsync() {
        using HttpClient client = Client(async (_, cancellationToken) => {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Response(HttpStatusCode.OK, Body);
        });
        AccountBalanceClient balance = new(
            client,
            TestOptions.Assistant with { RequireApiKey = false, BalanceTimeoutSeconds = 0.05 });

        AccountBalanceRead result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result.Reason, Is.EqualTo(AccountBalanceReason.Timeout));
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new RecordingHandler(send)) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler {

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
