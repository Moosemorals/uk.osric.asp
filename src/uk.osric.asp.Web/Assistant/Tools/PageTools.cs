// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Globalization;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Diff;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant.Shell;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>Lists the live pages and shell files under an optional prefix.</summary>
internal sealed class ListPagesTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "list_pages",
        "List live page keys under an optional prefix, each with its kind, its permission and its compile state. A shell file — a route the site answers itself — is listed as kind shell with mode -r--r--r--. "
        + "The listing is in key order; `limit` (1-500, default 50) bounds the rows and `truncated` says there are more, "
        + "so pass `after` with the last key you were given to read the rest.",
        false,
        [
            new("prefix", ToolArgumentType.String, false),
            new("includeDeleted", ToolArgumentType.Boolean, false),
            new("limit", ToolArgumentType.Integer, false),
            new("after", ToolArgumentType.String, false),
        ]) { Verb = "List" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string? prefixText = values.String("prefix");
        PagePath? prefix = prefixText is null ? null : PagePath.Normalize(prefixText);
        if (prefix is { IsAddressable: false }) {
            return services.Results.Failure("The prefix is not an addressable page path.", "Invalid page prefix");
        }

        bool includeDeleted = values.Boolean("includeDeleted");
        if (!PageToolData.TryReadLimit(values, services.Options, out int limit)) {
            return services.Results.Failure(PageToolData.LimitRefusal(services.Options), $"{Spec.Verb} refused");
        }

        string? afterText = values.String("after");
        PagePath? after = null;
        if (afterText is not null) {
            after = PagePath.Normalize(afterText);
            if (!after.IsAddressable) {
                return services.Results.Failure("The `after` cursor must be a page key this listing returned.", $"{Spec.Verb} refused");
            }
        }

        PageScope? scope = prefix is null ? null : new PageScope(prefix, IncludeSelf: true, Recursive: true);
        List<PageNode> matching = [.. (await services.Store.TreeAsync(includeDeleted, cancellationToken))
            .Where(node => node.IsPage && (scope is null || scope.Value.Contains(node.Path)))];
        if (after is not null) {
            matching = [.. matching.Where(item => string.CompareOrdinal(item.Path.Key, after.Key) > 0)];
        }

        bool truncated = matching.Count > limit;
        object data = new {
            pages = matching.Take(limit).Select(item => new {
                path = item.Path.Key,
                kind = item.IsShellFile ? "shell" : "page",
                mode = PageMode.Describe(item.CanWrite),
                deleted = item.IsDeleted,
                status = item.State?.Describe(),
            }),
            truncated,
        };
        return services.Results.Success(data, $"Listed {Math.Min(matching.Count, limit)} pages");
    }
}

/// <summary>Reads a page or a shell file's record, by line range.</summary>
internal sealed class ReadPageTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "read_page",
        "Read a page, its revision hash, compile status and diagnostics. Optional 1-based line numbers refer to physical lines of the stored template source. Each line in `source` is prefixed with its line number and `| `, which is not part of the source and must not be copied into a replacement. A shell file returns a record of what the route does instead, which can also be read by line.",
        false,
        [
            new("path", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, false),
            new("endLine", ToolArgumentType.Integer, false),
        ]) { Verb = "Read" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        return await ReadAsync(path!, values.Integer("startLine"), values.Integer("endLine"), services, cancellationToken);
    }

    // A shell file records no diagnostics; a page reports whatever the build that made its image wrote,
    // the template-shape warnings included.
    private static object[] PackageDiagnostics(PageRead read) => read.Page is null
        ? []
        : PageToolData.Diagnostics(read.Page);

    private async Task<ToolExecutionResult> ReadAsync(
        PagePath path,
        int? requestedStart,
        int? requestedEnd,
        ToolServices services,
        CancellationToken cancellationToken) {
        PageRead? read = await services.Store.ReadAsync(path, cancellationToken);
        if (read is null) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path)} was not found.", $"{Spec.Verb} {ToolPaths.Describe(path)}: not found");
        }

        PageToolData.ProjectedSource? projected = PageToolData.ProjectSource(read.Text, requestedStart, requestedEnd, services.Options);
        if (projected is null) {
            return services.Results.Failure("The requested line range is outside the page.", $"{Spec.Verb} {ToolPaths.Describe(path)}: invalid range");
        }

        object data = new {
            path = path.Key,
            kind = read.IsShellFile ? "shell" : "page",
            mode = PageMode.Describe(read.CanWrite),
            revisionHash = read.Page?.Hash,
            savedAt = read.Page?.SavedAt,
            compileStatus = read.State?.Describe(),
            diagnostics = PackageDiagnostics(read),
            startLine = projected.StartLine,
            endLine = projected.EndLine,
            totalLines = projected.TotalLines,
            truncated = projected.Truncated,
            source = projected.Text,
        };
        return services.Results.Success(data, $"{Spec.Verb} {ToolPaths.Describe(path)}");
    }
}

