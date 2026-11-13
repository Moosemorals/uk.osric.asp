// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class AssistantBalanceTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(assistantEnabled: true);

    [Test]
    public async Task TheConversationShowsTheProvidersCurrentBalanceAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        using HttpResponseMessage response = await client.GetAsync(conversation);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<legend>Account balance</legend>"));
            Assert.That(body, Does.Contain("<dt>CNY</dt>"));
            Assert.That(body, Does.Contain("110.00"));
            Assert.That(body, Does.Contain("10.00 granted, 100.00 topped up"));
            Assert.That(body, Does.Contain("is read when this page loads"));
        });
    }
}

[TestFixture]
public sealed class AssistantBalanceUnavailableTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(
        assistantEnabled: true,
        accountBalanceClient: new StubAccountBalanceClient { Balance = null });

    [Test]
    public async Task AProviderThatDoesNotReportABalanceStillRendersTheConversationAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        using HttpResponseMessage response = await client.GetAsync(conversation);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<legend>Account balance</legend>"));
            Assert.That(body, Does.Contain("The provider did not report a balance."));
            Assert.That(body, Does.Contain("id=\"assistant-message\""));
        });
    }
}

[TestFixture]
public sealed class AssistantBalanceNotAvailableTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(
        assistantEnabled: true,
        accountBalanceClient: new StubAccountBalanceClient {
            Balance = new(false, [new CurrencyBalance("CNY", "0.00", "0.00", "0.00")]),
        });

    [Test]
    public async Task AnUnavailableBalanceShowsTheFigureAndSaysCallsWillNotWorkAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        using HttpResponseMessage response = await client.GetAsync(conversation);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("<dt>CNY</dt>"));
            Assert.That(body, Does.Contain("The provider says this balance is not enough for API calls."));
        });
    }
}
