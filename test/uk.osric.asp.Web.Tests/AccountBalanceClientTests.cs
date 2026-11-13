// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
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
        AccountBalanceClient balance = new(client, new AssistantOptions { ApiKey = "private-key" },
            NullLogger<AccountBalanceClient>.Instance);

        AccountBalance? result = await balance.GetAsync(CancellationToken.None);
        AccountBalance read = result!;

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(request!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo("https://api.deepseek.com/user/balance"));
            Assert.That(request.Headers.Accept, Does.Contain(new MediaTypeWithQualityHeaderValue("application/json")));
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("moosemorals.com (assistant)"));
            Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "private-key")));
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
        AccountBalanceClient balance = new(client, new AssistantOptions { RequireApiKey = false },
            NullLogger<AccountBalanceClient>.Instance);

        _ = await balance.GetAsync(CancellationToken.None);

        Assert.That(request!.Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task AnErrorStatusIsNoBalanceAsync() {
        using HttpClient client = Client((_, _) => Task.FromResult(Response(HttpStatusCode.Unauthorized, "private provider text")));
        AccountBalanceClient balance = new(client, new AssistantOptions { ApiKey = "private-key" },
            NullLogger<AccountBalanceClient>.Instance);

        AccountBalance? result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ABodyThatIsNotTheBalanceJsonIsNoBalanceAsync() {
        using HttpClient client = Client((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "not the balance")));
        AccountBalanceClient balance = new(client, new AssistantOptions { RequireApiKey = false },
            NullLogger<AccountBalanceClient>.Instance);

        AccountBalance? result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AnUnreachableProviderIsNoBalanceAsync() {
        using HttpClient client = Client((_, _) => throw new HttpRequestException("the socket is closed"));
        AccountBalanceClient balance = new(client, new AssistantOptions { RequireApiKey = false },
            NullLogger<AccountBalanceClient>.Instance);

        AccountBalance? result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AProviderThatDoesNotAnswerBeforeTheDeadlineIsNoBalanceAsync() {
        using HttpClient client = Client(async (_, cancellationToken) => {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Response(HttpStatusCode.OK, Body);
        });
        AccountBalanceClient balance = new(
            client,
            new AssistantOptions { RequireApiKey = false, BalanceTimeoutSeconds = 0.05 },
            NullLogger<AccountBalanceClient>.Instance);

        AccountBalance? result = await balance.GetAsync(CancellationToken.None);

        Assert.That(result, Is.Null);
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
