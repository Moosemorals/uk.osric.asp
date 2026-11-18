// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.ServerSentEvents;
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

/// <summary>Serves the assistant page and the JSON endpoints its stream and approvals use.</summary>
internal static class AssistantEndpoint {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task ListAsync(
        HttpContext context,
        IConversationStore conversations,
        IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);

        string? owner = SignedInOwner.Of(context);
        if (owner is null) {
            await WriteConversationNotFoundAsync(context);
            return;
        }

        if (context.Request.Method == "POST") {
            SecurityHeaders.NoStore(context);
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
        await ShellDocument.BeginAsync(context, "Assistant",
            SiteNavigation.For(context), stylesheets: ["/assistant.css"]);
        await context.Response.WriteAsync("<h1>Assistant</h1>");
        await context.Response.WriteAsync(AssistantNavigation.Nav(context));
        await context.Response.WriteAsync($"""
            <form method="post" action="/assistant">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">New conversation</button>
            </form>
            """);
        if (items.Count == 0) {
            await context.Response.WriteAsync("<p>No conversations yet.</p>");
        } else {
            await context.Response.WriteAsync("<ul class=\"assistant-conversations\" role=\"list\">");
            foreach (ConversationListItem item in items) {
                string active = item.ActiveRunId is null ? "" : " <span class=\"assistant-conversation-active\">Active</span>";
                await context.Response.WriteAsync($"""
                    <li>
                        <a href="/assistant/{item.Id}">{Encode(item.Title)}</a> <time>{Encode(item.UpdatedAt.ToString("u"))}</time>{active}
                        <form class="assistant-conversation-delete" method="post" action="/assistant/{item.Id}/delete">
                            {AntiforgeryField.Hidden(tokens)}
                            <button type="submit" aria-label="Delete {Encode(item.Title)}">Delete</button>
                        </form>
                    </li>
                    """);
            }
            await context.Response.WriteAsync("</ul>");
        }
        await ShellDocument.EndAsync(context);
    }

