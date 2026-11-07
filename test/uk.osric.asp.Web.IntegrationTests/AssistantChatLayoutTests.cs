// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantChatLayoutTests : IntegrationTest {
    private const string Arguments = """{"path":"/missing-page-for-layout-test"}""";

    private readonly ToolCallingClient _provider = new(Arguments);

    internal override AspApplication CreateApplication() => new(assistantEnabled: true, chatCompletionClient: _provider);

    [Test]
    public async Task ChatGroupsControlsInOrderedFieldsetsAndHidesToolDetailBehindASummaryAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Look at a page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync(
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        string eventStream = await events.Content.ReadAsStringAsync();
        Assert.That(eventStream, Does.Contain("event: tool.started"));
        Assert.That(eventStream, Does.Contain("event: tool.finished"));
        Assert.That(eventStream, Does.Contain("missing-page-for-layout-test"));

        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();

        int options = body.IndexOf("<legend>Page options</legend>", StringComparison.Ordinal);
        int compose = body.IndexOf("<legend><label for=\"assistant-message\">Your message</label></legend>", StringComparison.Ordinal);
        int transcript = body.IndexOf("<legend>Assistant messages</legend>", StringComparison.Ordinal);
        Assert.That(transcript, Is.GreaterThanOrEqualTo(0), "the transcript fieldset is missing");
        Assert.That(options, Is.GreaterThanOrEqualTo(0), "the page options fieldset is missing");
        Assert.That(compose, Is.GreaterThan(transcript), "the message fieldset must follow the transcript");
        Assert.That(options, Is.GreaterThan(compose), "page options must follow the message fieldset");

        int title = body.IndexOf("<input id=\"conversation-title\"", StringComparison.Ordinal);
        int input = body.IndexOf("<textarea id=\"assistant-message\"", StringComparison.Ordinal);
        Assert.That(title, Is.GreaterThan(options));
        Assert.That(input, Is.InRange(compose, options));
        Assert.That(body, Does.Contain("<dt><label for=\"conversation-title\">Conversation title</label></dt>"));
        Assert.That(body, Does.Contain("<dt><label for=\"approval-mode\">Tool approval</label></dt>"));

        Match summary = Regex.Match(body, "<details class=\"assistant-tool\"><summary>(?<text>[^<]*)</summary>");
        Assert.That(summary.Success, Is.True, "the tool call is not hidden behind a details element");
        string summaryText = WebUtility.HtmlDecode(summary.Groups["text"].Value);
        Assert.That(summaryText, Does.StartWith("read_page {"));
        Assert.That(summaryText, Does.Not.Contain("layout-test"), "the summary must truncate the arguments");
        Assert.That(summaryText, Has.Length.EqualTo("read_page ".Length + MessageText.MaximumLength));
        Assert.That(summaryText, Does.EndWith(MessageText.Ellipsis));

        Assert.That(body, Does.Contain($"<pre class=\"assistant-tool-arguments\">{WebUtility.HtmlEncode(Arguments)}</pre>"));
        Match response = Regex.Match(body, "<pre class=\"assistant-tool-response\">(?<text>[^<]+)</pre>");
        Assert.That(response.Success, Is.True, "the tool response is not shown");
        Assert.That(response.Groups["text"].Value, Does.Contain("&quot;ok&quot;"));
    }

    [Test]
    public async Task AToolOnlyStepLeavesNoBlankAssistantRowAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Look at a page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync(
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        await events.Content.ReadAsStringAsync();

        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("I read the page."));
        Assert.That(body, Does.Not.Contain("<article class=\"assistant-message\"><h2 class=\"visually-hidden\">Assistant</h2></article>"),
            "a step that only calls a tool must not render an empty assistant row");
    }

    private sealed class ToolCallingClient(string arguments) : IChatCompletionClient {
        private int _request;

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            // The fixture's host is shared, so requests alternate tool call, answer, tool call, answer.
            IReadOnlyList<ChatStreamEvent> events = Interlocked.Increment(ref _request) % 2 == 1
                ? [new ToolCallDelta(0, "layout-call", "read_page", arguments), new Finished("tool_calls")]
                : [new ContentDelta("I read the page."), new Finished("stop")];
            foreach (ChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
