// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;

/// <summary>The live transcript's content route is authorized and renders the restricted profile.</summary>
[TestFixture]
public sealed class AssistantMessageContentTests {

    [Test]
    public async Task TheOwnerGetsTheRenderedAssistantMessageAsync() {
        DefaultHttpContext context = OwnerContext();
        Message message = Assistant("**Saved**\n\n| Name | Value |\n| --- | --- |\n| one | 1 |");

        await AssistantEndpoint.MessageContentAsync(
            context, 1, message.Id, new ShellConversationStore([message]), new MarkdownRenderer());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(body, Does.Contain("<strong>Saved</strong>"));
            Assert.That(body, Does.Contain("<th scope=\"col\">Name</th>"));
        });
    }

    [Test]
    public async Task HostileMarkdownIsShownAsTextAsync() {
        DefaultHttpContext context = OwnerContext();
        Message message = Assistant(
            "<script>alert(1)</script>\n\n![x](https://example.com/x.png)\n\n[x](javascript:alert(1))");

        await AssistantEndpoint.MessageContentAsync(
            context, 1, message.Id, new ShellConversationStore([message]), new MarkdownRenderer());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.Multiple(() => {
            Assert.That(body, Does.Not.Contain("<script>"));
            Assert.That(body, Does.Not.Contain("<img"));
            Assert.That(body, Does.Not.Contain("javascript:"));
            Assert.That(body, Does.Contain("&lt;script&gt;"));
            Assert.That(body, Does.Contain("x"));
        });
    }

    [Test]
    public async Task AUserMessageIsNotServedAsync() {
        DefaultHttpContext context = OwnerContext();
        Message message = new() {
            Id = 1,
            ConversationId = 1,
            RunId = "run-1",
            Sequence = 1,
            Role = "user",
            Content = "Hello",
        };

        await AssistantEndpoint.MessageContentAsync(
            context, 1, message.Id, new ShellConversationStore([message]), new MarkdownRenderer());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task AnotherOwnersMessageIsNotServedAsync() {
        DefaultHttpContext context = OwnerContext();
        Message message = Assistant("**Saved**");

        await AssistantEndpoint.MessageContentAsync(
            context, 2, message.Id, new ShellConversationStore([message]), new MarkdownRenderer());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task AMissingMessageIsNotServedAsync() {
        DefaultHttpContext context = OwnerContext();

        await AssistantEndpoint.MessageContentAsync(
            context, 1, 99, new ShellConversationStore([]), new MarkdownRenderer());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    private static DefaultHttpContext OwnerContext() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/assistant/1/messages/2/content");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        return context;
    }

    private static Message Assistant(string content) => new() {
        Id = 2,
        ConversationId = 1,
        RunId = "run-1",
        Sequence = 2,
        Role = "assistant",
        Content = content,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
