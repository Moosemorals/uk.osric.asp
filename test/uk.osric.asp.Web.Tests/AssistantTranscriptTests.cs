// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using uk.osric.asp.Core.Assistant;
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

    [Test]
    public async Task APendingApprovalIsRenderedFromTheRecordAsync() {
        OwnerInterrupt pending = new() {
            RunId = "run-1",
            Kind = OwnerInterruptKind.Approval,
            Status = OwnerInterruptStatus.Pending,
            ToolCallId = "call-1",
            RequestJson = InterruptPayloads.Write(new ApprovalRequest("write_page", "Writes a page.", """{"path":"/gone"}""")),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string html = await RenderPendingAsync(pending);

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("section class=\"assistant-approvals\""));
            Assert.That(html, Does.Contain("<h3>write_page</h3>"));
            Assert.That(html, Does.Contain("Writes a page."));
            Assert.That(html, Does.Contain("&quot;path&quot;:&quot;/gone&quot;"), "the arguments are escaped");
            Assert.That(html, Does.Contain("action=\"/assistant/1/runs/run-1/interrupt\""));
            Assert.That(html, Does.Contain("name=\"toolCallId\" value=\"call-1\""));
        });
    }

    [Test]
    public async Task APendingContinuationIsRenderedFromTheRecordAsync() {
        OwnerInterrupt pending = new() {
            RunId = "run-1",
            Kind = OwnerInterruptKind.Continuation,
            Status = OwnerInterruptStatus.Pending,
            RequestJson = InterruptPayloads.Write(new ContinuationRequest(
                RunBudget.Steps, 160, 160, RunBudget.Sentence(RunBudget.Steps, 160, 160))),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string html = await RenderPendingAsync(pending);

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("section class=\"assistant-continuation\""));
            Assert.That(html, Does.Contain("Do you want to continue?"));
            Assert.That(html, Does.Contain("used all 160 of its steps"));
            Assert.That(html, Does.Contain("action=\"/assistant/1/runs/run-1/interrupt\""));
        });
    }

    // A card on the page is a fact about the run, so it is shown whatever the conversation's approval
    // mode says now: the mode decides whether the next call is gated, not whether this one is.
    [Test]
    public async Task APendingCardIsRenderedWhateverTheApprovalModeIsAsync() {
        OwnerInterrupt pending = new() {
            RunId = "run-1",
            Kind = OwnerInterruptKind.Approval,
            Status = OwnerInterruptStatus.Pending,
            ToolCallId = "call-1",
            RequestJson = InterruptPayloads.Write(new ApprovalRequest("write_page", "Writes a page.", "{}")),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string html = await RenderPendingAsync(pending, ApprovalMode.AlwaysApprove);

        Assert.That(html, Does.Contain("section class=\"assistant-approvals\""));
    }

    private static async Task<string> RenderPendingAsync(OwnerInterrupt pending, ApprovalMode mode = ApprovalMode.RequireApproval) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/assistant/1");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        await AssistantConversationPage.ConversationAsync(
            context,
            1,
            new ShellConversationStore(
                [User("Write a page")],
                activeRun: new Run {
                    Id = "run-1",
                    ConversationId = 1,
                    Status = RunStatus.AwaitingOwner,
                    Model = "test-model",
                    ApprovalMode = mode.ToString(),
                    PromptVersion = PageAuthorPrompt.Version,
                },
                pendingInterrupt: pending,
                approvalMode: mode),
            new TestAntiforgery(),
            new RunRegistry(TestOptions.Assistant, TimeProvider.System),
            TestOptions.Assistant,
            ModelWindowSource.Fixed(TestOptions.Budget),
            new StubAccountBalanceClient());

        return await TestHttp.ReadBodyAsync(context);
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

    [Test]
    public async Task AnEstimatePastTheMarginTakesTheExceededClassAsync() {
        // A run whose estimate was far from what the provider counted: the row that says so is the one
        // the eye should find, the way it finds every other exceeded budget.
        string html = await RenderAsync(
            new Run {
                Id = "run-1",
                ConversationId = 1,
                Status = "completed",
                Model = "test-model",
                ApprovalMode = ApprovalMode.AlwaysApprove.ToString(),
                PromptVersion = PageAuthorPrompt.Version,
                EstimatedTokens = 100_000,
                LastPromptTokens = 1_000,
                MaxTokens = 900,
                OutputTokens = 300,
            },
            User("A question", "run-1"),
            Assistant("An answer", "run-1"));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<div class=\"assistant-budget assistant-budget-exceeded\" data-budget=\"estimate\">"));
            Assert.That(html, Does.Contain("<span id=\"assistant-budget-estimate\">99k</span> / <span class=\"assistant-budget-limit\" id=\"assistant-budget-estimate-limit\">28k</span> <span id=\"assistant-budget-estimate-direction\">over</span> tokens</span>"));
            Assert.That(html, Does.Contain("<span id=\"assistant-budget-output\">300</span> / <span class=\"assistant-budget-limit\" id=\"assistant-budget-output-limit\">900</span> tokens</span>"),
                "the completion row is read against the allowance that step had, not the model's ceiling");
        });
    }

    private static async Task<string> RenderAsync(Run? lastRun, params Message[] messages) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/assistant/1");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        await AssistantConversationPage.ConversationAsync(
            context,
            1,
            new ShellConversationStore(messages, lastRun: lastRun),
            new TestAntiforgery(),
            new RunRegistry(TestOptions.Assistant, TimeProvider.System),
            TestOptions.Assistant,
            ModelWindowSource.Fixed(TestOptions.Budget),
            new StubAccountBalanceClient());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static Task<string> RenderAsync(params Message[] messages) => RenderAsync(null, messages);

}
