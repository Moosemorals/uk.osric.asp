// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Storage;

/// <summary>The SQLite-backed conversation store.</summary>
internal sealed class SqliteConversationStore(StorageDbContext database, TimeProvider clock) : IConversationStore {

    public async Task<Conversation> CreateAsync(string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (owner.Length > 64) {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }

        DateTimeOffset now = clock.GetUtcNow();
        Conversation conversation = new() {
            Owner = owner,
            Title = ConversationTitle.NewConversation,
            CreatedAt = now,
            UpdatedAt = now,
        };

        database.Add(conversation);
        await database.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public async Task<IReadOnlyList<ConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return await database.Set<Conversation>()
            .FromSqlInterpolated($"SELECT * FROM \"AssistantConversations\" WHERE \"Owner\" = {owner} ORDER BY \"UpdatedAt\" DESC, \"Id\" DESC")
            .AsNoTracking()
            .Select(conversation => new ConversationListItem(
                conversation.Id,
                conversation.Title,
                conversation.ApprovalMode,
                conversation.UpdatedAt,
                database.Set<Run>().Where(run => run.ConversationId == conversation.Id
                        && (run.Status == "queued" || run.Status == "running" || run.Status == "awaiting_approval"))
                    .Select(run => run.Id)
                    .FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
    }

    public Task<Conversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return database.Set<Conversation>().AsNoTracking()
            .SingleOrDefaultAsync(conversation => conversation.Id == id && conversation.Owner == owner, cancellationToken);
    }

    public async Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(title);
        string normalized = title.Trim();
        if (normalized.Length is 0 or > 200) {
            return false;
        }

        int updated = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == id && conversation.Owner == owner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.Title, normalized)
                .SetProperty(conversation => conversation.HasGeneratedTitle, false)
                .SetProperty(conversation => conversation.UpdatedAt, clock.GetUtcNow()), cancellationToken);
        return updated > 0;
    }

    public async Task<bool> SetApprovalModeAsync(
        long id,
        string owner,
        ApprovalMode mode,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!Enum.IsDefined(mode)) {
            return false;
        }

        int updated = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == id && conversation.Owner == owner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.ApprovalMode, mode.ToString())
                .SetProperty(conversation => conversation.UpdatedAt, clock.GetUtcNow()), cancellationToken);
        return updated > 0;
    }

    public async Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        int deleted = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == id && conversation.Owner == owner)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<RunStartResult> StartRunAsync(
        long id,
        string owner,
        string content,
        string model,
        string promptVersion,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);

        DateTimeOffset now = clock.GetUtcNow();
        string runId = Guid.NewGuid().ToString("N");
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using DbCommand update = database.Database.GetDbConnection().CreateCommand();
        update.Transaction = transaction.GetDbTransaction();
        update.CommandText = """
            UPDATE "AssistantConversations"
            SET "NextMessageSequence" = "NextMessageSequence" + 1,
                "UpdatedAt" = $now,
                    "Title" = CASE WHEN "HasGeneratedTitle" = 1 AND "Title" = $defaultTitle AND "NextMessageSequence" = 1 THEN $title ELSE "Title" END
            WHERE "Id" = $id AND "Owner" = $owner
            RETURNING "NextMessageSequence" - 1, "ApprovalMode";
            """;
        AddParameter(update, "$now", now);
        AddParameter(update, "$defaultTitle", ConversationTitle.NewConversation);
        AddParameter(update, "$title", ConversationTitle.From(content));
        AddParameter(update, "$id", id);
        AddParameter(update, "$owner", owner);

        await using DbDataReader reader = await update.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) {
            await transaction.RollbackAsync(cancellationToken);
            return new RunStartResult(RunStartStatus.NotFound, null, null);
        }

        long sequence = reader.GetInt64(0);
        string approvalMode = reader.GetString(1);
        await reader.DisposeAsync();

        Run run = new() {
            Id = runId,
            ConversationId = id,
            Status = "queued",
            Model = model,
            ApprovalMode = approvalMode,
            PromptVersion = promptVersion,
            CreatedAt = now,
        };
        Message userMessage = new() {
            ConversationId = id,
            RunId = runId,
            Sequence = sequence,
            Role = "user",
            Content = content,
            CreatedAt = now,
        };
        database.Add(run);
        database.Add(userMessage);

        try {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        } catch (DbUpdateException exception) when (SqliteErrors.IsUniqueViolation(exception)) {
            await transaction.RollbackAsync(cancellationToken);
            database.Entry(run).State = EntityState.Detached;
            database.Entry(userMessage).State = EntityState.Detached;
            return new RunStartResult(RunStartStatus.AlreadyActive, null, null);
        }

        return new RunStartResult(RunStartStatus.Started, runId, sequence);
    }

    public async Task<long?> AppendMessageAsync(
        long conversationId,
        string runId,
        MessageInput input,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Role);
        ArgumentNullException.ThrowIfNull(input.Content);

        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using DbCommand update = database.Database.GetDbConnection().CreateCommand();
        update.Transaction = transaction.GetDbTransaction();
        update.CommandText = """
            UPDATE "AssistantConversations"
            SET "NextMessageSequence" = "NextMessageSequence" + 1, "UpdatedAt" = $now
            WHERE "Id" = $id
            RETURNING "NextMessageSequence" - 1;
            """;
        AddParameter(update, "$now", now);
        AddParameter(update, "$id", conversationId);

        object? value = await update.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull) {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        Message message = new() {
            ConversationId = conversationId,
            RunId = runId,
            Sequence = Convert.ToInt64(value, CultureInfo.InvariantCulture),
            Role = input.Role,
            Content = input.Content,
            Reasoning = input.Reasoning,
            ToolCallsJson = input.ToolCallsJson,
            ToolCallId = input.ToolCallId,
            ToolName = input.ToolName,
            Status = input.Status,
            CreatedAt = now,
        };
        database.Add(message);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message.Sequence;
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        bool owned = await OwnsConversationAsync(id, owner, cancellationToken);
        if (!owned) {
            return [];
        }

        return await database.Set<Message>().AsNoTracking()
            .Where(message => message.ConversationId == id)
            .OrderBy(message => message.Sequence)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        bool owned = await OwnsConversationAsync(id, owner, cancellationToken);
        return owned
            ? await database.Set<Run>().AsNoTracking()
                .Where(run => run.ConversationId == id
                && (run.Status == "queued" || run.Status == "running" || run.Status == "awaiting_approval"))
                .SingleOrDefaultAsync(cancellationToken)
            : null;
    }

    public async Task<Run?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        bool owned = await OwnsConversationAsync(id, owner, cancellationToken);
        if (!owned) {
            return null;
        }

        // SQLite cannot order by a DateTimeOffset, and a conversation has a handful of runs, so the
        // newest is picked here rather than in the query.
        IReadOnlyList<Run> runs = await database.Set<Run>().AsNoTracking()
            .Where(run => run.ConversationId == id)
            .ToArrayAsync(cancellationToken);
        return runs
            .OrderByDescending(run => run.CreatedAt)
            .ThenByDescending(run => run.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public async Task<Run?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        bool owned = await OwnsConversationAsync(conversationId, owner, cancellationToken);
        return owned
            ? await database.Set<Run>().AsNoTracking()
                .SingleOrDefaultAsync(run => run.ConversationId == conversationId && run.Id == runId, cancellationToken)
            : null;
    }

    public async Task<bool> SetRunStatusAsync(
        string runId,
        string status,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        DateTimeOffset now = clock.GetUtcNow();
        long? conversationId = await database.Set<Run>()
            .Where(run => run.Id == runId)
            .Select(run => (long?)run.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (conversationId is null) {
            return false;
        }

        int updated = await database.Set<Run>()
            .Where(run => run.Id == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, status)
                .SetProperty(run => run.ErrorCode, errorCode)
                .SetProperty(run => run.ErrorMessage, errorMessage)
                .SetProperty(run => run.StartedAt, run => run.StartedAt ?? (status == "running" ? now : null))
                .SetProperty(run => run.CompletedAt, status == "queued" || status == "running" || status == "awaiting_approval" ? null : now), cancellationToken);
        if (updated > 0) {
            await database.Set<Conversation>()
            .Where(conversation => conversation.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.UpdatedAt, now), cancellationToken);
        }
        return updated > 0;
    }

    public async Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ToolApproval approval = new() {
            RunId = runId,
            ToolCallId = toolCallId,
            Status = "pending",
            CreatedAt = clock.GetUtcNow(),
        };
        database.Add(approval);
        try {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateException exception) when (SqliteErrors.IsUniqueViolation(exception)) {
            database.Entry(approval).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<ApprovalResolutionResult> ResolveApprovalAsync(
        long conversationId,
        string owner,
        string runId,
        string toolCallId,
        string decision,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        if (decision is not ("approve" or "deny")) {
            return ApprovalResolutionResult.Conflict;
        }

        string status = decision == "approve" ? "approved" : "denied";
        DateTimeOffset now = clock.GetUtcNow();
        int updated = await database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "AssistantToolApprovals"
                SET "Status" = {status}, "ResolvedAt" = {now}, "ResolvedBy" = {owner}
                WHERE "RunId" = {runId} AND "ToolCallId" = {toolCallId} AND "Status" = 'pending'
                    AND EXISTS (
                        SELECT 1 FROM "AssistantRuns" AS run
                        JOIN "AssistantConversations" AS conversation ON conversation."Id" = run."ConversationId"
                        WHERE run."Id" = "AssistantToolApprovals"."RunId"
                            AND run."ConversationId" = {conversationId}
                            AND run."Status" = 'awaiting_approval'
                            AND conversation."Owner" = {owner})
                """, cancellationToken);
        if (updated > 0) {
            return decision == "approve" ? ApprovalResolutionResult.Approved : ApprovalResolutionResult.Denied;
        }

        string? existing = await database.Set<ToolApproval>().AsNoTracking()
            .Where(approval => approval.RunId == runId && approval.ToolCallId == toolCallId
                && database.Set<Run>().Any(run => run.Id == approval.RunId
                    && run.ConversationId == conversationId
                    && database.Set<Conversation>().Any(conversation => conversation.Id == run.ConversationId
                        && conversation.Owner == owner)))
            .Select(approval => approval.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is null) {
            return ApprovalResolutionResult.NotFound;
        }
        if (existing == status) {
            return decision == "approve" ? ApprovalResolutionResult.Approved : ApprovalResolutionResult.Denied;
        }
        return ApprovalResolutionResult.Conflict;
    }

    public async Task<IReadOnlyList<ToolApproval>> GetPendingApprovalsAsync(
        long conversationId,
        string owner,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        string[] runIds = await database.Set<Run>().AsNoTracking()
            .Where(run => run.ConversationId == conversationId
                && database.Set<Conversation>().Any(conversation => conversation.Id == conversationId && conversation.Owner == owner))
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        if (runIds.Length == 0) {
            return [];
        }

        return await database.Set<ToolApproval>().AsNoTracking()
            .Where(approval => runIds.Contains(approval.RunId) && approval.Status == "pending")
            .ToArrayAsync(cancellationToken);
    }

    public Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        DateTimeOffset now = clock.GetUtcNow();
        return database.Set<ToolApproval>()
            .Where(approval => approval.RunId == runId && approval.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(approval => approval.Status, "failed")
                .SetProperty(approval => approval.ResolvedAt, now)
                .SetProperty(approval => approval.FailureCode, failureCode), cancellationToken);
    }

    public async Task<bool> RecordStepAsync(
        string runId,
        int toolCallCount,
        TokenUsage? usage,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentOutOfRangeException.ThrowIfNegative(toolCallCount);

        IQueryable<Run> run = database.Set<Run>().Where(item => item.Id == runId);
        int updated = usage is null
            ? await run.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.StepCount, item => item.StepCount + 1)
                .SetProperty(item => item.ToolCallCount, item => item.ToolCallCount + toolCallCount), cancellationToken)
            : await run.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.StepCount, item => item.StepCount + 1)
                .SetProperty(item => item.ToolCallCount, item => item.ToolCallCount + toolCallCount)
                .SetProperty(item => item.PromptTokens, item => usage.PromptTokens ?? item.PromptTokens)
                .SetProperty(item => item.CompletionTokens, item => usage.CompletionTokens ?? item.CompletionTokens), cancellationToken);
        return updated > 0;
    }

    public async Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) {
        Run[] interrupted = await database.Set<Run>().AsNoTracking()
            .Where(run => run.Status == "queued" || run.Status == "running" || run.Status == "awaiting_approval")
            .ToArrayAsync(cancellationToken);

        foreach (Run run in interrupted) {
            await SetRunStatusAsync(run.Id, "interrupted", "restart", "The host restarted before the run completed.", cancellationToken);
            await FailPendingApprovalsAsync(run.Id, "restart", cancellationToken);

            Message[] assistantCalls = await database.Set<Message>().AsNoTracking()
                .Where(message => message.RunId == run.Id && message.Role == "assistant" && message.ToolCallsJson != null)
                .ToArrayAsync(cancellationToken);
            HashSet<string> answered = await database.Set<Message>().AsNoTracking()
                .Where(message => message.RunId == run.Id && message.Role == "tool" && message.ToolCallId != null)
                .Select(message => message.ToolCallId!)
                .ToHashSetAsync(cancellationToken);

            foreach (Message assistant in assistantCalls) {
                using JsonDocument document = JsonDocument.Parse(assistant.ToolCallsJson!);
                foreach (JsonElement call in document.RootElement.EnumerateArray()) {
                    string? id = call.TryGetProperty("id", out JsonElement idValue) ? idValue.GetString() : null;
                    string toolCallId = id ?? $"recovery-{assistant.Sequence}-{answered.Count}";
                    if (!answered.Add(toolCallId)) {
                        continue;
                    }
                    string? name = call.TryGetProperty("function", out JsonElement function)
                        && function.TryGetProperty("name", out JsonElement nameValue)
                            ? nameValue.GetString()
                            : null;
                    await AppendMessageAsync(
                        run.ConversationId,
                        run.Id,
                        new MessageInput(
                            "tool",
                            "{\"ok\":false,\"data\":null,\"error\":\"The host restarted before this tool call completed; it was not executed.\"}",
                            ToolCallId: toolCallId,
                            ToolName: name ?? "tool"),
                        cancellationToken);
                }
            }
        }

        return interrupted.Length;
    }

    // Every read is scoped to the owner, so a conversation that belongs to somebody else is reported
    // as missing rather than forbidden.
    private Task<bool> OwnsConversationAsync(long conversationId, string owner, CancellationToken cancellationToken) =>
        database.Set<Conversation>().AnyAsync(
            conversation => conversation.Id == conversationId && conversation.Owner == owner,
            cancellationToken);

    private static void AddParameter(DbCommand command, string name, object value) {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch {
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => value,
        };
        command.Parameters.Add(parameter);
    }
}