/// <summary>Replaces physical template lines if the revision is still the one read.</summary>
internal sealed class EditPageTool : AssistantTool {

    private static readonly string[] UnchangedWarnings = [
        "The edit left the page byte-identical, so nothing was saved. Check the line numbers and the replacement text.",
    ];

    public override ToolSpec Spec { get; } = new(
        "edit_page",
        "Replace physical lines of a page's stored template source if the current revision hash still matches. Lines include literal HTML and template delimiters as authored, not generated C# or rendered HTML. Give either `startLine` and `replacement` (with an optional `endLine`, which defaults to `startLine`), or a non-empty `edits` array of those objects; `edits` applies every hunk against the same hash in one save. `expectFirst` and `expectLast` name the exact text the first and last selected lines must still hold, so an off-by-one is refused instead of replacing the wrong line. The result carries a unified diff and the added and removed line counts. A shell file is refused: the owner changes the shell in code.",
        true,
        [
            new("path", ToolArgumentType.String, true),
            new("expectedRevisionHash", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, false),
            new("endLine", ToolArgumentType.Integer, false),
            new("replacement", ToolArgumentType.String, false),
            new("expectFirst", ToolArgumentType.String, false),
            new("expectLast", ToolArgumentType.String, false),
            new("edits", ToolArgumentType.Array, false) {
                Items = [
                    new("startLine", ToolArgumentType.Integer, true),
                    new("endLine", ToolArgumentType.Integer, false),
                    new("replacement", ToolArgumentType.String, true),
                    new("expectFirst", ToolArgumentType.String, false),
                    new("expectLast", ToolArgumentType.String, false),
                ],
            },
        ]) {
        Verb = "Edit",
        Writes = true,
        PathArgument = "path",
    };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveWrite(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        string expectedHash = values.RequiredString("expectedRevisionHash");
        PageTemplate? current = (await services.Store.ReadAsync(path!, cancellationToken))?.Page;
        if (current is null) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path!)} was not found. Use write_page to create it.", $"{Spec.Verb} target not found");
        }
        if (!string.Equals(current.Hash, expectedHash, StringComparison.Ordinal)) {
            return services.Results.Failure("The page has changed since read_page. Re-read it and reconcile before editing.", $"{Spec.Verb} {ToolPaths.Describe(path!)}: stale revision");
        }

        if (!TryReadEdits(values, out IReadOnlyList<PageLineEdit>? edits, out string? formError)) {
            return services.Results.Failure(formError!, $"{Spec.Verb} {ToolPaths.Describe(path!)}: rejected");
        }

        PageRangeEditResult edited = PageRangeEditor.ReplaceAll(
            current.Template,
            edits!,
            services.Options.MaxPageChars);
        if (!edited.IsSuccess) {
            return services.Results.Failure(edited.Error!, $"{Spec.Verb} {ToolPaths.Describe(path!)}: rejected");
        }

        if (PageSourceChange.IsNoOp(current.Template, edited.Content!)) {
            return services.Results.Success(new {
                path = path!.Key,
                changed = false,
                revisionHash = current.Hash,
                addedLines = 0,
                removedLines = 0,
                coarse = false,
                truncated = false,
                diff = string.Empty,
                diagnostics = PageToolData.Diagnostics(current),
                warnings = UnchangedWarnings,
            }, $"Edited {ToolPaths.Describe(path)}: no change");
        }

        DiffDocument comparison = DiffRenderer.Compare(current.Template, edited.Content!);
        (string diff, bool diffTruncated) = BoundedDiff(services, comparison);

        ConditionalPageSaveResult saved = await services.Pages.SaveIfRevisionAsync(
            path!, edited.Content!, expectedHash, cancellationToken);
        if (!saved.Saved) {
            return services.Results.Failure("The page changed while the edit was being saved. Re-read and reconcile before retrying.", $"{Spec.Verb} {ToolPaths.Describe(path!)}: stale revision");
        }

        object data = new {
            path = path!.Key,
            changed = true,
            revisionHash = saved.RevisionHash,
            addedLines = comparison.AddedLines,
            removedLines = comparison.RemovedLines,
            coarse = comparison.IsCoarse,
            truncated = diffTruncated,
            diff,
            diagnostics = PageToolData.DiagnosticData(saved.Diagnostics),
            warnings = comparison.IsEmpty
                ? new[] { "The source changed only in line endings, so the unified diff is empty." }
                : [],
        };
        return services.Results.Success(data,
            $"Edited {ToolPaths.Describe(path)} (+{comparison.AddedLines} -{comparison.RemovedLines}, {saved.Diagnostics.Count} diagnostics)");
    }

    // One form per call: either the three single-range arguments or an edits array. The array is
    // applied against the original line numbers, so a reader can quote every range from one
    // read_page without tracking a shift.
    private static bool TryReadEdits(
        ToolValues values,
        out IReadOnlyList<PageLineEdit>? edits,
        out string? error) {
        edits = null;
        error = null;

        bool hasEdits = values.Has("edits");
        bool hasSingle = values.Has("startLine") || values.Has("endLine") || values.Has("replacement")
            || values.Has("expectFirst") || values.Has("expectLast");
        if (hasEdits && hasSingle) {
            error = "Use either `startLine`, `endLine` and `replacement`, or an `edits` array, not both.";
            return false;
        }

        if (hasEdits) {
            IReadOnlyList<ToolValues> items = values.Array("edits");
            if (items.Count == 0) {
                error = "`edits` must contain at least one edit.";
                return false;
            }

            List<PageLineEdit> read = [];
            foreach (ToolValues item in items) {
                if (!item.Has("startLine") || !item.Has("replacement")) {
                    error = "Each edit needs `startLine` and `replacement`.";
                    return false;
                }
                int startLine = item.RequiredInteger("startLine");
                read.Add(new PageLineEdit(
                    startLine,
                    item.Integer("endLine") ?? startLine,
                    item.RequiredString("replacement"),
                    item.String("expectFirst"),
                    item.String("expectLast")));
            }

            edits = read;
            return true;
        }

        if (!values.Has("startLine") || !values.Has("replacement")) {
            error = "Use `startLine` and `replacement` (and `endLine` for more than one line), or an `edits` array.";
            return false;
        }

        int singleStart = values.RequiredInteger("startLine");
        edits = [
            new PageLineEdit(
                singleStart,
                values.Integer("endLine") ?? singleStart,
                values.RequiredString("replacement"),
                values.String("expectFirst"),
                values.String("expectLast")),
        ];
        return true;
    }

    // The diff is one fifth of the result limit rather than half, because JSON escaping can double a
    // line break and this answer has already saved the page: a result that overflowed into a refusal
    // would report a successful edit as a failure.
    private static (string Text, bool Truncated) BoundedDiff(ToolServices services, DiffDocument comparison) {
        string text = DiffRenderer.ToUnifiedDiff(comparison);
        int budget = Math.Max(1, services.Options.MaxToolResultChars / 5);
        if (text.Length <= budget) {
            return (text, false);
        }

        return (text[..budget] + "\n[truncated]", true);
    }
}

