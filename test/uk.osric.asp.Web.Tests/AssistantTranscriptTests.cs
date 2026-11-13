// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;

/// <summary>The transcript renders the assistant's Markdown and keeps everything else plain.</summary>
[TestFixture]
public sealed class AssistantTranscriptTests {

    [Test]
    public async Task AssistantContentIsRenderedAsRestrictedMarkdownAsync() {
        string html = await RenderAsync(Assistant(
            "## Saved\n\n**bold** and [page](/blog) and [outside](https://example.com)"));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<div class=\"assistant-content\">"));
            Assert.That(html, Does.Contain("<h3>Saved</h3>"));
            Assert.That(html, Does.Contain("<strong>bold</strong>"));
            Assert.That(html, Does.Contain(
                "<a href=\"/blog\" rel=\"nofollow noopener noreferrer\">page</a>"));
            Assert.That(html, Does.Contain(
                "<a href=\"https://example.com\" rel=\"nofollow noopener noreferrer\">outside</a>"));
        });
    }

    [Test]
    public async Task AssistantContentRendersAPipeTableWithScopedHeadersAsync() {
        string html = await RenderAsync(Assistant(
            "| Name | Value |\n| --- | --- |\n| one | 1 |"));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Name</th>"));
            Assert.That(html, Does.Contain("<td>one</td>"));
            Assert.That(html, Does.Not.Contain("style="));
        });
    }

    [Test]
    public async Task UserContentStaysEscapedAndInAPreBlockAsync() {
        string html = await RenderAsync(User("<b>not markup</b>"));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain(
                "<pre>&lt;b&gt;not markup&lt;/b&gt;</pre>"));
            Assert.That(html, Does.Not.Contain("<b>not markup</b>"));
        });
    }

    [Test]
    public async Task TheNewestMessageIsRenderedFirstAsync() {
        string html = await RenderAsync(
            User("The first prompt", "run-1"),
            Assistant("The first answer", "run-1"),
            User("The second prompt", "run-2"),
            Assistant("The second answer", "run-2"));

        int secondAnswer = html.IndexOf("The second answer", StringComparison.Ordinal);
        int secondPrompt = html.IndexOf("The second prompt", StringComparison.Ordinal);
        int firstAnswer = html.IndexOf("The first answer", StringComparison.Ordinal);
        int firstPrompt = html.IndexOf("The first prompt", StringComparison.Ordinal);
        Assert.Multiple(() => {
            Assert.That(secondAnswer, Is.GreaterThanOrEqualTo(0), "the newest answer is missing");
            Assert.That(secondPrompt, Is.GreaterThan(secondAnswer), "the newest prompt must follow its answer");
            Assert.That(firstAnswer, Is.GreaterThan(secondPrompt), "the older answer must follow the newer turn");
            Assert.That(firstPrompt, Is.GreaterThan(firstAnswer), "the older prompt must follow its answer");
        });
    }

    private static Message Assistant(string content, string runId = "run-1") => new() {
        Id = 2,
        ConversationId = 1,
        RunId = runId,
        Sequence = 2,
        Role = "assistant",
        Content = content,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Message User(string content, string runId = "run-1") => new() {
        Id = 1,
        ConversationId = 1,
        RunId = runId,
        Sequence = 1,
        Role = "user",
        Content = content,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<string> RenderAsync(params Message[] messages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/assistant/1");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        await AssistantEndpoint.ConversationAsync(
            context,
            1,
            new ShellConversationStore(messages),
            new TestAntiforgery(),
            new RunRegistry(new AssistantOptions(), TimeProvider.System),
            new AssistantOptions(),
            new StubAccountBalanceClient(),
            new MarkdownRenderer());

        return await TestHttp.ReadBodyAsync(context);
    }
}
