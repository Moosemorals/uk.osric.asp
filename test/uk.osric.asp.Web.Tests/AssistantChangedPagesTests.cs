// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.Json;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;

/// <summary>
/// The card is the only place the transcript says which pages a turn touched, so what it counts,
/// where it sits and when it is withheld are all behaviour worth pinning.
/// </summary>
[TestFixture]
public sealed class AssistantChangedPagesTests {

    private const string WriteResult = """{"ok":true,"data":{"path":"/blog","revisionHash":"abc","diagnostics":[]},"error":null}""";

    [Test]
    public async Task AFinishedTurnLinksEachChangedPageLiveAndInTheEditorAsync() {
        string html = await RenderAsync(WriteTurn("run-1"));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("class=\"assistant-changed-pages\""));
            Assert.That(html, Does.Contain("Changed pages"));
            Assert.That(html, Does.Contain("href=\"/blog\""), "the live page");
            Assert.That(html, Does.Contain("href=\"/pages?edit=/blog\""), "the editor");
        });
    }

    [Test]
    public async Task ATurnThatWritesOnePageTwiceListsItOnceAsync() {
        string html = await RenderAsync([
            User("run-1"),
            AssistantCall("run-1", "call-1", "write_page"),
            ToolResult("run-1", "call-1", "write_page", WriteResult),
            AssistantCall("run-1", "call-2", "edit_page"),
            ToolResult("run-1", "call-2", "edit_page", WriteResult),
            AssistantContent("run-1", "Both changes are saved."),
        ]);

        Assert.That(Count(html, "href=\"/blog\""), Is.EqualTo(1));
    }

    [Test]
    public async Task TheCardLeadsItsTurnInTheNewestFirstTranscriptAsync() {
        string html = await RenderAsync(WriteTurn("run-1"));

        Assert.That(html.IndexOf("assistant-changed-pages", StringComparison.Ordinal),
            Is.LessThan(html.IndexOf("The page is written.", StringComparison.Ordinal)));
    }

    [Test]
    public async Task EachFinishedTurnGetsItsOwnCardAsync() {
        string html = await RenderAsync([
            .. WriteTurn("run-1"),
            User("run-2"),
            AssistantCall("run-2", "call-2", "write_page", "/notes"),
            ToolResult("run-2", "call-2", "write_page", """{"ok":true,"data":{"path":"/notes"},"error":null}"""),
            AssistantContent("run-2", "The notes page is written."),
        ]);

        Assert.Multiple(() => {
            Assert.That(Count(html, "class=\"assistant-changed-pages\""), Is.EqualTo(2));
            Assert.That(html, Does.Contain("href=\"/notes\""));
        });
    }

    [Test]
    public async Task ARunThatIsStillGoingHasNoCardAsync() {
        string html = await RenderAsync(WriteTurn("run-1"), activeRun: Running("run-1"));

        Assert.That(html, Does.Not.Contain("assistant-changed-pages"));
    }

    [Test]
    public async Task ARefusedWriteIsNotListedAsync() {
        string html = await RenderAsync([
            User("run-1"),
            AssistantCall("run-1", "call-1", "write_page"),
            ToolResult("run-1", "call-1", "write_page", """{"ok":false,"data":null,"error":"Write refused"}"""),
            AssistantContent("run-1", "The write was refused."),
        ]);

        Assert.That(html, Does.Not.Contain("assistant-changed-pages"));
    }

    [Test]
    public async Task AnEditThatChangedNothingIsNotListedAsync() {
        string html = await RenderAsync([
            User("run-1"),
            AssistantCall("run-1", "call-1", "edit_page"),
            ToolResult("run-1", "call-1", "edit_page",
                """{"ok":true,"data":{"path":"/blog","revisionHash":"abc","changed":false},"error":null}"""),
            AssistantContent("run-1", "Nothing changed."),
        ]);

        Assert.That(html, Does.Not.Contain("assistant-changed-pages"));
    }

    [Test]
    public async Task ATurnThatOnlyReadsAPageHasNoCardAsync() {
        string html = await RenderAsync([
            User("run-1"),
            AssistantCall("run-1", "call-1", "read_page"),
            ToolResult("run-1", "call-1", "read_page", """{"ok":true,"data":{"path":"/blog"},"error":null}"""),
            AssistantContent("run-1", "The page says hello."),
        ]);

        Assert.That(html, Does.Not.Contain("assistant-changed-pages"));
    }

    private static Message[] WriteTurn(string runId) => [
        User(runId),
        AssistantCall(runId, "call-1", "write_page"),
        ToolResult(runId, "call-1", "write_page", WriteResult),
        AssistantContent(runId, "The page is written."),
    ];

    private static Message User(string runId) => new() {
        RunId = runId,
        Role = "user",
        Content = "Write a page",
    };

    private static Message AssistantCall(string runId, string callId, string tool, string path = "/blog") => new() {
        RunId = runId,
        Role = "assistant",
        Content = string.Empty,
        ToolCallsJson = JsonSerializer.Serialize(new[] {
            new { id = callId, type = "function", function = new { name = tool, arguments = $"{{\"path\":\"{path}\"}}" } },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };

    private static Message ToolResult(string runId, string callId, string tool, string content) => new() {
        RunId = runId,
        Role = "tool",
        Content = content,
        ToolCallId = callId,
        ToolName = tool,
    };

    private static Message AssistantContent(string runId, string content) => new() {
        RunId = runId,
        Role = "assistant",
        Content = content,
    };

    private static Run Running(string runId) => new() {
        Id = runId,
        ConversationId = 1,
        Status = "running",
        Model = "test",
        ApprovalMode = "AlwaysApprove",
        PromptVersion = "1",
    };

    private static async Task<string> RenderAsync(
        IReadOnlyList<Message> messages,
        Run? activeRun = null) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/assistant/1");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        await AssistantEndpoint.ConversationAsync(
            context,
            1,
            new ShellConversationStore(messages, activeRun: activeRun),
            new TestAntiforgery(),
            new RunRegistry(TestOptions.Assistant, TimeProvider.System),
            TestOptions.Assistant,
            new StubAccountBalanceClient(),
            new MarkdownRenderer());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static int Count(string text, string value) {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0) {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
