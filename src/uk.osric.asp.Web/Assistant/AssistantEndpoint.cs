// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.ServerSentEvents;
using System.Security.Claims;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;

/// <summary>Serves the assistant page and the JSON endpoints its stream and approvals use.</summary>
internal static class AssistantEndpoint {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task ListAsync(
        HttpContext context,
        IConversationStore conversations,
        IAntiforgery antiforgery,
        IOptions<AssistantOptions> options) {
        string? owner = Owner(context);
        if (owner is null) {
            await WriteConversationNotFoundAsync(context);
            return;
        }

        if (context.Request.Method == "POST") {
            NoStore(context);
            if (!IsPostValid(context)) {
                await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                    "assistant.form_expired",
                    "The conversation was not created",
                    "The form could not be verified. Reload this page and try again.",
                    [new("/assistant", "Conversations")]));
                return;
            }
            Conversation created = await conversations.CreateAsync(owner, context.RequestAborted);
            Redirect.SeeOther(context, $"/assistant/{created.Id}");
            return;
        }

        IReadOnlyList<ConversationListItem> items = await conversations.ListAsync(owner, context.RequestAborted);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Assistant", stylesheet: "/assistant.css");
        await context.Response.WriteAsync("<h1>Assistant</h1>");
        await context.Response.WriteAsync(PageLayout.Nav("Assistant navigation", [
            new("/assistant", "Conversations", true),
            new("/edit", "Page list"),
        ]));
        await context.Response.WriteAsync($"""
            <form method="post" action="/assistant">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">New conversation</button>
            </form>
            """);
        if (items.Count == 0) {
            await context.Response.WriteAsync("<p>No conversations yet.</p>");
        } else {
            await context.Response.WriteAsync("<ul class=\"assistant-conversations\">");
            foreach (ConversationListItem item in items) {
                string active = item.ActiveRunId is null ? "" : " <span>Active</span>";
                await context.Response.WriteAsync(
                    $"<li><a href=\"/assistant/{item.Id}\">{Encode(item.Title)}</a> <time>{Encode(item.UpdatedAt.ToString("u"))}</time>{active}</li>");
            }
            await context.Response.WriteAsync("</ul>");
        }
        if (!options.Value.Enabled) {
            await context.Response.WriteAsync("<p>The assistant is disabled.</p>");
        }
        await ShellDocument.EndAsync(context);
    }

    public static async Task ConversationAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        IAntiforgery antiforgery,
        RunRegistry registry,
        AssistantOptions options) {
        string? owner = Owner(context);
        Conversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await WriteConversationNotFoundAsync(context);
            return;
        }

        IReadOnlyList<Message> messages = await conversations.GetMessagesAsync(id, owner!, context.RequestAborted);
        Run? activeRun = await conversations.GetActiveRunAsync(id, owner!, context.RequestAborted);
        Run? lastRun = activeRun ?? await conversations.GetLastRunAsync(id, owner!, context.RequestAborted);
        RunStream? activeStream = activeRun is null ? null : registry.Find(activeRun.Id);
        bool replaying = activeStream?.CanReplayFrom(0) == true;
        long replayFrom = replaying ? 0 : activeStream?.LastSequence ?? 0;
        IEnumerable<Message> visibleMessages = messages.Where(message =>
            !replaying || message.RunId != activeRun!.Id || message.Role == "user");
        IReadOnlyList<ToolApproval> pending = conversation.ApprovalMode == ApprovalMode.RequireApproval.ToString()
            && activeRun is not null
                ? await conversations.GetPendingApprovalsAsync(id, owner!, context.RequestAborted)
                : [];
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        SecurityHeaders.AllowScripts(context);
        await ShellDocument.BeginAsync(context, conversation.Title, stylesheet: "/assistant.css", bodyClass: "assistant-page");
        await context.Response.WriteAsync($"<h1>{Encode(conversation.Title)}</h1>");
        await context.Response.WriteAsync(PageLayout.Nav("Assistant navigation", [
            new("/assistant", "Conversations"),
            new("/edit", "Page list"),
        ]));

        await context.Response.WriteAsync($"""
            <div class="assistant-chat" id="assistant-chat" data-conversation-id="{id}" data-active-run="{Encode(activeRun?.Id ?? "")}" data-replay-from="{replayFrom}">
                <p id="assistant-status" role="status" aria-live="polite"></p>
                <fieldset>
                    <legend>Assistant messages</legend>
                    <section id="assistant-transcript" aria-label="Conversation">
            """);

        Dictionary<string, (string Name, string Arguments)> calls = ReadToolCalls(messages);
        foreach (Message message in visibleMessages) {
            await WriteMessageAsync(context, message, calls);
        }
        await WritePendingApprovalsAsync(context, calls, pending, tokens, id);
        await WriteStoppedRunAsync(context, lastRun);
        await context.Response.WriteAsync($"""
                    </section>
                </fieldset>

                <fieldset class="assistant-compose">
                    <legend><label for="assistant-message">Your message</label></legend>
                    <form id="assistant-message-form" method="post" action="/assistant/{id}/messages">
                        {AntiforgeryField.Hidden(tokens)}
                        <textarea id="assistant-message" name="content" rows="5" maxlength="{options.MaxMessageChars}" required></textarea>
                        <p class="assistant-char-count" aria-hidden="true"><span id="assistant-char-count">0</span> / {options.MaxMessageChars}</p>
                        <button type="submit">Send</button>
                    </form>
                    <form id="assistant-stop-form" method="post" action="/assistant/{id}/stop"{(activeRun is null ? " hidden" : "")}>
                        {AntiforgeryField.Hidden(tokens)}
                        <button type="submit">Stop</button>
                    </form>
                </fieldset>

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
                                    <option value="AlwaysApprove"{Selected(conversation.ApprovalMode == "AlwaysApprove")}>Always approve</option>
                                    <option value="RequireApproval"{Selected(conversation.ApprovalMode == "RequireApproval")}>Require approval</option>
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
                <noscript><p>JavaScript is disabled. Your message will still start a run; refresh this page to see the response.</p></noscript>
            </div>
            <script type="module" src="/assistant.js"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    public static async Task MessagesAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        RunQueue queue,
        RunRegistry registry,
        AssistantOptions options) {
        NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The message could not be sent",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("content", out Microsoft.Extensions.Primitives.StringValues contentValues)
            || contentValues.Count != 1
            || string.IsNullOrWhiteSpace(contentValues[0])
            || contentValues[0]!.Length > options.MaxMessageChars) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.message_invalid", "The message is empty or too long",
                $"A message needs one value no longer than {options.MaxMessageChars} characters.");
            return;
        }
        if (await conversations.GetActiveRunAsync(id, owner, context.RequestAborted) is not null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                "assistant.run_active", "A run is already in progress",
                "Wait for it to finish, or stop it, then send the message again.");
            return;
        }
        if (!queue.TryReserve()) {
            await WriteJsonErrorAsync(context, StatusCodes.Status429TooManyRequests,
                "assistant.queue_full", "The run queue is full",
                "Too many runs are waiting. Try again shortly.");
            return;
        }

        bool reservationConsumed = false;
        try {
            RunStartResult started = await conversations.StartRunAsync(
                id, owner, contentValues[0]!, options.Model, PageAuthorPrompt.Version, context.RequestAborted);
            if (started.Status != RunStartStatus.Started || started.RunId is null) {
                await WriteJsonErrorAsync(context,
                    started.Status == RunStartStatus.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict,
                    started.Status == RunStartStatus.NotFound ? "assistant.conversation_not_found" : "assistant.run_not_started",
                    started.Status == RunStartStatus.NotFound ? "That conversation does not exist" : "The run could not be started",
                    started.Status == RunStartStatus.NotFound
                        ? "It may have been deleted, or it belongs to another account."
                        : "Reload the conversation and try again.");
                return;
            }

            if (!registry.TryStart(started.RunId, id, out _)) {
                await conversations.SetRunStatusAsync(started.RunId, "failed", "registry_conflict", "The run could not be started.", CancellationToken.None);
                await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                    "assistant.run_conflict", "The run could not be started",
                    "Reload the conversation and try again.");
                return;
            }
            if (!queue.TryEnqueueReserved(new QueuedRun(id, started.RunId, owner))) {
                await conversations.SetRunStatusAsync(started.RunId, "failed", "queue_full", "The run queue is full.", CancellationToken.None);
                registry.Complete(started.RunId, id);
                await WriteJsonErrorAsync(context, StatusCodes.Status429TooManyRequests,
                    "assistant.queue_full", "The run queue is full",
                    "Too many runs are waiting. Try again shortly.");
                return;
            }

            reservationConsumed = true;
            if (context.Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)) {
                Redirect.SeeOther(context, $"/assistant/{id}");
                return;
            }
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { runId = started.RunId }, s_jsonOptions));
        } finally {
            if (!reservationConsumed) {
                queue.ReleaseReservation();
            }
        }
    }

    public static async Task StopAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        RunRegistry registry) {
        NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The run could not be stopped",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        Run? active = await conversations.GetActiveRunAsync(id, owner, context.RequestAborted);
        if (active is null) {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        _ = registry.Stop(active.Id);
        await conversations.FailPendingApprovalsAsync(active.Id, "stop", CancellationToken.None);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public static async Task<IResult> EventsAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        RunRegistry registry,
        AssistantOptions options) {
        NoStore(context);
        context.Response.Headers["X-Accel-Buffering"] = "no";
        string? owner = Owner(context);
        if (owner is null) {
            return ErrorResponses.Problem(StatusCodes.Status401Unauthorized, new ErrorMessage(
                "auth.required",
                "Sign in required",
                "You need to sign in before this site will stream that run.",
                []));
        }

        string? requestedRun = context.Request.Query["run"].FirstOrDefault();
        Run? run = requestedRun is null
            ? await conversations.GetActiveRunAsync(id, owner, context.RequestAborted)
            : await conversations.GetRunAsync(id, owner, requestedRun, context.RequestAborted);
        if (requestedRun is not null && run is null) {
            return ErrorResponses.Problem(StatusCodes.Status404NotFound, new ErrorMessage(
                "assistant.run_not_found",
                "That run does not exist",
                "It may have been deleted, or it belongs to another conversation.",
                []));
        }
        if (run is null) {
            return TypedResults.ServerSentEvents(IdleEventsAsync(id));
        }

        RunStream? stream = registry.Find(run.Id);
        if (stream is null) {
            return TypedResults.ServerSentEvents(IdleEventsAsync(id));
        }

        long after = 0;
        bool reset = false;
        string? lastId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!string.IsNullOrEmpty(lastId)) {
            int separator = lastId.LastIndexOf(':');
            if (separator <= 0 || !long.TryParse(lastId.AsSpan(separator + 1), out after)) {
                reset = true;
            } else if (!string.Equals(lastId[..separator], run.Id, StringComparison.Ordinal)) {
                reset = true;
            }
        } else if (long.TryParse(context.Request.Query["after"], out long queryAfter)) {
            after = queryAfter;
        }

        return TypedResults.ServerSentEvents(ReadEventsAsync(stream, after, reset, options.HeartbeatSeconds, context.RequestAborted));
    }

    public static async Task OptionsAsync(HttpContext context, long id, IConversationStore conversations) {
        NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The options could not be saved",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = Owner(context);
        if (owner is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        Conversation? conversation = await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("title", out Microsoft.Extensions.Primitives.StringValues title)
            || title.Count != 1 || string.IsNullOrWhiteSpace(title[0]) || title[0]!.Length > ConversationTitle.MaximumLength
            || !context.Request.Form.TryGetValue("mode", out Microsoft.Extensions.Primitives.StringValues mode)
            || mode.Count != 1
            || !Enum.TryParse(mode[0], ignoreCase: false, out ApprovalMode approvalMode)
            || !Enum.IsDefined(approvalMode)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.options_invalid", "The options could not be saved",
                $"A title no longer than {ConversationTitle.MaximumLength} characters and an approval mode are required.");
            return;
        }

        // An unchanged title keeps its generated flag, so saving a mode on its own does not stop the
        // conversation taking its title from the first message.
        string trimmed = title[0]!.Trim();
        if (!string.Equals(trimmed, conversation.Title, StringComparison.Ordinal)
            && !await conversations.RenameAsync(id, owner, trimmed, context.RequestAborted)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        if (conversation.ApprovalMode != approvalMode.ToString()
            && !await conversations.SetApprovalModeAsync(id, owner, approvalMode, context.RequestAborted)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        // A page submit reloads the conversation; the script posts the same form and gets no body.
        if (context.Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)) {
            Redirect.SeeOther(context, $"/assistant/{id}");
            return;
        }
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public static async Task ApprovalAsync(
        HttpContext context,
        long id,
        string runId,
        string toolCallId,
        IConversationStore conversations,
        ToolApprovalCoordinator coordinator) {
        NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The decision could not be recorded",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = Owner(context);
        if (owner is null || !context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("decision", out Microsoft.Extensions.Primitives.StringValues decision)
            || decision.Count != 1 || decision[0] is not ("approve" or "deny")) {
            if (owner is null) {
                await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                    "assistant.conversation_not_found", "That conversation does not exist",
                    "It may have been deleted, or it belongs to another account.");
            } else {
                await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                    "assistant.decision_invalid", "The decision could not be recorded",
                    "Choose approve or deny, then try again.");
            }
            return;
        }

        ApprovalResolutionResult result = await conversations.ResolveApprovalAsync(
            id, owner, runId, toolCallId, decision[0]!, context.RequestAborted);
        if (result is ApprovalResolutionResult.Approved or ApprovalResolutionResult.Denied) {
            _ = coordinator.Signal(runId, toolCallId, result);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        } else if (result == ApprovalResolutionResult.NotFound) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.approval_not_found", "That approval is no longer waiting",
                "The run moved on before the decision arrived. Reload the conversation to see its state.");
        } else {
            await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                "assistant.approval_conflict", "That approval could not be recorded",
                "The run is no longer waiting for it. Reload the conversation to see its state.");
        }
    }

    public static async Task DeleteConfirmationAsync(HttpContext context, long id, IConversationStore conversations, IAntiforgery antiforgery) {
        string? owner = Owner(context);
        Conversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await WriteConversationNotFoundAsync(context);
            return;
        }
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Delete conversation", stylesheet: "/assistant.css");
        await context.Response.WriteAsync("<h1>Delete conversation</h1>");
        await context.Response.WriteAsync($"<p>Delete {Encode(conversation.Title)} and its transcript? Pages are not affected.</p>");
        await context.Response.WriteAsync($"""
            <form method="post" action="/assistant/{id}/delete">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Delete conversation</button>
            </form>
            <p><a href="/assistant/{id}">Cancel</a></p>
            """);
        await ShellDocument.EndAsync(context);
    }

    public static async Task DeleteAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        RunRegistry registry) {
        NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The run could not be stopped",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }
        Run? active = await conversations.GetActiveRunAsync(id, owner, context.RequestAborted);
        if (active is not null) {
            _ = registry.Stop(active.Id);
            await conversations.FailPendingApprovalsAsync(active.Id, "conversation_deleted", CancellationToken.None);
        }
        if (!await conversations.DeleteAsync(id, owner, context.RequestAborted)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
                "assistant.conversation_not_found",
                "That conversation does not exist",
                "It may already have been deleted. Nothing was deleted.",
                [new("/assistant", "Conversations")]));
            return;
        }
        Redirect.SeeOther(context, "/assistant");
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
                await context.Response.WriteAsync($"<pre class=\"assistant-content\">{Encode(message.Content)}</pre>");
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

    private static async Task WritePendingApprovalsAsync(
        HttpContext context,
        IReadOnlyDictionary<string, (string Name, string Arguments)> calls,
        IReadOnlyList<ToolApproval> pending,
        AntiforgeryTokenSet tokens,
        long conversationId) {
        if (pending.Count == 0) {
            return;
        }
        await context.Response.WriteAsync("<section class=\"assistant-approvals\" aria-label=\"Pending tool approvals\"><h2>Approval required</h2>");
        foreach (ToolApproval approval in pending) {
            if (!calls.TryGetValue(approval.ToolCallId, out (string Name, string Arguments) call)) {
                continue;
            }
            ToolDefinition? definition = PageToolExecutor.Definitions.SingleOrDefault(tool => tool.Name == call.Name);
            string description = definition?.Description ?? "Review this tool call.";
            string action = $"/assistant/{conversationId}/runs/{Encode(approval.RunId)}/tool-calls/{Encode(approval.ToolCallId)}/approval";
            await context.Response.WriteAsync($"<article><h3>{Encode(MessageText.Sanitize(call.Name))}</h3><p>{Encode(description)}</p><pre>{Encode(call.Arguments)}</pre>");
            foreach ((string decision, string label) in new[] { ("approve", "Approve"), ("deny", "Deny") }) {
                await context.Response.WriteAsync($"<form method=\"post\" action=\"{action}\">{AntiforgeryField.Hidden(tokens)}<input type=\"hidden\" name=\"decision\" value=\"{decision}\"><button type=\"submit\">{label}</button></form>");
            }
            await context.Response.WriteAsync("</article>");
        }
        await context.Response.WriteAsync("</section>");
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

    private static Dictionary<string, (string Name, string Arguments)> ReadToolCalls(IEnumerable<Message> messages) {
        Dictionary<string, (string Name, string Arguments)> calls = new(StringComparer.Ordinal);
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

    private static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(
        RunStream stream,
        long after,
        bool reset,
        int heartbeatSeconds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) {
        if (reset) {
            yield return new SseItem<string>(JsonSerializer.Serialize(new { reason = "event id belongs to a different run" }, s_jsonOptions), "stream.reset");
            yield break;
        }
        await using IAsyncEnumerator<RunEvent> events = stream.ReadAfterAsync(after, cancellationToken).GetAsyncEnumerator(cancellationToken);
        using PeriodicTimer heartbeat = new(TimeSpan.FromSeconds(heartbeatSeconds));
        Task<bool> nextEvent = events.MoveNextAsync().AsTask();
        Task<bool> nextHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
        while (true) {
            Task completed = await Task.WhenAny(nextEvent, nextHeartbeat);
            if (completed == nextHeartbeat) {
                if (!await nextHeartbeat) {
                    yield break;
                }
                yield return new SseItem<string>(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow }, s_jsonOptions), "heartbeat");
                nextHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                continue;
            }

            if (!await nextEvent) {
                yield break;
            }
            RunEvent @event = events.Current;
            yield return new SseItem<string>(@event.DataJson, @event.Type) { EventId = @event.EventId };
            nextEvent = events.MoveNextAsync().AsTask();
        }
    }

    private static async IAsyncEnumerable<SseItem<string>> IdleEventsAsync(
        long conversationId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new SseItem<string>(JsonSerializer.Serialize(new { conversationId }, s_jsonOptions), "run.idle");
        await Task.CompletedTask;
    }

    private static string? Owner(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool IsPostValid(HttpContext context) =>
        context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false };

    private static Task WriteConversationNotFoundAsync(HttpContext context) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
            "assistant.conversation_not_found",
            "That conversation does not exist",
            "It may have been deleted, or it belongs to another account.",
            [new("/assistant", "Conversations")]));

    private static Task WriteJsonErrorAsync(HttpContext context, int statusCode, string code, string title, string summary) =>
        ErrorResponses.WriteJsonAsync(context, statusCode, new ErrorMessage(code, title, summary, []));

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Selected(bool selected) => selected ? " selected" : "";
}
