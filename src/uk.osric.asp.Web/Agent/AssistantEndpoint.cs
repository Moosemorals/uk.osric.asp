// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.ServerSentEvents;
using System.Security.Claims;
using System.Text.Json;
using uk.osric.asp.Core.Agent;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Storage;

/// <summary>Serves the assistant page and the JSON endpoints its stream and approvals use.</summary>
internal static class AssistantEndpoint {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task ListAsync(
        HttpContext context,
        IAgentConversationStore conversations,
        IAntiforgery antiforgery,
        IOptions<AgentOptions> options) {
        string? owner = Owner(context);
        if (owner is null) {
            NoStore(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (context.Request.Method == "POST") {
            NoStore(context);
            if (!PostIsValid(context)) {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            AgentConversation created = await conversations.CreateAsync(owner, context.RequestAborted);
            Redirect.SeeOther(context, $"/assistant/{created.Id}");
            return;
        }

        IReadOnlyList<AgentConversationListItem> items = await conversations.ListAsync(owner, context.RequestAborted);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Assistant", stylesheet: "/assistant.css");
        await context.Response.WriteAsync("<h1>Assistant</h1>");
        await context.Response.WriteAsync(PageLayout.Nav("Assistant navigation", [
            new("/assistant", "Conversations", true),
            new("/edit", "Page listing"),
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
            foreach (AgentConversationListItem item in items) {
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
        IAgentConversationStore conversations,
        IAntiforgery antiforgery,
        AgentRunRegistry registry,
        AgentOptions options) {
        string? owner = Owner(context);
        AgentConversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            NoStore(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        IReadOnlyList<AgentMessage> messages = await conversations.GetMessagesAsync(id, owner!, context.RequestAborted);
        AgentRun? activeRun = await conversations.GetActiveRunAsync(id, owner!, context.RequestAborted);
        AgentRunStream? activeStream = activeRun is null ? null : registry.Find(activeRun.Id);
        bool replaying = activeStream?.CanReplayFrom(0) == true;
        long replayFrom = replaying ? 0 : activeStream?.LastSequence ?? 0;
        IEnumerable<AgentMessage> visibleMessages = messages.Where(message =>
            !replaying || message.RunId != activeRun!.Id || message.Role == "user");
        IReadOnlyList<AgentToolApproval> pending = conversation.ApprovalMode == AgentApprovalMode.RequireApproval.ToString()
            && activeRun is not null
                ? await conversations.GetPendingApprovalsAsync(id, owner!, context.RequestAborted)
                : [];
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        SecurityHeaders.AllowScripts(context);
        await ShellDocument.BeginAsync(context, conversation.Title, stylesheet: "/assistant.css", bodyClass: "assistant-page");
        await context.Response.WriteAsync($"<h1>{Encode(conversation.Title)}</h1>");
        await context.Response.WriteAsync(PageLayout.Nav("Assistant navigation", [
            new("/assistant", "Conversations"),
            new("/edit", "Page listing"),
        ]));

        await context.Response.WriteAsync($"""
            <div class="assistant-chat" id="assistant-chat" data-conversation-id="{id}" data-active-run="{Encode(activeRun?.Id ?? "")}" data-replay-from="{replayFrom}">
                <form method="post" action="/assistant/{id}/rename" class="assistant-rename">
                    {AntiforgeryField.Hidden(tokens)}
                    <label for="conversation-title">Conversation title</label>
                    <input id="conversation-title" name="title" maxlength="200" value="{Encode(conversation.Title)}" required>
                    <button type="submit">Rename</button>
                </form>
                <form method="post" action="/assistant/{id}/approval-mode" class="assistant-approval-mode">
                    {AntiforgeryField.Hidden(tokens)}
                    <label for="approval-mode">Tool approval</label>
                    <select id="approval-mode" name="mode">
                        <option value="AlwaysApprove"{Selected(conversation.ApprovalMode == "AlwaysApprove")}>Always approve</option>
                        <option value="RequireApproval"{Selected(conversation.ApprovalMode == "RequireApproval")}>Require approval</option>
                    </select>
                    <button type="submit">Update</button>
                </form>
                <p id="assistant-status" role="status" aria-live="polite"></p>
                <section id="assistant-transcript" aria-label="Conversation">
            """);

        Dictionary<string, (string Name, string Arguments)> calls = ReadToolCalls(messages);
        foreach (AgentMessage message in visibleMessages) {
            await WriteMessageAsync(context, message, calls);
        }
        await WritePendingApprovalsAsync(context, calls, pending, tokens, id);
        await context.Response.WriteAsync("</section>");
        await context.Response.WriteAsync($"""
            <form id="assistant-message-form" method="post" action="/assistant/{id}/messages">
                {AntiforgeryField.Hidden(tokens)}
                <label for="assistant-message">Message</label>
                <textarea id="assistant-message" name="content" rows="5" maxlength="{options.MaxMessageChars}" required></textarea>
                <p class="assistant-char-count" aria-hidden="true"><span id="assistant-char-count">0</span> / {options.MaxMessageChars}</p>
                <button type="submit">Send</button>
            </form>
            <form id="assistant-stop-form" method="post" action="/assistant/{id}/stop"{(activeRun is null ? " hidden" : "")}>
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Stop</button>
            </form>
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
        IAgentConversationStore conversations,
        AgentRunQueue queue,
        AgentRunRegistry registry,
        AgentOptions options) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("content", out Microsoft.Extensions.Primitives.StringValues contentValues)
            || contentValues.Count != 1
            || string.IsNullOrWhiteSpace(contentValues[0])
            || contentValues[0]!.Length > options.MaxMessageChars) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (await conversations.GetActiveRunAsync(id, owner, context.RequestAborted) is not null) {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }
        if (!queue.TryReserve()) {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        bool reservationConsumed = false;
        try {
            AgentRunStartResult started = await conversations.StartRunAsync(
                id, owner, contentValues[0]!, options.Model, PageAuthorPrompt.Version, context.RequestAborted);
            if (started.Status != AgentRunStartStatus.Started || started.RunId is null) {
                context.Response.StatusCode = started.Status == AgentRunStartStatus.NotFound
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status409Conflict;
                return;
            }

            if (!registry.TryStart(started.RunId, id, out _)) {
                await conversations.SetRunStatusAsync(started.RunId, "failed", "registry_conflict", "The run could not be started.", CancellationToken.None);
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            if (!queue.TryEnqueueReserved(new AgentQueuedRun(id, started.RunId, owner))) {
                await conversations.SetRunStatusAsync(started.RunId, "failed", "queue_full", "The run queue is full.", CancellationToken.None);
                registry.Complete(started.RunId, id);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
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
        IAgentConversationStore conversations,
        AgentRunRegistry registry) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        AgentRun? active = await conversations.GetActiveRunAsync(id, owner, context.RequestAborted);
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
        IAgentConversationStore conversations,
        AgentRunRegistry registry,
        AgentOptions options) {
        NoStore(context);
        context.Response.Headers["X-Accel-Buffering"] = "no";
        string? owner = Owner(context);
        if (owner is null) {
            return Results.Unauthorized();
        }

        string? requestedRun = context.Request.Query["run"].FirstOrDefault();
        AgentRun? run = requestedRun is null
            ? await conversations.GetActiveRunAsync(id, owner, context.RequestAborted)
            : await conversations.GetRunAsync(id, owner, requestedRun, context.RequestAborted);
        if (requestedRun is not null && run is null) {
            return Results.NotFound();
        }
        if (run is null) {
            return TypedResults.ServerSentEvents(IdleEventsAsync(id));
        }

        AgentRunStream? stream = registry.Find(run.Id);
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

    public static async Task RenameAsync(HttpContext context, long id, IAgentConversationStore conversations, IAntiforgery antiforgery) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || !context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("title", out Microsoft.Extensions.Primitives.StringValues title)
            || title.Count != 1 || string.IsNullOrWhiteSpace(title[0]) || title[0]!.Length > 200) {
            context.Response.StatusCode = owner is null ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return;
        }
        if (!await conversations.RenameAsync(id, owner, title[0]!, context.RequestAborted)) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        Redirect.SeeOther(context, $"/assistant/{id}");
    }

    public static async Task ApprovalModeAsync(HttpContext context, long id, IAgentConversationStore conversations) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || !context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("mode", out Microsoft.Extensions.Primitives.StringValues mode)
            || mode.Count != 1
            || !Enum.TryParse(mode[0], ignoreCase: false, out AgentApprovalMode approvalMode)
            || !Enum.IsDefined(approvalMode)) {
            context.Response.StatusCode = owner is null ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return;
        }
        if (!await conversations.SetApprovalModeAsync(id, owner, approvalMode, context.RequestAborted)) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        Redirect.SeeOther(context, $"/assistant/{id}");
    }

    public static async Task ApprovalAsync(
        HttpContext context,
        long id,
        string runId,
        string toolCallId,
        IAgentConversationStore conversations,
        AgentToolApprovalCoordinator coordinator) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || !context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("decision", out Microsoft.Extensions.Primitives.StringValues decision)
            || decision.Count != 1 || decision[0] is not ("approve" or "deny")) {
            context.Response.StatusCode = owner is null ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return;
        }

        AgentApprovalResolutionResult result = await conversations.ResolveApprovalAsync(
            id, owner, runId, toolCallId, decision[0]!, context.RequestAborted);
        if (result is AgentApprovalResolutionResult.Approved or AgentApprovalResolutionResult.Denied) {
            _ = coordinator.Signal(runId, toolCallId, result);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        } else {
            context.Response.StatusCode = result == AgentApprovalResolutionResult.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict;
        }
    }

    public static async Task DeleteConfirmationAsync(HttpContext context, long id, IAgentConversationStore conversations, IAntiforgery antiforgery) {
        string? owner = Owner(context);
        AgentConversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            NoStore(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
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
        IAgentConversationStore conversations,
        AgentRunRegistry registry) {
        NoStore(context);
        if (!PostIsValid(context)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        string? owner = Owner(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        AgentRun? active = await conversations.GetActiveRunAsync(id, owner, context.RequestAborted);
        if (active is not null) {
            _ = registry.Stop(active.Id);
            await conversations.FailPendingApprovalsAsync(active.Id, "conversation_deleted", CancellationToken.None);
        }
        if (!await conversations.DeleteAsync(id, owner, context.RequestAborted)) {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        Redirect.SeeOther(context, "/assistant");
    }

    private static async Task WriteMessageAsync(
        HttpContext context,
        AgentMessage message,
        IReadOnlyDictionary<string, (string Name, string Arguments)> calls) {
        if (message.Role == "user") {
            await context.Response.WriteAsync($"<article class=\"assistant-message user-message\"><h2 class=\"visually-hidden\">You</h2><pre>{Encode(message.Content)}</pre></article>");
            return;
        }
        if (message.Role == "assistant") {
            await context.Response.WriteAsync("<article class=\"assistant-message\"><h2 class=\"visually-hidden\">Assistant</h2>");
            if (!string.IsNullOrEmpty(message.Reasoning)) {
                await context.Response.WriteAsync($"<details class=\"assistant-reasoning\"><summary>Thinking</summary><pre>{Encode(message.Reasoning)}</pre></details>");
            }
            if (!string.IsNullOrEmpty(message.Content)) {
                await context.Response.WriteAsync($"<pre class=\"assistant-content\">{Encode(message.Content)}</pre>");
            }
            if (message.Status != "complete") {
                await context.Response.WriteAsync($"<p>{Encode(message.Status)}</p>");
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
        IReadOnlyList<AgentToolApproval> pending,
        AntiforgeryTokenSet tokens,
        long conversationId) {
        if (pending.Count == 0) {
            return;
        }
        await context.Response.WriteAsync("<section class=\"assistant-approvals\" aria-label=\"Pending tool approvals\"><h2>Approval required</h2>");
        foreach (AgentToolApproval approval in pending) {
            if (!calls.TryGetValue(approval.ToolCallId, out (string Name, string Arguments) call)) {
                continue;
            }
            AgentToolDefinition? definition = AgentPageToolExecutor.Definitions.SingleOrDefault(tool => tool.Name == call.Name);
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

    private static Dictionary<string, (string Name, string Arguments)> ReadToolCalls(IEnumerable<AgentMessage> messages) {
        Dictionary<string, (string Name, string Arguments)> calls = new(StringComparer.Ordinal);
        foreach (AgentMessage message in messages.Where(message => message.ToolCallsJson is not null)) {
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
        AgentRunStream stream,
        long after,
        bool reset,
        int heartbeatSeconds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) {
        if (reset) {
            yield return new SseItem<string>(JsonSerializer.Serialize(new { reason = "event id belongs to a different run" }, s_jsonOptions), "stream.reset");
            yield break;
        }
        await using IAsyncEnumerator<AgentEvent> events = stream.ReadAfterAsync(after, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
            AgentEvent @event = events.Current;
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

    private static bool PostIsValid(HttpContext context) =>
        context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false };

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Selected(bool selected) => selected ? " selected" : "";
}