    public static async Task MessagesAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        RunQueue queue,
        RunRegistry registry,
        AssistantOptions options,
        ModelWindowSource windows) {
        ArgumentNullException.ThrowIfNull(context);

        string? owner = await ValidatedOwnerAsync(context, id, conversations, "The message could not be sent");
        if (owner is null) {
            return;
        }

        // The owner's message is bounded by its share of the model's window rather than by a count of
        // its own: an owner pasting a specification is not an attack, and the newest turn is the one
        // thing trimming cannot touch.
        if (windows.Budget is not { } budget) {
            await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                "assistant.model_window_unknown", "The model's window is not reported",
                "The provider has not reported this model's context window, so the assistant cannot run yet.");
            return;
        }

        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("content", out Microsoft.Extensions.Primitives.StringValues contentValues)
            || contentValues.Count != 1
            || string.IsNullOrWhiteSpace(contentValues[0])
            || contentValues[0]!.Length > budget.MessageChars) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.message_invalid", "The message is empty or too long",
                $"A message needs one value no longer than {budget.MessageChars.ToString(CultureInfo.InvariantCulture)} characters.");
            return;
        }
        if (await conversations.GetActiveRunAsync(id, owner, context.RequestAborted) is not null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                "assistant.run_active", "A run is already in progress",
                "Wait for it to finish, or continue or stop it, then send the message again.");
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
        ArgumentNullException.ThrowIfNull(context);

        string? owner = await ValidatedOwnerAsync(context, id, conversations, "The run could not be stopped");
        if (owner is null) {
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
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        context.Response.Headers["X-Accel-Buffering"] = "no";
        string? owner = SignedInOwner.Of(context);
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
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The options could not be saved",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = SignedInOwner.Of(context);
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
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The decision could not be recorded",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = SignedInOwner.Of(context);
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

    public static async Task ContinueAsync(
        HttpContext context,
        long id,
        string runId,
        IConversationStore conversations,
        RunContinuationCoordinator continuation,
        AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", "The run could not be continued",
                "The form could not be verified. Reload the conversation and try again.");
            return;
        }
        string? owner = SignedInOwner.Of(context);
        Conversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await WriteJsonErrorAsync(context, StatusCodes.Status404NotFound,
                "assistant.conversation_not_found", "That conversation does not exist",
                "It may have been deleted, or it belongs to another account.");
            return;
        }

        // The store is the arbiter: only a run that is still paused can be continued, and it widens
        // the budget that ran out by one window of that budget alone.
        bool continued = await conversations.ContinueRunAsync(
            id, owner!, runId, options.MaxSteps, options.MaxToolCalls, context.RequestAborted);
        if (!continued) {
            await WriteJsonErrorAsync(context, StatusCodes.Status409Conflict,
                "assistant.run_not_paused", "The run is not waiting to continue",
                "It may have finished, been stopped, or already been continued. Reload the conversation to see its state.");
            return;
        }

        _ = continuation.Signal(runId);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public static async Task DeleteConfirmationAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);

        string? owner = SignedInOwner.Of(context);
        Conversation? conversation = owner is null ? null : await conversations.GetAsync(id, owner, context.RequestAborted);
        if (conversation is null) {
            await WriteConversationNotFoundAsync(context);
            return;
        }
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Delete conversation",
            SiteNavigation.For(context), stylesheets: ["/assistant.css"]);
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
        ArgumentNullException.ThrowIfNull(context);

        string? owner = await ValidatedOwnerAsync(context, id, conversations, "The conversation could not be deleted");
        if (owner is null) {
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

    /// <summary>Serves one stored assistant message as the restricted HTML the transcript shows.</summary>
    /// <remarks>
    /// The live transcript streams plain text because a delta is not a document, then asks for this
    /// route when the step completes. The renderer is the only writer, so a live step and the reload
    /// that follows the run cannot disagree about what Markdown is allowed.
    /// </remarks>
    public static async Task MessageContentAsync(
        HttpContext context,
        long id,
        long messageId,
        IConversationStore conversations) {
        string? owner = SignedInOwner.Of(context);
        Message? message = owner is null
            ? null
            : await conversations.GetMessageAsync(id, owner, messageId, context.RequestAborted);
        if (message is null || !string.Equals(message.Role, "assistant", StringComparison.Ordinal)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
                "assistant.message_not_found",
                "That message is not available",
                "It may have been deleted, or it belongs to another account.",
                [new($"/assistant/{id}", "Conversation")]));
            return;
        }

        SecurityHeaders.NoStore(context);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(MarkdownRenderer.ToRestrictedHtml(message.Content));
    }

    private static async IAsyncEnumerable<SseItem<string>> ReadEventsAsync(
        RunStream stream,
        long after,
        bool reset,
        int heartbeatSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new SseItem<string>(JsonSerializer.Serialize(new { conversationId }, s_jsonOptions), "run.idle");
        await Task.CompletedTask;
    }

    /// <summary>Validates a posted conversation form, or answers it and returns null.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="id">The conversation the form names.</param>
    /// <param name="conversations">The store the conversation is looked up in.</param>
    /// <param name="expiredTitle">What the refusal is about, for the invalid-form message.</param>
    /// <returns>The signed-in account, or null when a refusal has already been written.</returns>
    private static async Task<string?> ValidatedOwnerAsync(
        HttpContext context,
        long id,
        IConversationStore conversations,
        string expiredTitle) {
        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest,
                "assistant.form_expired", expiredTitle,
                "The form could not be verified. Reload the conversation and try again.");
            return null;
        }

        string? owner = SignedInOwner.Of(context);
        if (owner is null || await conversations.GetAsync(id, owner, context.RequestAborted) is null) {
            await WriteConversationNotFoundAsync(context);
            return null;
        }

        return owner;
    }


    private static bool IsPostValid(HttpContext context) =>
        context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false };

    internal static Task WriteConversationNotFoundAsync(HttpContext context) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
            "assistant.conversation_not_found",
            "That conversation does not exist",
            "It may have been deleted, or it belongs to another account.",
            [new("/assistant", "Conversations")]));

    private static Task WriteJsonErrorAsync(HttpContext context, int statusCode, string code, string title, string summary) =>
        ErrorResponses.WriteJsonAsync(context, statusCode, new ErrorMessage(code, title, summary, []));


}
