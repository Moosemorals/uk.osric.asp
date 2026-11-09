// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Storage;

/// <summary>The SQLite-backed memory store: two tables, one FTS5 index, every statement owner-scoped.</summary>
/// <remarks>
/// The writes that have to be atomic are atomic in SQLite rather than in this class: the cap and the
/// insert share a transaction, and so do the version check, the history row and the new head. Recall
/// spends its money on the FTS5 query and filters the owner beside it, so an index search never sees
/// another account's text.
/// </remarks>
internal sealed class SqliteMemoryStore(
    StorageDbContext database,
    AssistantOptions options,
    TimeProvider clock) : IMemoryStore {

    private const string SelectColumns = """
        memory."Id", memory."Kind", memory."Content", memory."Version", memory."CreatedAt",
        memory."LastRecalledAt", memory."RecallCount", memory."Pinned"
        """;

    public async Task<IReadOnlyList<MemoryCandidate>> RecallAsync(
        string owner,
        string queryTerms,
        int limit,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(queryTerms);

        if (queryTerms.Length == 0 || limit <= 0) {
            return [];
        }

        // The FTS5 table is external content, so the row it ranks is read from AssistantMemories and the
        // owner filter sits beside the match rather than after it.
        return await QueryAsync($"""
            SELECT {SelectColumns}
            FROM "AssistantMemorySearch"
            JOIN "AssistantMemories" AS memory ON memory."Id" = "AssistantMemorySearch"."rowid"
            WHERE "AssistantMemorySearch" MATCH $terms AND memory."Owner" = $owner
            ORDER BY bm25("AssistantMemorySearch")
            LIMIT $limit;
            """,
            command => {
                AddParameter(command, "$terms", queryTerms);
                AddParameter(command, "$owner", owner);
                AddParameter(command, "$limit", limit);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryCandidate>> StickyAsync(
        string owner,
        long conversationId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return await QueryAsync($"""
            SELECT {SelectColumns}
            FROM "AssistantMemories" AS memory
            WHERE memory."Owner" = $owner AND memory."LastRecalledConversationId" = $conversation
            ORDER BY memory."LastRecalledAt" DESC, memory."Id" DESC
            LIMIT $limit;
            """,
            command => {
                AddParameter(command, "$owner", owner);
                AddParameter(command, "$conversation", conversationId);
                AddParameter(command, "$limit", options.MaxMemoriesPerRun);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryCandidate>> PinnedAsync(
        string owner,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return await QueryAsync($"""
            SELECT {SelectColumns}
            FROM "AssistantMemories" AS memory
            WHERE memory."Owner" = $owner AND memory."Pinned" = 1
            ORDER BY memory."UpdatedAt" DESC, memory."Id" DESC;
            """,
            command => AddParameter(command, "$owner", owner),
            cancellationToken);
    }

    public async Task<MemoryWriteResult> RememberAsync(
        string owner,
        MemoryKind kind,
        string content,
        MemorySource source,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(source);

        MemoryContentResult normalised = Normalise(content);
        if (!normalised.IsSuccess) {
            return MemoryWriteResult.Refused(normalised.Error!);
        }
        if (MemorySecrets.LooksLikeSecret(normalised.Content) is { } rule) {
            return MemoryWriteResult.Refused(
                $"A memory that looks like {rule} is refused. Say where the secret lives instead of what it is.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // The owner's rows are read once, for both rules: a duplicate is the same note said twice, and
        // the count is the cap. The transaction is what stops two runs both passing the check.
        MemoryEntry[] held = await database.Set<MemoryEntry>()
            .Where(memory => memory.Owner == owner)
            .ToArrayAsync(cancellationToken);
        if (held.FirstOrDefault(memory => MemoryContent.SameNote(memory.Content, normalised.Content)) is { } duplicate) {
            await transaction.RollbackAsync(cancellationToken);
            return MemoryWriteResult.Stored(ToCandidate(duplicate));
        }
        if (held.Length >= options.MaxMemoriesPerOwner) {
            await transaction.RollbackAsync(cancellationToken);
            return MemoryWriteResult.Refused(
                $"The owner already holds {held.Length.ToString(CultureInfo.InvariantCulture)} memories, which is the cap (Assistant:MaxMemoriesPerOwner). Forget one with forget_memory, or update one that is out of date.");
        }

        MemoryEntry entry = new() {
            Owner = owner,
            Kind = MemoryKinds.Name(kind),
            Content = normalised.Content,
            Version = 1,
            Pinned = false,
            SourceConversationId = source.ConversationId,
            SourceRunId = source.RunId,
            CreatedAt = now,
            UpdatedAt = now,
            RecallCount = 0,
        };
        database.Add(entry);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MemoryWriteResult.Stored(ToCandidate(entry));
    }

    public async Task<MemoryWriteResult> UpdateAsync(
        string owner,
        long id,
        int expectedVersion,
        string content,
        MemorySource source,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(source);

        MemoryContentResult normalised = Normalise(content);
        if (!normalised.IsSuccess) {
            return MemoryWriteResult.Refused(normalised.Error!);
        }
        if (MemorySecrets.LooksLikeSecret(normalised.Content) is { } rule) {
            return MemoryWriteResult.Refused(
                $"A memory that looks like {rule} is refused. Say where the secret lives instead of what it is.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        MemoryEntry? entry = await database.Set<MemoryEntry>()
            .SingleOrDefaultAsync(memory => memory.Id == id && memory.Owner == owner, cancellationToken);
        if (entry is null) {
            await transaction.RollbackAsync(cancellationToken);
            return MemoryWriteResult.Refused($"No memory with id {id.ToString(CultureInfo.InvariantCulture)}.");
        }
        if (entry.Version != expectedVersion) {
            await transaction.RollbackAsync(cancellationToken);
            return MemoryWriteResult.Refused(
                $"Memory {id.ToString(CultureInfo.InvariantCulture)} is at version {entry.Version.ToString(CultureInfo.InvariantCulture)}, not {expectedVersion.ToString(CultureInfo.InvariantCulture)}. It now reads: {entry.Content}");
        }

        // The version being replaced is kept as history before the head moves, so the trail is never
        // missing the version an update skipped over.
        database.Add(new MemoryEdit {
            MemoryId = entry.Id,
            Version = entry.Version,
            Kind = entry.Kind,
            Content = entry.Content,
            EditedAt = now,
            SourceConversationId = entry.SourceConversationId,
        });
        entry.Content = normalised.Content;
        entry.Version++;
        entry.UpdatedAt = now;
        entry.SourceConversationId = source.ConversationId;
        entry.SourceRunId = source.RunId;

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MemoryWriteResult.Stored(ToCandidate(entry));
    }

    public async Task<bool> ForgetAsync(string owner, long id, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        // The history goes with the head through the foreign key, and the delete trigger takes the
        // index row with it: one statement, and nothing left saying what was forgotten.
        int deleted = await database.Set<MemoryEntry>()
            .Where(memory => memory.Id == id && memory.Owner == owner)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task TouchAsync(
        string owner,
        IReadOnlyList<long> ids,
        long conversationId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0) {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        long[] touched = [.. ids];

        // One statement for the batch, and the index is untouched: the update trigger only fires for
        // the three columns it names.
        _ = await database.Set<MemoryEntry>()
            .Where(memory => memory.Owner == owner && touched.Contains(memory.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(memory => memory.LastRecalledAt, now)
                .SetProperty(memory => memory.LastRecalledConversationId, conversationId)
                .SetProperty(memory => memory.RecallCount, memory => memory.RecallCount + 1), cancellationToken);
    }

    public Task<int> CountAsync(string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return database.Set<MemoryEntry>().CountAsync(memory => memory.Owner == owner, cancellationToken);
    }

    public Task<int> ClearProvenanceAsync(long conversationId, CancellationToken cancellationToken = default) =>
        database.Set<MemoryEntry>()
            .Where(memory => memory.SourceConversationId == conversationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(memory => memory.SourceConversationId, (long?)null)
                .SetProperty(memory => memory.SourceRunId, (string?)null), cancellationToken);

    public Task<int> DeleteForOwnerAsync(string owner, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return database.Set<MemoryEntry>()
            .Where(memory => memory.Owner == owner)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private MemoryContentResult Normalise(string content) => MemoryContent.Normalise(content, options.MaxMemoryChars);

    private static MemoryCandidate ToCandidate(MemoryEntry entry) => new(
        entry.Id,
        MemoryKinds.Parse(entry.Kind),
        entry.Content,
        entry.Version,
        entry.UpdatedAt,
        entry.LastRecalledAt,
        entry.RecallCount,
        entry.Pinned);

    // A raw command because a virtual table is not a mapped type: it is read through the context's own
    // connection, which is opened for the read and closed again when this call opened it.
    private async Task<IReadOnlyList<MemoryCandidate>> QueryAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) {
        DbConnection connection = database.Database.GetDbConnection();
        bool opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) {
            await database.Database.OpenConnectionAsync(cancellationToken);
        }

        try {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<MemoryCandidate> memories = [];
            while (await reader.ReadAsync(cancellationToken)) {
                memories.Add(new MemoryCandidate(
                    reader.GetInt64(0),
                    MemoryKinds.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    Instant(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Instant(reader.GetString(5)),
                    reader.GetInt32(6),
                    reader.GetBoolean(7)));
            }

            return memories;
        } finally {
            if (opened) {
                await database.Database.CloseConnectionAsync();
            }
        }
    }

    // Timestamps are stored as TEXT, exactly as every other table stores them.
    private static DateTimeOffset Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