/// <summary>Creates or deliberately replaces a complete page.</summary>
internal sealed class WritePageTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "write_page",
        "Create or deliberately replace a complete page through the normal compile-and-save path. A shell file is refused: only the owner changes the shell, in code.",
        true,
        [
            new("path", ToolArgumentType.String, true),
            new("content", ToolArgumentType.String, true),
        ]) {
        Verb = "Write",
        Writes = true,
        PathArgument = "path",
    };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveWrite(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        string content = values.RequiredString("content");
        if (content.Length > services.Options.MaxPageChars) {
            return services.Results.Failure("The page exceeds Assistant:MaxPageChars.", $"{Spec.Verb} {ToolPaths.Describe(path!)}: too large");
        }

        SavedRevision saved = await services.Pages.SaveAsync(path!, content, cancellationToken);
        return services.Results.Success(new { path = path!.Key, revisionHash = saved.Revision.Hash, diagnostics = PageToolData.DiagnosticData(saved.Diagnostics) },
            $"Wrote {ToolPaths.Describe(path)} ({saved.Diagnostics.Count} diagnostics)");
    }
}

/// <summary>Tombstones a page, retaining its revisions.</summary>
internal sealed class DeletePageTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "delete_page",
        "Tombstone a page while retaining its revisions. A shell file and a locked page are refused.",
        true,
        [new("path", ToolArgumentType.String, true)]) {
        Verb = "Delete",
        PathArgument = "path",
    };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? path, ToolExecutionResult? refusal) = ToolPaths.ResolveWrite(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        bool deleted = await services.Pages.DeleteAsync(path!, cancellationToken);
        return services.Results.Success(new { path = path!.Key, deleted },
            deleted ? $"Deleted {ToolPaths.Describe(path)}" : $"{Spec.Verb} {ToolPaths.Describe(path)}: not live");
    }
}

