// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Diff;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Compares two retained revisions of a page and returns a unified diff.</summary>
internal sealed class DiffPageTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "diff_page",
        "Compare two retained revisions of a page and return a unified diff, the two revision "
        + "hashes and the editor link. With no hashes, compares the current revision with the one "
        + "before it. Read-only: it changes nothing and does not need approval.",
        false,
        [
            new("path", ToolArgumentType.String, true),
            new("fromHash", ToolArgumentType.String, false),
            new("toHash", ToolArgumentType.String, false),
        ]) { Verb = "Diff" };

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

        string? fromHash = values.String("fromHash");
        string? toHash = values.String("toHash");
        if ((fromHash is not null && !IsRevisionHash(fromHash))
            || (toHash is not null && !IsRevisionHash(toHash))) {
            return services.Results.Failure(
                "A revision hash is 64 hexadecimal characters; use the hash read_page returned.",
                "Invalid revision hash");
        }

        IReadOnlyList<PageRevisionSummary> revisions = await services.Pages.ListRevisionsAsync(path, cancellationToken);
        if (revisions.Count == 0) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path)} has no saved revisions to compare.", $"Diff {ToolPaths.Describe(path)}: no revisions");
        }

        PageRevisionSummary current = revisions.FirstOrDefault(revision => revision.IsCurrent) ?? revisions[0];
        string resolvedTo = toHash ?? current.Hash;
        string resolvedFrom;
        if (fromHash is not null) {
            resolvedFrom = fromHash;
        } else {
            PageRevisionSummary? previous = revisions.FirstOrDefault(
                revision => !string.Equals(revision.Hash, resolvedTo, StringComparison.Ordinal));
            if (previous is null) {
                return services.Results.Failure(
                    $"Page {ToolPaths.Describe(path)} has one saved revision. Save it again, or pass two hashes, before comparing.",
                    $"Diff {ToolPaths.Describe(path)}: one revision");
            }
            resolvedFrom = previous.Hash;
        }

        PageRevisionTemplate? before = await services.Pages.GetRevisionTemplateByHashAsync(resolvedFrom, cancellationToken);
        PageRevisionTemplate? after = await services.Pages.GetRevisionTemplateByHashAsync(resolvedTo, cancellationToken);
        if (before is null || before.Path != path || after is null || after.Path != path) {
            return services.Results.Failure($"One of those revisions is not a revision of {ToolPaths.Describe(path)}.", "Revision not found");
        }

        DiffDocument comparison = services.Diff.Compare(before.Template, after.Template);
        string text = services.Diff.ToUnifiedDiff(comparison);
        int budget = services.Options.ToolResultBudget;
        bool truncated = text.Length > budget;
        if (truncated) {
            text = text[..budget] + "\n[truncated]";
        }

        object data = new {
            path = path.Key,
            from = new { hash = before.Hash, savedAt = before.SavedAt },
            to = new { hash = after.Hash, savedAt = after.SavedAt },
            changed = !comparison.IsEmpty,
            addedLines = comparison.AddedLines,
            removedLines = comparison.RemovedLines,
            coarse = comparison.IsCoarse,
            truncated,
            diff = text,
            editorUrl = PageUrls.EditorDiff(path, before.Hash, after.Hash),
        };
        return services.Results.Success(data, $"Compared 2 revisions of {ToolPaths.Describe(path)}");
    }

    private static bool IsRevisionHash(string? hash) =>
        hash is { Length: 64 } value && value.All(Uri.IsHexDigit);
}
