// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

[TestFixture]
public sealed class AssistantOptionsTests : IntegrationTest {

    internal override AspApplication CreateApplication() => new(assistantEnabled: true);

    [Test]
    public async Task SavingOptionsRenamesAndSwitchesApprovalModeWithoutARedirectAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, conversation);

        using HttpResponseMessage saved = await client.PostAsync(conversation + "/options", Form(
            ("__RequestVerificationToken", token),
            ("title", "Planning notes"),
            ("mode", "RequireApproval")));

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("<h1>Planning notes</h1>"));
            Assert.That(body, Does.Contain("value=\"Planning notes\""));
            Assert.That(body, Does.Contain("<option value=\"RequireApproval\" selected>"));
        });
    }

    [Test]
    public async Task AnUnknownRunOnTheEventStreamIsAProblemDocumentAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        using HttpRequestMessage request = new(HttpMethod.Get, conversation + "/events?run=missing");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        // The stream's client speaks JSON, so its refusal is a problem document even though the
        // successful response is an event stream.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(body, Does.Contain("\"code\":\"assistant.run_not_found\""));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task PageSubmitRedirectsBackToTheConversationAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpRequestMessage request = new(HttpMethod.Post, conversation + "/options") {
            Content = Form(("__RequestVerificationToken", token), ("title", "Renamed by a page"), ("mode", "AlwaysApprove")),
        };
        request.Headers.Accept.ParseAdd("text/html");

        using HttpResponseMessage saved = await client.SendAsync(request);

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        Assert.That(saved.Headers.Location?.ToString(), Is.EqualTo(conversation));
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        Assert.That(await reloaded.Content.ReadAsStringAsync(), Does.Contain("<h1>Renamed by a page</h1>"));
    }

    [Test]
    public async Task AnUnknownApprovalModeIsRejectedAndSavesNothingAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string token = await GetAntiforgeryTokenAsync(client, conversation);

        using HttpResponseMessage saved = await client.PostAsync(conversation + "/options", Form(
            ("__RequestVerificationToken", token),
            ("title", "Never applied"),
            ("mode", "AskEveryTime")));

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("Never applied"));
            Assert.That(body, Does.Contain("<option value=\"AlwaysApprove\" selected>"));
        });
    }
}