/// <summary>Searches live page source and the shell files' records for a literal substring.</summary>
internal sealed class SearchPagesTool : AssistantTool {

    private static readonly PagePath s_root = PagePath.Normalize("/");

    public override ToolSpec Spec { get; } = new(
        "search_pages",
        "Search live page source and the shell files' records for a literal substring.",
        false,
        [
            new("query", ToolArgumentType.String, true),
            new("prefix", ToolArgumentType.String, false),
            new("caseSensitive", ToolArgumentType.Boolean, false),
        ]) { Verb = "Search" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string query = values.RequiredString("query");
        if (query.Length == 0) {
            return services.Results.Failure("The search query must not be empty.", "Search query is empty");
        }
        PathValidationResult checkedPrefix = PagePathPolicy.ValidatePrefix(values.String("prefix"));
        if (!checkedPrefix.IsSuccess) {
            return services.Results.Failure(checkedPrefix.Error!, $"{Spec.Verb} refused");
        }

        PagePath? prefix = checkedPrefix.Path;

        StringComparison comparison = values.Boolean("caseSensitive")
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        // A search always descends: the tool asks for every page under the prefix.
        PagePath root = prefix ?? s_root;
        IReadOnlyList<PageRead> sources = await services.Store.SourcesAsync(
            [new PageScope(root, IncludeSelf: true, Recursive: true)], cancellationToken);
        LineMatches found = PageSourceSearch.Find(
            sources, PageToolData.TemplateSourceLines, line => line.Contains(query, comparison),
            services.Options.MaxSearchMatches);

        object[] matches = [.. found.Matches.Select(match => new {
            path = match.Path.Key,
            line = match.Line,
            text = match.Text[..Math.Min(match.Text.Length, 240)],
        })];

        return services.Results.Success(new { query, matches, truncated = found.Truncated }, $"Found {matches.Length} matches");
    }
}

/// <summary>What the page tools share: the line rule, the listing rows and the diagnostic shape.</summary>
internal static class PageToolData {

    /// <summary>The refusal a hash that is not 64 lowercase hexadecimal characters gets.</summary>
    public const string HashRefusal =
        "A revision hash is 64 lowercase hexadecimal characters; use the hash list_revisions returned.";

    /// <summary>The refusal a <c>limit</c> outside the configured range gets.</summary>
    /// <param name="options">The assistant's configured listing limits.</param>
    public static string LimitRefusal(AssistantOptions options) {
        return $"`limit` must be between 1 and {options.MaxListLimit}; omit it for {options.DefaultListLimit} rows.";
    }

    /// <summary>Reads a caller's <c>limit</c>, or the configured default when it named none.</summary>
    /// <param name="values">The validated arguments.</param>
    /// <param name="options">The assistant's configured listing limits.</param>
    /// <param name="limit">The number of rows to answer with.</param>
    /// <returns>False when the argument was supplied and is outside the accepted range.</returns>
    public static bool TryReadLimit(ToolValues values, AssistantOptions options, out int limit) {
        limit = values.Integer("limit") ?? options.DefaultListLimit;

        return limit >= 1 && limit <= options.MaxListLimit;
    }

