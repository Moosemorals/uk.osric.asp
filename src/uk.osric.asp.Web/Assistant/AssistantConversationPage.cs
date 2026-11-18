// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
using static uk.osric.asp.Web.Html.Markup;

/// <summary>The conversation page: the transcript, the compose box and the figures under them.</summary>
/// <remarks>
/// The endpoint answers and owns the routes; the markup a reader sees lives here, so the page can be
/// read as markup rather than as a method between two routes. Every figure on it is one the run
/// publishes or the store holds, so a live row and a reloaded one cannot disagree.
/// </remarks>
internal static class AssistantConversationPage {

    public static async Task ConversationAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        IAntiforgery antiforgery,
        RunRegistry registry,
        AssistantOptions options,
        ModelWindowSource windows,
        IAccountBalanceClient balanceClient) {
        ArgumentNullException.ThrowIfNull(context);

        ContextBudget? budget = windows.Budget;
        string messageLimit = budget is null ? string.Empty
            : $" maxlength=\"{budget.MessageChars.ToString(CultureInfo.InvariantCulture)}\"";
        string messageScale = budget is null ? string.Empty : $" / {BudgetAmount.Format(budget.MessageChars)}";
        string? owner = SignedInOwner.Of(context);
        Conversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await AssistantEndpoint.WriteConversationNotFoundAsync(context);
            return;
        }

        // The balance is the page's one outbound call, so it runs while the transcript is read and
        // is awaited before the document starts: a slow provider costs the page its own deadline,
        // and a failure is still a page rather than a half-written response.
        Task<AccountBalanceRead> balanceTask = balanceClient.GetAsync(context.RequestAborted);
        IReadOnlyList<Message> messages = await conversations.GetMessagesAsync(id, owner!, context.RequestAborted);
        string alwaysApprove = nameof(ApprovalMode.AlwaysApprove);
        string requireApproval = nameof(ApprovalMode.RequireApproval);
        Run? activeRun = await conversations.GetActiveRunAsync(id, owner!, context.RequestAborted);
        Run? lastRun = activeRun ?? await conversations.GetLastRunAsync(id, owner!, context.RequestAborted);
        RunStream? activeStream = activeRun is null ? null : registry.Find(activeRun.Id);
        bool replaying = activeStream?.CanReplayFrom(0) == true;
        long replayFrom = replaying ? 0 : activeStream?.LastSequence ?? 0;
        IEnumerable<Message> visibleMessages = messages.Where(message =>
            !replaying || message.RunId != activeRun!.Id || message.Role == "user");
        // A pending interrupt is a fact about the run, not a setting: a conversation whose mode changed
        // after the card appeared still has a run blocked on it.
        OwnerInterrupt? pending = activeRun is null
            ? null
            : await conversations.GetPendingInterruptAsync(id, owner!, context.RequestAborted);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        AccountBalance? balance = (await balanceTask).Balance;
        SecurityHeaders.AllowScripts(context);
        await ShellDocument.BeginAsync(context, conversation.Title,
            SiteNavigation.For(context), stylesheets: ["/assistant.css"], bodyClass: "assistant-page");
        await context.Response.WriteAsync($"<h1>{Encode(conversation.Title)}</h1>");
        await context.Response.WriteAsync(AssistantNavigation.Nav(context));

        await context.Response.WriteAsync($"""
            <div class="assistant-chat" id="assistant-chat" data-conversation-id="{id}" data-active-run="{Encode(activeRun?.Id ?? "")}" data-replay-from="{replayFrom}">
                <fieldset class="assistant-compose">
                    <legend><label for="assistant-message">Message</label></legend>
                    <form id="assistant-message-form" method="post" action="/assistant/{id}/messages">
                        {AntiforgeryField.Hidden(tokens)}
                        <textarea id="assistant-message" name="content" rows="5"{messageLimit} required></textarea>
                        <div class="assistant-compose-actions">
                            <button type="submit" id="assistant-send">Send</button>
                            <!-- Stopping is its own action, and formnovalidate lets an empty box still stop the run. -->
                            <button type="submit" id="assistant-stop" formaction="/assistant/{id}/stop" formnovalidate{(activeRun is null ? " hidden" : "")}>Stop</button>
                            <p class="assistant-char-count" aria-hidden="true"><span id="assistant-char-count">0</span>{messageScale}</p>
                        </div>
                    </form>
                </fieldset>

                <p id="assistant-status" role="status" aria-live="polite"></p>
                <fieldset>
                    <legend>Assistant messages</legend>
                    <section id="assistant-transcript" aria-label="Conversation">
            """);

        Dictionary<string, (string Name, string Arguments)> calls = ReadToolCalls(messages);
        // The transcript reads newest first, so the latest run sits directly under the prompt box. A
        // run still going has not changed anything yet as far as a reader is concerned, and the card
        // that says what a finished turn changed leads that turn's block.
        await WriteStoppedRunAsync(context, lastRun);
        await WriteInterruptAsync(context, pending, tokens, id);
        foreach (IGrouping<string, Message> turn in visibleMessages.Reverse().GroupBy(message => message.RunId)) {
            if (!string.Equals(turn.Key, activeRun?.Id, StringComparison.Ordinal)) {
                await WriteChangedPagesAsync(context, turn);
            }
            foreach (Message message in turn) {
                await WriteMessageAsync(context, message, calls);
            }
        }
        await context.Response.WriteAsync("""
                    </section>
                </fieldset>
            """);
        // The page builds the plan the next step would send from the same stored transcript and the
        // same options the run uses, so the figure a reader reloads is the one the run published.
        List<ChatMessage> stored = [new ChatMessage("system", PageAuthorPrompt.Build()), .. messages.Select(message => message.ToChatMessage())];
        long estimateError = lastRun?.EstimatedTokens is { } storedEstimate && lastRun.LastPromptTokens is { } storedPrompt
            ? storedEstimate - storedPrompt
            : 0;
        BudgetSnapshot budgets = BudgetSnapshot.Empty;
        if (budget is not null) {
            ChatRequest request = new(
                lastRun?.Model ?? options.Model,
                stored,
                PageToolExecutor.Definitions,
                options.Temperature,
                budget.MinimumOutputTokens,
                options.ReasoningField);
            TokenEstimator estimator = TokenEstimator.For(
                lastRun?.RequestBytes is { } calibratedBytes && lastRun?.LastPromptTokens is { } calibratedPrompt
                    ? new TokenCalibration(calibratedBytes, calibratedPrompt)
                    : null);
            ContextPlan plan = ContextPlanner.Build(request, budget, estimator);
            budgets = BudgetSnapshot.Measure(
                plan,
                stored,
                lastRun?.StepCount ?? 0,
                lastRun?.ToolCallCount ?? 0,
                lastRun?.OutputTokens,
                lastRun?.MaxTokens,
                budget,
                estimateError);
        }
        await WriteBudgetsAsync(context, budgets, options, budget, lastRun);
        await WriteUsageAsync(context, lastRun?.ToUsage() ?? RunUsage.Empty);
        await WriteAccountBalanceAsync(context, balance);
        await context.Response.WriteAsync($"""
                <fieldset>
                    <legend>Page options</legend>
                    <form id="assistant-options-form" method="post" action="/assistant/{id}/options" class="assistant-options form">
                        {AntiforgeryField.Hidden(tokens)}
                        <dl>
                            <dt><label for="conversation-title">Conversation title</label></dt>
                            <dd><input id="conversation-title" name="title" maxlength="200" value="{Encode(conversation.Title)}" required></dd>
                            <dt><label for="approval-mode">Tool approval</label></dt>
                            <dd>
                                <select id="approval-mode" name="mode">
                                    <option value="{alwaysApprove}"{Selected(conversation.ApprovalMode == alwaysApprove)}>Always approve</option>
                                    <option value="{requireApproval}"{Selected(conversation.ApprovalMode == requireApproval)}>Require approval</option>
                                </select>
                            </dd>
                        </dl>
                        <div class="assistant-option-actions form-actions">
                            <button type="submit">Save</button>
                            <p id="assistant-options-status" role="status" aria-live="polite"></p>
                        </div>
                    </form>
                </fieldset>

                <p><a href="/assistant/{id}/delete">Delete conversation</a></p>
                <noscript><p>JavaScript is disabled. Your prompt will still start a run; refresh this page to see the response.</p></noscript>
            </div>
            <script type="module" src="{WebUtility.HtmlEncode(await ShellDocument.AssetUrlAsync(context, "/assistant.js", context.RequestAborted))}"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteMessageAsync(
        HttpContext context,
        Message message,
        IReadOnlyDictionary<string, (string Name, string Arguments)> calls) {
        if (message.Role == "user") {
            await context.Response.WriteAsync($"<article class=\"assistant-message user-message\"><h2 class=\"visually-hidden\">You</h2><pre>{Encode(message.Content)}</pre></article>");
            return;
        }
        if (message.Role == "assistant") {
            // A step that only calls tools has nothing to show, but its message row still carries
            // the call arguments the tool details need.
            if (string.IsNullOrEmpty(message.Content) && string.IsNullOrEmpty(message.Reasoning) && message.Status == "complete") {
                return;
            }
            await context.Response.WriteAsync("<article class=\"assistant-message\"><h2 class=\"visually-hidden\">Assistant</h2>");
            if (!string.IsNullOrEmpty(message.Reasoning)) {
                await context.Response.WriteAsync($"<details class=\"assistant-reasoning\"><summary>Thinking</summary><pre>{Encode(message.Reasoning)}</pre></details>");
            }
            if (!string.IsNullOrEmpty(message.Content)) {
                await context.Response.WriteAsync(
                    $"<div class=\"assistant-content\">{MarkdownRenderer.ToRestrictedHtml(message.Content)}</div>");
            }
            if (message.Status != "complete") {
                await context.Response.WriteAsync($"<p class=\"assistant-incomplete\">{Encode(Describe(message.Status))}</p>");
            }
            await context.Response.WriteAsync("</article>");
            return;
        }
        if (message.Role == "tool") {
            string name = Encode(MessageText.Sanitize(message.ToolName ?? "Tool"));
            string arguments = message.ToolCallId is not null && calls.TryGetValue(message.ToolCallId, out (string Name, string Arguments) call)
                ? call.Arguments
                : "";
            string summary = string.IsNullOrEmpty(arguments) ? name : $"{name} {Encode(MessageText.Sanitize(arguments))}";
            await context.Response.WriteAsync($"<details class=\"assistant-tool\"><summary>{summary}</summary>");
            if (!string.IsNullOrEmpty(arguments)) {
                await context.Response.WriteAsync($"<p class=\"assistant-tool-label\">Arguments</p><pre class=\"assistant-tool-arguments\">{Encode(arguments)}</pre>");
            }
            await context.Response.WriteAsync($"<p class=\"assistant-tool-label\">Response</p><pre class=\"assistant-tool-response\">{Encode(message.Content)}</pre></details>");
        }
    }

    // The card is read from the stored tool results rather than published live: the page reloads on
    // the terminal event, and a result the run reported is what the reload would show anyway.
    private static async Task WriteChangedPagesAsync(HttpContext context, IEnumerable<Message> turn) {
        List<PagePath> changed = [];
        foreach (Message message in turn) {
            if (message.Role == "tool"
                && PageWrites.Written(PageToolExecutor.ChangesPages(message.ToolName), message.Content) is { } path
                && !changed.Contains(path)) {
                changed.Add(path);
            }
        }
        if (changed.Count == 0) {
            return;
        }

        await context.Response.WriteAsync("<section class=\"assistant-changed-pages\"><h2>Changed pages</h2><ul>");
        foreach (PagePath path in changed) {
            string key = Encode(path.Key);
            await context.Response.WriteAsync(
                $"<li><a href=\"{Encode(PageUrls.Page(path))}\">{key}</a> <a href=\"{Encode(path.Editor(EditorPaths.Of(context.RequestServices)))}\">Edit<span class=\"visually-hidden\"> {key}</span></a></li>");
        }
        await context.Response.WriteAsync("</ul></section>");
    }

    /// <summary>Writes the card a run is stopped on, by the kind the record names.</summary>
    /// <remarks>
    /// Rendered from the record rather than from the event stream, so a reload or another device shows
    /// the same card, and the words are the ones the live event carried. The record says what kind the
    /// run waits on, which is why the route needs no kind of its own.
    /// </remarks>
    private static async Task WriteInterruptAsync(
        HttpContext context,
        OwnerInterrupt? interrupt,
        AntiforgeryTokenSet tokens,
        long conversationId) {
        if (interrupt is null) {
            return;
        }

        switch (interrupt.Kind) {
            case OwnerInterruptKind.Approval when InterruptPayloads.Approval(interrupt.RequestJson) is { } approval:
                await WriteApprovalAsync(context, interrupt, approval, tokens, conversationId);
                break;
            case OwnerInterruptKind.Continuation when InterruptPayloads.Continuation(interrupt.RequestJson) is { } continuation:
                await WriteContinuationAsync(context, interrupt, continuation, tokens, conversationId);
                break;
            default:
                // A request this build cannot read still leaves a run blocked, so the owner is told
                // that and offered the one thing that ends it.
                await context.Response.WriteAsync($"""
                    <section class="assistant-interrupt" aria-label="Run waiting" data-interrupt-kind="{Encode(interrupt.Kind)}">
                        <h2>The run is waiting for you</h2>
                        <p>This card could not be read. Stopping the run ends it; the transcript is kept.</p>
                    </section>
                    """);
                break;
        }
    }

    private static async Task WriteApprovalAsync(
        HttpContext context,
        OwnerInterrupt interrupt,
        ApprovalRequest approval,
        AntiforgeryTokenSet tokens,
        long conversationId) {
        string action = $"/assistant/{conversationId}/runs/{Encode(interrupt.RunId)}/interrupt";
        await context.Response.WriteAsync(
            $"<section class=\"assistant-approvals\" aria-label=\"Pending tool approvals\" data-interrupt-kind=\"{Encode(interrupt.Kind)}\"><h2>Approval required</h2>");
        await context.Response.WriteAsync(
            $"<article data-tool-call-id=\"{Encode(interrupt.ToolCallId ?? string.Empty)}\"><h3>{Encode(MessageText.Sanitize(approval.Name))}</h3><p>{Encode(approval.Description)}</p><pre>{Encode(approval.Arguments)}</pre>");
        foreach ((string decision, string label) in new[] { ("approve", "Approve"), ("deny", "Deny") }) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{action}">
                    {AntiforgeryField.Hidden(tokens)}
                    <input type="hidden" name="toolCallId" value="{Encode(interrupt.ToolCallId ?? string.Empty)}">
                    <input type="hidden" name="decision" value="{decision}">
                    <button type="submit">{label}</button>
                </form>
                """);
        }
        await context.Response.WriteAsync("</article></section>");
    }

    /// <summary>Writes the question a run that ran out of a budget asked the owner.</summary>
    private static async Task WriteContinuationAsync(
        HttpContext context,
        OwnerInterrupt interrupt,
        ContinuationRequest continuation,
        AntiforgeryTokenSet tokens,
        long conversationId) {
        string action = $"/assistant/{conversationId}/runs/{Encode(interrupt.RunId)}/interrupt";

        await context.Response.WriteAsync($"""
            <section class="assistant-continuation" aria-label="Run paused" data-budget="{Encode(continuation.Budget)}" data-interrupt-kind="{Encode(interrupt.Kind)}">
                <h2>Do you want to continue?</h2>
                <p>{Encode(continuation.Message)}</p>
                <form method="post" action="{action}">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">Continue</button>
                </form>
                <p class="assistant-continuation-note">Stopping ends the run; the transcript is kept.</p>
            </section>
            """);
    }

    // Every budget a reader can run into is shown with what has been spent on it, so a run that
    // stops on one names it before it stops. The figures come from the same snapshot the run
    // publishes (`assistant-budgets-are-one-snapshot`), so a live one and a reloaded one cannot disagree.
    private static async Task WriteBudgetsAsync(
        HttpContext context,
        BudgetSnapshot snapshot,
        AssistantOptions options,
        ContextBudget? budget,
        Run? run) {
        await context.Response.WriteAsync("""
            <fieldset class="assistant-budgets">
                <legend>Budgets</legend>
                <dl class="assistant-budget-list">
            """);
        await WriteBudgetAsync(context, "message", "Message", 0, budget?.MessageChars, "chars");
        await WriteBudgetAsync(context, "context", "Context", snapshot.ContextTokens, budget?.PromptBudget, "tokens");
        // The reply's allowance is a figure, not a share: it is whatever the prompt left of the window.
        await WriteBudgetAsync(context, "reply", "Reply", snapshot.MaxTokens, null, "tokens");
        await WriteBudgetAsync(context, "window", "Window", snapshot.WindowUsed, budget?.ContextWindow, "tokens");
        await WriteEstimateAsync(context, snapshot, budget?.EstimateMargin);
        await WriteBudgetAsync(context, "steps", "Steps", snapshot.Steps, run?.StepLimit ?? options.MaxSteps, "steps");
        await WriteBudgetAsync(context, "tool-calls", "Tool calls", snapshot.ToolCalls, run?.ToolCallLimit ?? options.MaxToolCalls, "calls");
        await WriteBudgetAsync(context, "tool-arguments", "Tool arguments", snapshot.LongestArguments, options.MaxToolArgumentChars, "chars");
        await WriteBudgetAsync(context, "tool-result", "Tool result", snapshot.LongestResult, budget?.ToolResultChars, "chars");
        await WriteBudgetAsync(context, "output", "Completion", snapshot.LastCompletionTokens,
            snapshot.LastMaxTokens == 0 ? null : snapshot.LastMaxTokens, "tokens");
        await context.Response.WriteAsync("""
                </dl>
                <p class="assistant-budget-note">Context is the next request's estimate: the provider's count of the last request plus the messages added since, in tokens. Reply is the max_tokens that request leaves, and Window is the two together against the model's reported window. Estimate is the last step's error, signed, against the margin the estimate allows; Completion counts the last step and the allowance it was given. Tool calls counts the calls this run has attempted. While a step streams, Context, Window and Completion are marked estimates until the next snapshot replaces them. The transcript above is complete.</p>
            </fieldset>
            """);
    }

    private static async Task WriteUsageAsync(HttpContext context, RunUsage usage) {
        int share = usage.PromptTokens == 0
            ? 0
            : Math.Clamp((int)Math.Round(100.0 * usage.CachedTokens / usage.PromptTokens), 0, 100);
        await context.Response.WriteAsync($"""
            <fieldset class="assistant-usage">
                <legend>Tokens this run</legend>
                <dl class="assistant-usage-list">
                    <div data-usage="prompt"><dt>Prompt tokens</dt><dd><span id="assistant-usage-prompt">{BudgetAmount.Format(usage.PromptTokens)}</span></dd></div>
                    <div data-usage="cached"><dt>Cached</dt><dd><span id="assistant-usage-cached">{BudgetAmount.Format(usage.CachedTokens)}</span> <span id="assistant-usage-cached-share">({share}%)</span></dd></div>
                    <div data-usage="completion"><dt>Completion</dt><dd><span id="assistant-usage-completion">{BudgetAmount.Format(usage.CompletionTokens)}</span></dd></div>
                    <div data-usage="reasoning"><dt>Reasoning</dt><dd><span id="assistant-usage-reasoning">{BudgetAmount.Format(usage.ReasoningTokens)}</span></dd></div>
                    <div data-usage="total"><dt>Total</dt><dd><span id="assistant-usage-total">{BudgetAmount.Format(usage.TotalTokens)}</span></dd></div>
                </dl>
                <p class="assistant-usage-note">Cached is part of prompt; reasoning is part of completion. Total is prompt + completion. The provider reports usage once per step.</p>
            </fieldset>
            """);
    }

    // The balance is the provider account's, not this site's, and it is read afresh on every page
    // load because a run has just spent some of it. It is an aside: a provider that does not answer
    // costs the panel its figure and nothing else.
    private static async Task WriteAccountBalanceAsync(HttpContext context, AccountBalance? balance) {
        await context.Response.WriteAsync("""
            <fieldset class="assistant-balance">
                <legend>Account balance</legend>
            """);

        if (balance is null || balance.Currencies.Count == 0) {
            string reason = balance is null
                ? "The provider did not report a balance."
                : "The provider did not report a currency balance.";
            await context.Response.WriteAsync($"<p class=\"assistant-balance-note\">{Encode(reason)}</p>");
        } else {
            await context.Response.WriteAsync("<dl class=\"assistant-balance-list\">");
            foreach (CurrencyBalance currency in balance.Currencies) {
                List<string> parts = [];
                if (!string.IsNullOrEmpty(currency.Granted)) {
                    parts.Add($"{currency.Granted} granted");
                }
                if (!string.IsNullOrEmpty(currency.ToppedUp)) {
                    parts.Add($"{currency.ToppedUp} topped up");
                }
                string total = string.IsNullOrEmpty(currency.Total) ? "not reported" : currency.Total;
                string detail = parts.Count == 0
                    ? string.Empty
                    : $" <span class=\"assistant-balance-detail\">{Encode(string.Join(", ", parts))}</span>";
                await context.Response.WriteAsync($"""
                    <div><dt>{Encode(currency.Currency)}</dt><dd><span class="assistant-balance-total">{Encode(total)}</span>{detail}</dd></div>
                    """);
            }
            await context.Response.WriteAsync("</dl>");
            string note = balance.IsAvailable
                ? "The provider's total includes granted and topped-up credit, and is read when this page loads."
                : "The provider says this balance is not enough for API calls.";
            string css = balance.IsAvailable ? "assistant-balance-note" : "assistant-balance-warning";
            await context.Response.WriteAsync($"<p class=\"{css}\">{Encode(note)}</p>");
        }

        await context.Response.WriteAsync("</fieldset>");
    }

    private static async Task WriteBudgetAsync(
        HttpContext context,
        string row,
        string label,
        long used,
        long? available,
        string unit) {
        // The row's name is what the page's script looks it up by, and both ids follow from it, so a
        // live figure and a reloaded one address the same elements. The meter is the picture of a
        // number that is already spelled out over it, so it is hidden from assistive technology
        // rather than announced twice with no name.
        if (available is not { } limit) {
            // A figure with no denominator is a figure, not a share: the provider has not reported a
            // window, so the row says what is known and draws no meter rather than inventing a limit.
            await context.Response.WriteAsync($"""
                <div class="assistant-budget" data-budget="{Encode(row)}">
                    <dt>{Encode(label)}</dt>
                    <dd class="assistant-budget-gauge">
                        <span class="assistant-budget-count"><span id="assistant-budget-{Encode(row)}">{BudgetAmount.Format(used)}</span> {Encode(unit)}</span>
                    </dd>
                </div>
                """);
            return;
        }

        // A meter past its own maximum is invalid markup, so only the number is allowed to exceed it.
        long shown = Math.Clamp(used, 0, limit);
        string exceeded = used > limit ? " assistant-budget-exceeded" : "";
        await context.Response.WriteAsync($"""
            <div class="assistant-budget{exceeded}" data-budget="{Encode(row)}">
                <dt>{Encode(label)}</dt>
                <dd class="assistant-budget-gauge">
                    <meter id="assistant-budget-{Encode(row)}-meter" aria-hidden="true" min="0" max="{limit}" value="{shown}" low="{limit * 3 / 5}" high="{limit * 17 / 20}" optimum="0"></meter>
                    <span class="assistant-budget-count"><span id="assistant-budget-{Encode(row)}">{BudgetAmount.Format(used)}</span> / <span class="assistant-budget-limit" id="assistant-budget-{Encode(row)}-limit">{BudgetAmount.Format(limit)}</span> {Encode(unit)}</span>
                </dd>
            </div>
            """);
    }

    // The estimate row is the one signed figure: its magnitude against the margin the estimate allows,
    // with the direction beside it, because `under` is the direction that can breach the budget.
    private static async Task WriteEstimateAsync(HttpContext context, BudgetSnapshot snapshot, long? allowance) {
        long magnitude = snapshot.EstimateErrorMagnitude;
        string direction = snapshot.EstimateDirection;
        if (allowance is not { } margin) {
            await context.Response.WriteAsync($"""
                <div class="assistant-budget" data-budget="estimate">
                    <dt>Estimate</dt>
                    <dd class="assistant-budget-gauge">
                        <span class="assistant-budget-count"><span id="assistant-budget-estimate">{BudgetAmount.Format(magnitude)}</span> <span id="assistant-budget-estimate-direction">{direction}</span></span>
                    </dd>
                </div>
                """);
            return;
        }

        long shown = Math.Clamp(magnitude, 0, margin);
        string exceeded = magnitude > margin ? " assistant-budget-exceeded" : "";
        await context.Response.WriteAsync($"""
            <div class="assistant-budget{exceeded}" data-budget="estimate">
                <dt>Estimate</dt>
                <dd class="assistant-budget-gauge">
                    <meter id="assistant-budget-estimate-meter" aria-hidden="true" min="0" max="{margin}" value="{shown}" low="{margin * 3 / 5}" high="{margin * 17 / 20}" optimum="0"></meter>
                    <span class="assistant-budget-count"><span id="assistant-budget-estimate">{BudgetAmount.Format(magnitude)}</span> / <span class="assistant-budget-limit" id="assistant-budget-estimate-limit">{BudgetAmount.Format(margin)}</span> <span id="assistant-budget-estimate-direction">{direction}</span> tokens</span>
                </dd>
            </div>
            """);
    }

    // The run's own message is written by the host and is safe to show; a bare status word is not
    // an explanation, and a reader who reloads otherwise has no way to know why an answer stopped.
    private static async Task WriteStoppedRunAsync(HttpContext context, Run? run) {
        if (run is null || run.ErrorMessage is null) {
            return;
        }
        if (run.Status is not ("failed" or "interrupted")) {
            return;
        }

        // The message is a sentence this host wrote, never provider text, so it is quoted whole.
        await context.Response.WriteAsync($"<p class=\"assistant-run-error\">{Encode(run.ErrorMessage)}</p>");
    }

    private static string Describe(string status) => status switch {
        "truncated" => "This answer reached the provider's token limit, so it is incomplete.",
        "failed" => "This answer stopped before it finished.",
        "cancelled" => "This answer was stopped.",
        "interrupted" => "The host restarted before this answer finished.",
        _ => status,
    };

    internal static Dictionary<string, (string Name, string Arguments)> ReadToolCalls(IEnumerable<Message> messages) {
        Dictionary<string, (string Name, string Arguments)> calls = [];
        foreach (Message message in messages.Where(message => message.ToolCallsJson is not null)) {
            using JsonDocument document = JsonDocument.Parse(message.ToolCallsJson!);
            foreach (JsonElement call in document.RootElement.EnumerateArray()) {
                string? id = call.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() : null;
                JsonElement function = call.GetProperty("function");
                string name = function.GetProperty("name").GetString() ?? "tool";
                string arguments = function.GetProperty("arguments").GetString() ?? "{}";
                if (id is not null) {
                    calls[id] = (name, arguments);
                }
            }
        }
        return calls;
    }

    private static string Selected(bool selected) => selected ? " selected" : "";
}
