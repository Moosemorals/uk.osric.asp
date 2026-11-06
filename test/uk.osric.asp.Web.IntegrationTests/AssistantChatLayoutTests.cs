// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Agent;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Agent;

[TestFixture]
public sealed class AssistantChatLayoutTests : IntegrationTest {
    private const string Arguments = """{"path":"/missing-page-for-layout-test"}""";

    private readonly ToolCallingClient _provider = new(Arguments);

    internal override AspApplication CreateApplication() => new(agentEnabled: true, chatCompletionClient: _provider);

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
        Assert.That(options, Is.GreaterThanOrEqualTo(0), "the page options fieldset is missing");
        Assert.That(compose, Is.GreaterThan(options), "the message fieldset must follow page options");
        Assert.That(transcript, Is.GreaterThan(compose), "the transcript must follow the message fieldset");

        int title = body.IndexOf("<input id=\"conversation-title\"", StringComparison.Ordinal);
        int input = body.IndexOf("<textarea id=\"assistant-message\"", StringComparison.Ordinal);
        Assert.That(title, Is.InRange(options, compose));
        Assert.That(input, Is.InRange(compose, transcript));
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

    private static async Task<string> CreateConversationAsync(HttpClient client) {
        string token = await GetAntiforgeryTokenAsync(client, "/assistant");
        using HttpResponseMessage created = await client.PostAsync("/assistant", Form(("__RequestVerificationToken", token)));
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
        return created.Headers.Location!.ToString();
    }

    private sealed class ToolCallingClient(string arguments) : IChatCompletionClient {
        private readonly IReadOnlyList<AgentChatStreamEvent>[] _responses = [
            [
                new AgentToolCallDelta(0, "layout-call", "read_page", arguments),
                new AgentFinished("tool_calls"),
            ],
            [new AgentContentDelta("I read the page."), new AgentFinished("stop")],
        ];
        private int _request;

        public async IAsyncEnumerable<AgentChatStreamEvent> StreamAsync(
            AgentChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            int index = Interlocked.Increment(ref _request) - 1;
            foreach (AgentChatStreamEvent @event in _responses[index]) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }
}
