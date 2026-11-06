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

    private static async Task<string> CreateConversationAsync(HttpClient client) {
        string token = await GetAntiforgeryTokenAsync(client, "/assistant");
        using HttpResponseMessage created = await client.PostAsync("/assistant", Form(("__RequestVerificationToken", token)));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        return created.Headers.Location!.ToString();
    }
}
