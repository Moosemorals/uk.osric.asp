// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Lists a page's retained revisions, newest first.</summary>
internal sealed class ListRevisionsTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "list_revisions",
        "List a page's retained revisions, newest first: each hash, when it was saved, the runtime that created it, "
        + "its compile state and whether it is the page's current revision. A deleted page's history is listed too, "
        + "which is what answers \"what did this page hold before it was deleted?\". `limit` (1-500, default 50) bounds "
        + "the rows and `truncated` says there are more, so pass `before` with the last hash you were given to read the "
        + "next, older slice. Read-only: it changes nothing and needs no approval.",
        false,
        [
            new("path", ToolArgumentType.String, true),
            new("limit", ToolArgumentType.Integer, false),
            new("before", ToolArgumentType.String, false),
        ]) { Verb = "List" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? resolved, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        PagePath path = resolved!;
        if (ToolPaths.RequirePageSource(Spec, path, "lists retained revisions", services) is { } noSource) {
            return noSource;
        }

        if (!PageToolData.TryReadLimit(values, out int limit)) {
            return services.Results.Failure(PageToolData.LimitRefusal, $"{Spec.Verb} refused");
        }

        string? beforeText = values.String("before");
        if (beforeText is not null && !RevisionHash.IsCanonical(beforeText)) {
            return services.Results.Failure(PageToolData.HashRefusal, "Invalid revision hash");
        }

        if (await services.Pages.GetHeadAsync(path, cancellationToken) is not { } head) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path)} was not found.", $"{Spec.Verb} {ToolPaths.Describe(path)}: not found");
        }

        IReadOnlyList<PageRevisionSummary> revisions = await services.Pages.ListRevisionsAsync(path, cancellationToken);
        int start = 0;
        if (beforeText is not null) {
            int index = IndexOf(revisions, beforeText);
            if (index < 0) {
                return services.Results.Failure(
                    $"That hash is not a revision of {ToolPaths.Describe(path)}. Use a hash list_revisions returned.",
                    "Revision not found");
            }
            start = index + 1;
        }

        List<PageRevisionSummary> page = [.. revisions.Skip(start)];
        bool truncated = page.Count > limit;
        object data = new {
            path = path.Key,
            deleted = head.IsDeleted,
            revisions = page.Take(limit).Select(revision => new {
                hash = revision.Hash,
                savedAt = revision.SavedAt,
                creationRuntimeId = revision.CreationRuntimeId,
                imageState = revision.ImageState.Describe(),
                isCurrent = revision.IsCurrent,
                editorUrl = PageUrls.EditorRevision(path, revision.Hash),
            }),
            truncated,
        };
        return services.Results.Success(data, $"Listed {Math.Min(page.Count, limit)} revisions of {ToolPaths.Describe(path)}");
    }

    private static int IndexOf(IReadOnlyList<PageRevisionSummary> revisions, string hash) {
        for (int index = 0; index < revisions.Count; index++) {
            if (string.Equals(revisions[index].Hash, hash, StringComparison.Ordinal)) {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>Reads one retained revision's stored source, by line range.</summary>
internal sealed class ReadRevisionTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "read_revision",
        "Read one retained revision's stored template source, by optional 1-based line range. Get a hash from "
        + "list_revisions first. This returns the source as it was saved, not what it rendered: it compiles nothing "
        + "and runs nothing. Reading a revision sends its source to the provider, exactly as read_page does. "
        + "Read-only: it changes nothing and needs no approval.",
        false,
        [
            new("path", ToolArgumentType.String, true),
            new("hash", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, false),
            new("endLine", ToolArgumentType.Integer, false),
        ]) { Verb = "Read" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? resolved, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        PagePath path = resolved!;
        if (ToolPaths.RequirePageSource(Spec, path, "reads retained revisions", services) is { } noSource) {
            return noSource;
        }

        string hash = values.RequiredString("hash");
        if (!RevisionHash.IsCanonical(hash)) {
            return services.Results.Failure(PageToolData.HashRefusal, "Invalid revision hash");
        }

        if (await services.Pages.GetHeadAsync(path, cancellationToken) is null) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path)} was not found.", $"{Spec.Verb} {ToolPaths.Describe(path)}: not found");
        }

        PageRevisionTemplate? revision = await services.Pages.GetRevisionTemplateByHashAsync(hash, cancellationToken);
        if (revision is null || revision.Path != path) {
            return services.Results.Failure(
                $"That hash is not a revision of {ToolPaths.Describe(path)}. Use a hash list_revisions returned.",
                "Revision not found");
        }

        PageToolData.ProjectedSource? projected = PageToolData.ProjectSource(
            revision.Template, values.Integer("startLine"), values.Integer("endLine"), services.Options);
        if (projected is null) {
            return services.Results.Failure("The requested line range is outside the revision.", $"{Spec.Verb} {ToolPaths.Describe(path)}: invalid range");
        }

        object data = new {
            path = path.Key,
            hash = revision.Hash,
            savedAt = revision.SavedAt,
            creationRuntimeId = revision.CreationRuntimeId,
            imageState = revision.ImageState.Describe(),
            startLine = projected.StartLine,
            endLine = projected.EndLine,
            totalLines = projected.TotalLines,
            truncated = projected.Truncated,
            source = projected.Text,
            editorUrl = PageUrls.EditorRevision(path, revision.Hash),
        };
        return services.Results.Success(data, $"{Spec.Verb} revision {ShortHash(revision.Hash)} of {ToolPaths.Describe(path)}");
    }

    internal static string ShortHash(string hash) => hash[..Math.Min(12, hash.Length)];
}

