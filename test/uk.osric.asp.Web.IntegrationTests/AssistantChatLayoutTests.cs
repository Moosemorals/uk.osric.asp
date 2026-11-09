// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed partial class AssistantChatLayoutTests : IntegrationTest {
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
        int budgets = body.IndexOf("<legend>Budgets</legend>", StringComparison.Ordinal);
        int usage = body.IndexOf("<legend>Tokens this run</legend>", StringComparison.Ordinal);
        Assert.That(transcript, Is.GreaterThanOrEqualTo(0), "the transcript fieldset is missing");
        Assert.That(budgets, Is.GreaterThanOrEqualTo(0), "the budgets fieldset is missing");
        Assert.That(usage, Is.GreaterThan(budgets), "usage must follow the budgets");
        Assert.That(options, Is.GreaterThanOrEqualTo(0), "the page options fieldset is missing");
        Assert.That(compose, Is.GreaterThan(transcript), "the message fieldset must follow the transcript");
        Assert.That(budgets, Is.GreaterThan(compose), "budgets must follow the message fieldset");
        Assert.That(options, Is.GreaterThan(usage), "page options must follow the usage");

        int title = body.IndexOf("<input id=\"conversation-title\"", StringComparison.Ordinal);
        int input = body.IndexOf("<textarea id=\"assistant-message\"", StringComparison.Ordinal);
        Assert.That(title, Is.GreaterThan(options));
        Assert.That(input, Is.InRange(compose, options));
        Assert.That(body, Does.Contain("<dt><label for=\"conversation-title\">Conversation title</label></dt>"));
        Assert.That(body, Does.Contain("<dt><label for=\"approval-mode\">Tool approval</label></dt>"));

        Match summary = ToolSummary().Match(body);
        Assert.That(summary.Success, Is.True, "the tool call is not hidden behind a details element");
        string summaryText = WebUtility.HtmlDecode(summary.Groups["text"].Value);
        Assert.That(summaryText, Does.StartWith("read_page {"));
        Assert.That(summaryText, Does.Not.Contain("layout-test"), "the summary must truncate the arguments");
        Assert.That(summaryText, Has.Length.EqualTo("read_page ".Length + MessageText.MaximumLength));
        Assert.That(summaryText, Does.EndWith(MessageText.Ellipsis));

        Assert.That(body, Does.Contain($"<pre class=\"assistant-tool-arguments\">{WebUtility.HtmlEncode(Arguments)}</pre>"));
        Match response = ToolResponse().Match(body);
        Assert.That(response.Success, Is.True, "the tool response is not shown");
        Assert.That(response.Groups["text"].Value, Does.Contain("&quot;ok&quot;"));
    }

    [Test]
    public async Task TheBudgetsSectionReportsWhatEachLimitHasHadSpentOnItAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string body = await RunAndReloadAsync(client, conversation);

        Assert.That(body, Does.Contain("<dt>Message</dt>"));
        Assert.That(body, Does.Contain("<dt>Request</dt>"));
        Assert.That(body, Does.Contain("<span id=\"assistant-budget-message\">0</span> / 16k chars</span>"));
        Assert.That(body, Does.Contain(" / 90k tokens</span>"), "the request budget is missing");
        Assert.That(body, Does.Contain(" / 512 steps</span>"), "the step budget is missing");
        Assert.That(body, Does.Contain(" / 1k calls</span>"), "the tool-call budget is missing");
        Assert.That(body, Does.Contain(" / 33k tokens</span>"), "the output budget is missing");

        // The system prompt and tool definitions alone are already spent, and the run's own steps are
        // counted from the store.
        Assert.That(body, Does.Match("<meter id=\"assistant-budget-context-meter\" aria-hidden=\"true\" min=\"0\" max=\"90000\" value=\"[1-9][0-9]*\""));
        Assert.That(body, Does.Contain("<span class=\"assistant-budget-count\"><span id=\"assistant-budget-steps\">2</span> / 512 steps</span>"),
            "the answered run took two steps: the tool call and the answer");
        Assert.That(body, Does.Contain("<span class=\"assistant-budget-count\"><span id=\"assistant-budget-tool-calls\">1</span> / 1k calls</span>"));

        // Every row carries the name the page's script looks it up by.
        foreach (string row in new[] { "message", "context", "steps", "tool-calls", "tool-arguments", "tool-result", "output" }) {
            Assert.That(body, Does.Contain($"data-budget=\"{row}\""), $"the {row} row is missing its hook");
        }
    }

    [Test]
    public async Task TheUsageSectionReportsWhatTheProviderReportedAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string eventStream = await RunAsync(client, conversation);
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        string body = await reloaded.Content.ReadAsStringAsync();
        JsonElement usage = ParseEvents(eventStream).Last(item => item.Type == "budget.updated").Data.GetProperty("usage");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("<legend>Tokens this run</legend>"));
            Assert.That(body, Does.Contain($"<span id=\"assistant-usage-prompt\">{BudgetAmount.Format(usage.GetProperty("prompt").GetInt64())}</span>"));
            Assert.That(body, Does.Contain($"<span id=\"assistant-usage-cached\">{BudgetAmount.Format(usage.GetProperty("cached").GetInt64())}</span>"));
            Assert.That(body, Does.Contain($"<span id=\"assistant-usage-completion\">{BudgetAmount.Format(usage.GetProperty("completion").GetInt64())}</span>"));
            Assert.That(body, Does.Contain($"<span id=\"assistant-usage-reasoning\">{BudgetAmount.Format(usage.GetProperty("reasoning").GetInt64())}</span>"));
            Assert.That(body, Does.Contain($"<span id=\"assistant-usage-total\">{BudgetAmount.Format(usage.GetProperty("total").GetInt64())}</span>"));
            Assert.That(body, Does.Contain("<span id=\"assistant-usage-cached-share\">(60%)</span>"));
            foreach (string row in new[] { "prompt", "cached", "completion", "reasoning", "total" }) {
                Assert.That(body, Does.Contain($"data-usage=\"{row}\""), $"the {row} usage row is missing its hook");
            }
        });
    }

    [Test]
    public async Task ARunPublishesItsUsageAsItSpendsItAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string eventStream = await RunAsync(client, conversation);
        JsonElement usage = ParseEvents(eventStream).Last(item => item.Type == "budget.updated").Data.GetProperty("usage");

        Assert.Multiple(() => {
            Assert.That(usage.GetProperty("prompt").GetInt32(), Is.EqualTo(3_000));
            Assert.That(usage.GetProperty("cached").GetInt32(), Is.EqualTo(1_800));
            Assert.That(usage.GetProperty("completion").GetInt32(), Is.EqualTo(50));
            Assert.That(usage.GetProperty("reasoning").GetInt32(), Is.EqualTo(12));
            Assert.That(usage.GetProperty("total").GetInt32(), Is.EqualTo(3_050));
        });
    }

    [Test]
    public async Task ARunPublishesItsBudgetsAsItSpendsThemAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string eventStream = await RunAsync(client, conversation);
        JsonElement budgets = LastBudgets(eventStream);

        Assert.Multiple(() => {
            Assert.That(eventStream, Does.Contain("event: budget.updated"));
            Assert.That(budgets.GetProperty("steps").GetInt32(), Is.EqualTo(2), "the tool call and the answer are two steps");
            Assert.That(budgets.GetProperty("tool-calls").GetInt32(), Is.EqualTo(1));
            Assert.That(budgets.GetProperty("tool-arguments").GetInt32(), Is.GreaterThan(0));
            Assert.That(budgets.GetProperty("tool-result").GetInt32(), Is.GreaterThan(0));
            Assert.That(budgets.GetProperty("context").GetInt64(), Is.GreaterThan(0), "the system prompt alone is already spent");
            Assert.That(budgets.TryGetProperty("message", out _), Is.False, "the textarea owns the message row");
        });
    }

    [Test]
    public async Task TheStepBudgetIsTheRequestThatStepSent() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        int before = _provider.Requests.Count;

        string eventStream = await RunAsync(client, conversation);
        long[] figures = [.. ParseEvents(eventStream)
            .Where(item => item.Type == "budget.updated")
            .Select(item => item.Data.GetProperty("budgets").GetProperty("context").GetInt64())];
        IReadOnlyList<ChatRequest> sent = [.. _provider.Requests.Skip(before)];

        Assert.Multiple(() => {
            Assert.That(sent, Has.Count.EqualTo(2));
            TokenCalibration? calibration = null;
            for (int index = 0; index < sent.Count; index++) {
                long expected = TokenEstimator.For(calibration).Estimate(ChatRequestJson.MeasureBytes(sent[index]));
                Assert.That(figures, Does.Contain(expected),
                    $"the request row is the estimate of the request step {index + 1} sent");
                // The scripted client reports 1,000 prompt tokens for the tool call and 2,000 for
                // the answer, which is what calibrates the next step.
                calibration = new TokenCalibration(
                    ChatRequestJson.MeasureBytes(sent[index]), index == 0 ? 1_000 : 2_000);
            }
        });
    }

    [Test]
    public async Task TheBudgetsAfterTheReloadMatchTheLastLiveFigureAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        JsonElement budgets = LastBudgets(await RunAsync(client, conversation));
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        IReadOnlyDictionary<string, string> shown = RenderedBudgets(await reloaded.Content.ReadAsStringAsync());

        foreach (string row in new[] { "context", "steps", "tool-calls", "tool-arguments", "tool-result", "output" }) {
            Assert.That(shown[row], Is.EqualTo(BudgetAmount.Format(budgets.GetProperty(row).GetInt64())),
                $"the {row} row disagrees with the last figure the run published");
        }
    }

    [Test]
    public async Task AToolOnlyStepLeavesNoBlankAssistantRowAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);

        string body = await RunAndReloadAsync(client, conversation);

        Assert.That(body, Does.Contain("I read the page."));
        Assert.That(body, Does.Not.Contain("<article class=\"assistant-message\"><h2 class=\"visually-hidden\">Assistant</h2></article>"),
            "a step that only calls a tool must not render an empty assistant row");
    }

    [Test]
    public async Task ACompletedStepIsAvailableAsRestrictedHtmlAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        string conversation = await CreateConversationAsync(client);
        string eventStream = await RunAsync(client, conversation);

        long messageId = ParseEvents(eventStream)
            .Last(item => item.Type == "message.completed"
                && item.Data.GetProperty("status").GetString() == "complete")
            .Data.GetProperty("messageId").GetInt64();

        using HttpResponseMessage content = await client.GetAsync(
            $"{conversation}/messages/{messageId}/content");
        string html = await content.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(content.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(html, Does.Contain("<p>I read the page.</p>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Name</th>"));
            Assert.That(html, Does.Not.Contain("style="));
        });
    }

    private static async Task<string> RunAndReloadAsync(HttpClient client, string conversation) {
        await RunAsync(client, conversation);
        using HttpResponseMessage reloaded = await client.GetAsync(conversation);
        return await reloaded.Content.ReadAsStringAsync();
    }

    private static async Task<string> RunAsync(HttpClient client, string conversation) {
        string messageToken = await GetAntiforgeryTokenAsync(client, conversation);
        using HttpResponseMessage started = await client.PostAsync(conversation + "/messages", Form(
            ("__RequestVerificationToken", messageToken),
            ("content", "Look at a page")));
        Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using JsonDocument run = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string runId = run.RootElement.GetProperty("runId").GetString()!;

        using HttpResponseMessage events = await client.GetAsync(
            $"{conversation}/events?run={Uri.EscapeDataString(runId)}&after=0");
        return await events.Content.ReadAsStringAsync();
    }

    private static JsonElement LastBudgets(string eventStream) =>
        ParseEvents(eventStream).Last(item => item.Type == "budget.updated").Data.GetProperty("budgets");

    private static IReadOnlyDictionary<string, string> RenderedBudgets(string body) {
        Dictionary<string, string> budgets = new(StringComparer.Ordinal);
        foreach (Match match in BudgetCount().Matches(body)) {
            budgets[match.Groups["row"].Value] = match.Groups["used"].Value;
        }
        return budgets;
    }

    private static IReadOnlyList<(string Type, JsonElement Data)> ParseEvents(string eventStream) {
        List<(string Type, JsonElement Data)> events = [];
        foreach (string block in eventStream.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) {
            string? type = null;
            string? data = null;
            foreach (string line in block.Split('\n')) {
                if (line.StartsWith("event: ", StringComparison.Ordinal)) {
                    type = line["event: ".Length..];
                } else if (line.StartsWith("data: ", StringComparison.Ordinal)) {
                    data = line["data: ".Length..];
                }
            }
            if (type is null || data is null) {
                continue;
            }
            using JsonDocument document = JsonDocument.Parse(data);
            events.Add((type, document.RootElement.Clone()));
        }
        return events;
    }

    private sealed class ToolCallingClient(string arguments) : IChatCompletionClient {
        private readonly List<ChatRequest> _requests = [];
        private int _request;

        public IReadOnlyList<ChatRequest> Requests {
            get {
                lock (_requests) {
                    return [.. _requests];
                }
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            lock (_requests) {
                _requests.Add(request);
            }
            // The fixture's host is shared, so requests alternate tool call, answer, tool call, answer.
            IReadOnlyList<ChatStreamEvent> events = Interlocked.Increment(ref _request) % 2 == 1
                ? [
                    new ToolCallDelta(0, "layout-call", "read_page", arguments),
                    new UsageEvent(new TokenUsage(1_000, 20, 600, 5)),
                    new Finished("tool_calls"),
                ]
                : [
                    new ContentDelta("I read the page.\n\n| Name | Value |\n| --- | --- |\n| one | 1 |"),
                    new UsageEvent(new TokenUsage(2_000, 30, 1_200, 7)),
                    new Finished("stop"),
                ];
            foreach (ChatStreamEvent @event in events) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return @event;
                await Task.Yield();
            }
        }
    }

    [GeneratedRegex("<span id=\"assistant-budget-(?<row>[a-z-]+)\">(?<used>[0-9.]+[kMG]?)</span>")]
    private static partial Regex BudgetCount();

    [GeneratedRegex("<details class=\"assistant-tool\"><summary>(?<text>[^<]*)</summary>")]
    private static partial Regex ToolSummary();

    [GeneratedRegex("<pre class=\"assistant-tool-response\">(?<text>[^<]+)</pre>")]
    private static partial Regex ToolResponse();
}
