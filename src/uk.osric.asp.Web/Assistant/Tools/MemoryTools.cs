// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;

/// <summary>Stores something durable the owner has said about themselves or the site.</summary>
internal sealed class RememberTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "remember",
        "Store something durable the owner has told you about themselves, this site or how they want "
        + "it run: a preference, a fact, a correction, or where something lives. Not page source, not "
        + "tool output, not this task, and never a secret. Search first, and update a memory rather than "
        + "adding a second one that contradicts it.",
        true,
        [new("kind", ToolArgumentType.String, true), new("content", ToolArgumentType.String, true)]) { Verb = "Remember" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string kindText = values.RequiredString("kind");
        if (!MemoryKinds.TryParse(kindText, out MemoryKind kind)) {
            return MemoryTools.RefusedWrite(services, "remember", Spec.Verb, kind: null,
                $"The kind must be one of {string.Join(", ", MemoryKinds.All.Select(MemoryKinds.Name))}.");
        }

        MemoryWriteResult written = await services.Memories.RememberAsync(
            services.Context.Owner, kind, values.RequiredString("content"), services.Context.Source, cancellationToken);
        return MemoryTools.Result(services, written, "remember", kind, Spec.Verb, $"Remembered a {MemoryKinds.Name(kind)}");
    }
}

/// <summary>Replaces a memory's content when it is out of date.</summary>
internal sealed class UpdateMemoryTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "update_memory",
        "Replace a memory's content when it is out of date or wrong. Carry the version the memory was "
        + "read at: a stale version is refused with the current content, so read it again rather than "
        + "guessing. The kind cannot be changed here.",
        true,
        [
            new("id", ToolArgumentType.Integer, true),
            new("expectedVersion", ToolArgumentType.Integer, true),
            new("content", ToolArgumentType.String, true),
        ]) { Verb = "Update memory" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        long id = values.RequiredInteger("id");
        MemoryWriteResult written = await services.Memories.UpdateAsync(
            services.Context.Owner,
            id,
            values.RequiredInteger("expectedVersion"),
            values.RequiredString("content"),
            services.Context.Source,
            cancellationToken);
        return MemoryTools.Result(services, written, "update", kind: null, Spec.Verb, $"Updated memory {id}");
    }
}

/// <summary>Deletes a memory and its history.</summary>
internal sealed class ForgetMemoryTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "forget_memory",
        "Delete a memory and its history. There is no undo: the owner's page is the other way to do "
        + "this, and forgetting is the only way a memory stops being recalled.",
        true,
        [new("id", ToolArgumentType.Integer, true)]) { Verb = "Forget memory" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        long id = values.RequiredInteger("id");
        bool forgotten = await services.Memories.ForgetAsync(services.Context.Owner, id, cancellationToken);
        if (!forgotten) {
            services.Metrics.RecordMemoryWrite("forget", kind: null, success: false);
            return services.Results.Failure($"No memory with id {id}.", $"{Spec.Verb} {id}: not found");
        }

        services.Metrics.RecordMemoryWrite("forget", kind: null, success: true);
        return services.Results.Success(new { id }, $"Forgot memory {id}");
    }
}

/// <summary>Searches the owner's memories by keyword.</summary>
internal sealed class SearchMemoriesTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "search_memories",
        "Search the owner's memories by keyword. This is the escape hatch when what you remember does "
        + "not cover the current turn: search before saying you do not know.",
        false,
        [new("query", ToolArgumentType.String, true), new("limit", ToolArgumentType.Integer, false)]) { Verb = "Search memories" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string query = values.RequiredString("query");
        string terms = MemoryQuery.Terms(query, services.Options.MaxMemoryQueryTerms);
        if (terms.Length == 0) {
            return services.Results.Failure("The query has no word long enough to search for.", "Searched memories: nothing to search for");
        }

        // The limit is the caller's, bounded by the configured maximum rather than trusted.
        int limit = Math.Clamp(values.Integer("limit") ?? services.Options.MaxMemorySearchResults, 1, services.Options.MaxMemorySearchResults);
        IReadOnlyList<MemoryCandidate> found = await services.Memories.RecallAsync(services.Context.Owner, terms, limit, cancellationToken);
        return services.Results.Success(new { query, memories = found.Select(MemoryTools.Data).ToArray() }, $"Searched memories ({found.Count} found)");
    }
}

/// <summary>What the memory tools share: the metric and the shape of a memory.</summary>
internal static class MemoryTools {

    // A write is a write whatever it did, so the counter records the outcome and, for a refusal, no
    // kind at all: the kind a refused call named may be the very thing that was wrong with it.
    public static ToolExecutionResult Result(
        ToolServices services,
        MemoryWriteResult written,
        string operation,
        MemoryKind? kind,
        string verb,
        string summary) {
        services.Metrics.RecordMemoryWrite(operation, written.IsSuccess ? kind : null, written.IsSuccess);
        return written.IsSuccess
            ? services.Results.Success(Data(written.Memory!), summary)
            : services.Results.Failure(written.Error!, $"{verb} refused");
    }

    public static ToolExecutionResult RefusedWrite(
        ToolServices services,
        string operation,
        string verb,
        MemoryKind? kind,
        string error) {
        services.Metrics.RecordMemoryWrite(operation, kind, success: false);
        return services.Results.Failure(error, $"{verb} refused");
    }

    public static object Data(MemoryCandidate memory) => new {
        id = memory.Id,
        kind = MemoryKinds.Name(memory.Kind),
        version = memory.Version,
        content = memory.Content,
        written = memory.Written,
        used = memory.RecallCount,
    };
}