/// <summary>Restores one retained revision as a new revision of the page.</summary>
internal sealed class RestoreRevisionTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "restore_revision",
        "Restore a retained revision by saving its stored source as a new revision of the page, through the same "
        + "compile-and-save path an edit uses. Nothing is rewound: the old revision stays, and a deleted page comes "
        + "back with its history. Refused when the chosen revision already has the source the live page holds, when "
        + "the hash is not a revision of that path, for a shell file and for a missing page. A shell file and a locked "
        + "page are refused. This writes a page and needs approval.",
        true,
        [new("path", ToolArgumentType.String, true), new("hash", ToolArgumentType.String, true)]) {
        Verb = "Restore",
        Writes = true,
        PathArgument = "path",
    };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? resolved, ToolExecutionResult? refusal) = ToolPaths.ResolveWrite(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        PagePath path = resolved!;
        string hash = values.RequiredString("hash");
        if (!RevisionHash.IsCanonical(hash)) {
            return services.Results.Failure(PageToolData.HashRefusal, "Invalid revision hash");
        }

        if (await services.Pages.GetHeadAsync(path, cancellationToken) is null) {
            return services.Results.Failure(
                $"Page {ToolPaths.Describe(path)} was not found, so it has no revisions to restore. Use write_page to create it.",
                $"{Spec.Verb} {ToolPaths.Describe(path)}: not found");
        }

        PageRevisionTemplate? revision = await services.Pages.GetRevisionTemplateByHashAsync(hash, cancellationToken);
        if (revision is null || revision.Path != path) {
            return services.Results.Failure(
                $"That hash is not a revision of {ToolPaths.Describe(path)}. Use a hash list_revisions returned.",
                "Revision not found");
        }

        PageTemplate? live = await services.Pages.GetAsync(path, cancellationToken);
        if (live is not null && string.Equals(live.Template, revision.Template, StringComparison.Ordinal)) {
            return services.Results.Failure(
                $"{ReadRevisionTool.ShortHash(revision.Hash)} has the source {ToolPaths.Describe(path)} already holds, so restoring it would change nothing. Choose a different revision, or edit the page.",
                $"{Spec.Verb} {ToolPaths.Describe(path)}: nothing to restore");
        }

        SavedRevision saved = await services.Pages.SaveAsync(path, revision.Template, cancellationToken);
        object data = new {
            path = path.Key,
            restoredFrom = revision.Hash,
            revisionHash = saved.Revision.Hash,
            diagnostics = PageToolData.DiagnosticData(saved.Diagnostics),
        };
        return services.Results.Success(data,
            $"Restored revision {ReadRevisionTool.ShortHash(revision.Hash)} of {ToolPaths.Describe(path)}");
    }
}
