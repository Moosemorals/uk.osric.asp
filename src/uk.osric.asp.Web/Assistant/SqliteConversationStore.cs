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
internal sealed class SqliteConversationStore(
    StorageDbContext database,
    TimeProvider clock,
    ILogger<SqliteConversationStore> logger) : IConversationStore {

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
            .FromSql($"SELECT * FROM \"AssistantConversations\" WHERE \"Owner\" = {owner} ORDER BY \"UpdatedAt\" DESC, \"Id\" DESC")
            .AsNoTracking()
            .Select(conversation => new ConversationListItem(
                conversation.Id,
                conversation.Title,
                conversation.ApprovalMode,
                conversation.UpdatedAt,
                database.Set<Run>().Where(run => run.ConversationId == conversation.Id
                        && (run.Status == RunStatus.Queued || run.Status == RunStatus.Running || run.Status == RunStatus.AwaitingOwner))
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

        // A memory outlives the conversation that wrote it, so the delete drops the provenance rather
        // than the memory: the rows keep their content and say where they came from no longer. Both
        // statements are one transaction, so a delete that turns out not to be this owner's changes
        // nothing at all.
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        _ = await database.Set<MemoryEntry>()
            .Where(memory => memory.SourceConversationId == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(memory => memory.SourceConversationId, (long?)null)
                .SetProperty(memory => memory.SourceRunId, (string?)null), cancellationToken);
        int deleted = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == id && conversation.Owner == owner)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // A completion time is written only when a run stops; a run waiting on its owner has not completed.
    private static bool IsRunning(string status) => RunStatus.IsActive(status);

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
        update.AddParameter("$now", now);
        update.AddParameter("$defaultTitle", ConversationTitle.NewConversation);
        update.AddParameter("$title", ConversationTitle.From(content));
        update.AddParameter("$id", id);
        update.AddParameter("$owner", owner);

        await using DbDataReader reader = await update.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) {
            await transaction.RollbackAsync(cancellationToken);
            return new RunStartResult(RunStartStatus.NotFound, null, null);
        }

        long sequence = reader.GetInt64(0);
        string approvalMode = reader.GetString(1);
        await reader.DisposeAsync();

        // A conversation's content mixture is stable, so a previous run's calibration is a better
        // first estimate than the conservative default. A different model may tokenise differently,
        // so only a run of the same model is copied. Any matching run will do; SQLite cannot order by
        // a DateTimeOffset, and the ratio is a property of the conversation's content rather than of
        // the moment it was measured.
        Run? previous = await database.Set<Run>().AsNoTracking()
            .Where(item => item.ConversationId == id && item.Model == model
                && item.RequestBytes != null && item.LastPromptTokens != null)
            .FirstOrDefaultAsync(cancellationToken);

        Run run = new() {
            Id = runId,
            ConversationId = id,
            Status = RunStatus.Queued,
            Model = model,
            ApprovalMode = approvalMode,
            PromptVersion = promptVersion,
            CreatedAt = now,
            RequestBytes = previous?.RequestBytes,
            LastPromptTokens = previous?.LastPromptTokens,
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
            // The unique index on an active run is the arbiter: two sends from one page, or a retry
            // after a slow first one, meet here and the second is told a run is already going.
            logger.LogDebug("Conversation {ConversationId} already has an active run, so {RunId} was not started", id, runId);
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
        update.AddParameter("$now", now);
        update.AddParameter("$id", conversationId);

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
        // The row's id, not its sequence: the live transcript fetches the stored message by id.
        return message.Id;
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

    public async Task<Message?> GetMessageAsync(
        long conversationId,
        string owner,
        long messageId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        bool owned = await OwnsConversationAsync(conversationId, owner, cancellationToken);
        if (!owned) {
            return null;
        }

        return await database.Set<Message>().AsNoTracking()
            .FirstOrDefaultAsync(
                message => message.Id == messageId && message.ConversationId == conversationId,
                cancellationToken);
    }

    public async Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        bool owned = await OwnsConversationAsync(id, owner, cancellationToken);
        return owned
            ? await database.Set<Run>().AsNoTracking()
                .Where(run => run.ConversationId == id
                && (run.Status == RunStatus.Queued || run.Status == RunStatus.Running || run.Status == RunStatus.AwaitingOwner))
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
                .SetProperty(run => run.StartedAt, run => run.StartedAt ?? (status == RunStatus.Running ? now : null))
                .SetProperty(run => run.CompletedAt, IsRunning(status) ? null : now), cancellationToken);
        if (updated > 0) {
            await database.Set<Conversation>()
            .Where(conversation => conversation.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.UpdatedAt, now), cancellationToken);
        }
        return updated > 0;
    }

    public async Task<bool> OpenInterruptAsync(
        string runId,
        string kind,
        string? toolCallId,
        string requestJson,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(requestJson);

        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        long? conversationId = await database.Set<Run>()
            .Where(run => run.Id == runId)
            .Select(run => (long?)run.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (conversationId is null) {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // The status moves with the record, in one transaction: a run whose interrupt is open is a run
        // the owner is being waited on, and there is no state in between for a reload to find.
        int updated = await database.Set<Run>()
            .Where(run => run.Id == runId && run.Status == RunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, RunStatus.AwaitingOwner)
                .SetProperty(run => run.ErrorCode, (string?)null)
                .SetProperty(run => run.ErrorMessage, (string?)null)
                .SetProperty(run => run.CompletedAt, (DateTimeOffset?)null), cancellationToken);
        if (updated == 0) {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        OwnerInterrupt interrupt = new() {
            RunId = runId,
            Kind = kind,
            Status = OwnerInterruptStatus.Pending,
            ToolCallId = toolCallId,
            RequestJson = requestJson,
            CreatedAt = now,
        };
        database.Add(interrupt);
        try {
            await database.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateException exception) when (SqliteErrors.IsUniqueViolation(exception)) {
            // A run waits on one thing at a time, so a second interrupt for it is the caller's defect,
            // not a second question: the key refuses it and the run is left as it was.
            logger.LogDebug("Run {RunId} already has an interrupt", runId);
            database.Entry(interrupt).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await database.Set<Conversation>()
            .Where(conversation => conversation.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<OwnerInterrupt?> GetInterruptAsync(string runId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return database.Set<OwnerInterrupt>().AsNoTracking()
            .SingleOrDefaultAsync(interrupt => interrupt.RunId == runId, cancellationToken);
    }

    public async Task<OwnerInterrupt?> GetPendingInterruptAsync(
        long conversationId,
        string owner,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!await OwnsConversationAsync(conversationId, owner, cancellationToken)) {
            return null;
        }

        return await database.Set<OwnerInterrupt>().AsNoTracking()
            .Where(interrupt => interrupt.Status == OwnerInterruptStatus.Pending
                && database.Set<Run>().Any(run => run.Id == interrupt.RunId && run.ConversationId == conversationId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<InterruptResolutionResult> ResolveInterruptAsync(
        long conversationId,
        string owner,
        string runId,
        string kind,
        string? toolCallId,
        string resolutionJson,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(resolutionJson);

        if (!await OwnsConversationAsync(conversationId, owner, cancellationToken)) {
            return InterruptResolutionResult.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // The kind and the call the caller read are part of the condition, so a form built for one
        // wait cannot resolve another that happens to be pending on the same run.
        int resolved = await database.Database.ExecuteSqlAsync($"""
                UPDATE "AssistantInterrupts"
                SET "Status" = {OwnerInterruptStatus.Resolved}, "ResolutionJson" = {resolutionJson},
                    "ResolvedAt" = {now}, "ResolvedBy" = {owner}
                WHERE "RunId" = {runId} AND "Status" = {OwnerInterruptStatus.Pending}
                    AND "Kind" = {kind}
                    AND "ToolCallId" IS {toolCallId}
                    AND EXISTS (
                        SELECT 1 FROM "AssistantRuns" AS run
                        WHERE run."Id" = "AssistantInterrupts"."RunId"
                            AND run."ConversationId" = {conversationId}
                            AND run."Status" = {RunStatus.AwaitingOwner})
                """, cancellationToken);
        if (resolved == 0) {
            await transaction.RollbackAsync(cancellationToken);
            OwnerInterrupt? existing = await database.Set<OwnerInterrupt>().AsNoTracking()
                .SingleOrDefaultAsync(interrupt => interrupt.RunId == runId
                    && database.Set<Run>().Any(run => run.Id == interrupt.RunId && run.ConversationId == conversationId),
                    cancellationToken);
            if (existing is null) {
                return InterruptResolutionResult.NotFound;
            }

            // The same answer arriving twice is one answer: a double click is not a conflict. A
            // different answer to a settled question is.
            return existing.Kind == kind
                && string.Equals(existing.ToolCallId, toolCallId, StringComparison.Ordinal)
                && existing.Status == OwnerInterruptStatus.Resolved
                && string.Equals(existing.ResolutionJson, resolutionJson, StringComparison.Ordinal)
                ? InterruptResolutionResult.Resolved
                : InterruptResolutionResult.Conflict;
        }

        _ = await database.Set<Run>()
            .Where(run => run.Id == runId && run.ConversationId == conversationId && run.Status == RunStatus.AwaitingOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, RunStatus.Running)
                .SetProperty(run => run.CompletedAt, (DateTimeOffset?)null), cancellationToken);
        _ = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InterruptResolutionResult.Resolved;
    }

    public async Task<bool> ContinueRunAsync(
        long conversationId,
        string owner,
        string runId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (!await OwnsConversationAsync(conversationId, owner, cancellationToken)) {
            return false;
        }

        // What continuing buys is what the owner was shown, not what the configuration says now: a
        // deploy between the pause and the answer cannot change an answer already on the page.
        OwnerInterrupt? pending = await database.Set<OwnerInterrupt>().AsNoTracking()
            .SingleOrDefaultAsync(interrupt => interrupt.RunId == runId
                && interrupt.Status == OwnerInterruptStatus.Pending
                && interrupt.Kind == OwnerInterruptKind.Continuation, cancellationToken);
        if (pending is null || InterruptPayloads.Continuation(pending.RequestJson) is not { } request) {
            return false;
        }

        DateTimeOffset now = clock.GetUtcNow();
        string resolution = InterruptPayloads.Write(new OwnerResolution("continue"));
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        int resolved = await database.Database.ExecuteSqlAsync($"""
                UPDATE "AssistantInterrupts"
                SET "Status" = {OwnerInterruptStatus.Resolved}, "ResolutionJson" = {resolution},
                    "ResolvedAt" = {now}, "ResolvedBy" = {owner}
                WHERE "RunId" = {runId} AND "Status" = {OwnerInterruptStatus.Pending}
                    AND "Kind" = {OwnerInterruptKind.Continuation}
                """, cancellationToken);
        if (resolved == 0) {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // Every right-hand side reads the row as it was, so the budget that ran out is widened by the
        // window the owner was shown and the other keeps its value, whichever order the columns are
        // set in. The per-step budget is not widened: a continuation cannot make one step larger.
        int updated = await database.Set<Run>()
            .Where(run => run.Id == runId
                && run.ConversationId == conversationId
                && run.Status == RunStatus.AwaitingOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, RunStatus.Running)
                .SetProperty(run => run.StepLimit, run => request.Budget == RunBudget.Steps
                    ? (run.StepLimit ?? request.Window) + request.Window
                    : run.StepLimit)
                .SetProperty(run => run.ToolCallLimit, run => request.Budget == RunBudget.ToolCalls
                    ? (run.ToolCallLimit ?? request.Window) + request.Window
                    : run.ToolCallLimit)
                .SetProperty(run => run.CompletedAt, (DateTimeOffset?)null), cancellationToken);
        if (updated == 0) {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        _ = await database.Set<Conversation>()
            .Where(conversation => conversation.Id == conversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<int> FailPendingInterruptAsync(string runId, string failureCode, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        DateTimeOffset now = clock.GetUtcNow();
        return database.Set<OwnerInterrupt>()
            .Where(interrupt => interrupt.RunId == runId && interrupt.Status == OwnerInterruptStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(interrupt => interrupt.Status, OwnerInterruptStatus.Failed)
                .SetProperty(interrupt => interrupt.ResolvedAt, now)
                .SetProperty(interrupt => interrupt.FailureCode, failureCode), cancellationToken);
    }

    public async Task<bool> RecordStepAsync(
        string runId,
        long requestBytes,
        long estimatedTokens,
        int maxTokens,
        TokenUsage? usage,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);

        IQueryable<Run> run = database.Set<Run>().Where(item => item.Id == runId);
        int updated = usage is null
            ? await run.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.StepCount, item => item.StepCount + 1)
                .SetProperty(item => item.EstimatedTokens, item => estimatedTokens)
                .SetProperty(item => item.MaxTokens, item => maxTokens), cancellationToken)
            : await run.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.StepCount, item => item.StepCount + 1)
                .SetProperty(item => item.EstimatedTokens, item => estimatedTokens)
                .SetProperty(item => item.MaxTokens, item => maxTokens)
                .SetProperty(item => item.PromptTokens, item => (item.PromptTokens ?? 0) + (usage.PromptTokens ?? 0))
                .SetProperty(item => item.CompletionTokens, item => (item.CompletionTokens ?? 0) + (usage.CompletionTokens ?? 0))
                .SetProperty(item => item.CachedTokens, item => (item.CachedTokens ?? 0) + (usage.CachedTokens ?? 0))
                .SetProperty(item => item.ReasoningTokens, item => (item.ReasoningTokens ?? 0) + (usage.ReasoningTokens ?? 0))
                .SetProperty(item => item.OutputTokens, item => usage.CompletionTokens ?? item.OutputTokens)
                .SetProperty(item => item.RequestBytes, item => requestBytes)
                .SetProperty(item => item.LastPromptTokens, item => usage.PromptTokens ?? item.LastPromptTokens), cancellationToken);
        return updated > 0;
    }

    public async Task<bool> RecordToolCallAsync(string runId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        int updated = await database.Set<Run>()
            .Where(item => item.Id == runId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ToolCallCount, item => item.ToolCallCount + 1),
                cancellationToken);
        return updated > 0;
    }

    public async Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) {
        Run[] interrupted = await database.Set<Run>().AsNoTracking()
            .Where(run => run.Status == RunStatus.Queued || run.Status == RunStatus.Running || run.Status == RunStatus.AwaitingOwner)
            .ToArrayAsync(cancellationToken);

        foreach (Run run in interrupted) {
            await SetRunStatusAsync(run.Id, RunStatus.Interrupted, "restart", "The host restarted before the run completed.", cancellationToken);
            await FailPendingInterruptAsync(run.Id, "restart", cancellationToken);

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
}