    public static object[] Diagnostics(PageTemplate page) => page.Image is null
        ? []
        : DiagnosticData(page.Image.Diagnostics);

    public static object[] DiagnosticData(IReadOnlyList<PageDiagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => new {
            code = DiagnosticCode(diagnostic),
            severity = diagnostic.Severity.ToString(),
            line = diagnostic.Position?.Line,
            column = diagnostic.Position?.Column,
            message = diagnostic.Message,
        })];

    /// <summary>One source read as a line range, windowed to fit the result budget.</summary>
    /// <param name="Text">
    /// The lines the caller asked for, each prefixed with its 1-based number and <c>| </c>, or a
    /// head-and-tail window when they were too long.
    /// </param>
    /// <param name="StartLine">The first 1-based line in the answer.</param>
    /// <param name="EndLine">The last 1-based line in the answer.</param>
    /// <param name="TotalLines">How many lines the whole source has.</param>
    /// <param name="Truncated">Whether the text was windowed because it exceeded the budget.</param>
    internal sealed record ProjectedSource(string Text, int StartLine, int EndLine, int TotalLines, bool Truncated);

    /// <summary>Reads a source as a 1-based inclusive line range, windowed to the result budget.</summary>
    /// <param name="content">The stored source.</param>
    /// <param name="requestedStart">The first line wanted, or null for the first.</param>
    /// <param name="requestedEnd">The last line wanted, or null for one line or the rest.</param>
    /// <param name="options">The assistant's limits.</param>
    /// <returns>The range, or null when it is outside the source.</returns>
    /// <remarks>
    /// <para>
    /// `read_page` and `read_revision` read source through this one function, so a revision a tool
    /// quotes is numbered and windowed exactly as the live page is (DEC-112).
    /// </para>
    /// <para>
    /// Every line carries its number because the result is read as escaped JSON: counting lines in
    /// that is guesswork, and a number that is not on the line cannot be miscounted.
    /// </para>
    /// </remarks>
    public static ProjectedSource? ProjectSource(string content, int? requestedStart, int? requestedEnd, AssistantOptions options) {
        string[] lines = TemplateSourceLines(content);
        int start = requestedStart ?? 1;
        int end = requestedEnd ?? (requestedStart is null ? lines.Length : start);
        if (start < 1 || end < start || end > lines.Length) {
            return null;
        }

        string[] selected = [.. lines.Skip(start - 1).Take(end - start + 1)];
        string[] numbered = new string[selected.Length];
        for (int index = 0; index < selected.Length; index++) {
            numbered[index] = NumberedLine(start + index, selected[index]);
        }

        string source = string.Join('\n', numbered);
        bool truncated = source.Length > options.ToolResultBudget;
        if (truncated) {
            source = Window(source, Math.Max(1, options.MaxToolResultChars / 5));
        }

        return new ProjectedSource(source, start, end, lines.Length, truncated);
    }

    private static string NumberedLine(int number, string text) =>
        number.ToString(CultureInfo.InvariantCulture) + "| " + text;

    // Both ends of a window keep complete lines, so every line still opens with its own number.
    private static string Window(string source, int window) {
        string head = source[..Math.Min(window, source.Length)];
        int lastBreak = head.LastIndexOf('\n');
        if (lastBreak >= 0) {
            head = head[..lastBreak];
        }

        int tailStart = Math.Max(1, source.Length - window);
        int lineStart = source.LastIndexOf('\n', tailStart - 1);
        string tail = lineStart >= 0 ? source[(lineStart + 1)..] : string.Empty;

        return head + "\n\u2026[source omitted; request a line range]\u2026\n" + tail;
    }

    // These are physical lines of the stored template as the editor shows them, so a trailing break
    // still ends a line. ShellText.Split answers the POSIX count instead and must not be unified.
    public static string[] TemplateSourceLines(string content) {
        List<string> lines = [];
        using StringReader reader = new(content);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            lines.Add(line);
        }
        if (content.Length == 0 || content.EndsWith('\n') || content.EndsWith('\r')) {
            lines.Add("");
        }
        return [.. lines];
    }

    private static string? DiagnosticCode(PageDiagnostic diagnostic) {
        int separator = diagnostic.Message.IndexOf(':');
        return separator > 0 ? diagnostic.Message[..separator] : null;
    }
}
