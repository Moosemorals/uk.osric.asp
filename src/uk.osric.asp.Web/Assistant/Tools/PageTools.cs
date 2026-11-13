// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
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
        if (!PageToolData.TryReadLimit(values, out int limit)) {
            return services.Results.Failure(PageToolData.LimitRefusal, $"{Spec.Verb} refused");
        }

        string? afterText = values.String("after");
        PagePath? after = null;
        if (afterText is not null) {
            after = PagePath.Normalize(afterText);
            if (!after.IsAddressable) {
                return services.Results.Failure("The `after` cursor must be a page key this listing returned.", $"{Spec.Verb} refused");
            }
        }

        List<PageToolData.Entry> matching = await PageToolData.EntriesAsync(services, includeDeleted, prefix, cancellationToken);
        if (after is not null) {
            matching = [.. matching.Where(item => string.CompareOrdinal(item.Path.Key, after.Key) > 0)];
        }

        bool truncated = matching.Count > limit;
        object data = new {
            pages = matching.Take(limit).Select(item => new {
                path = item.Path.Key,
                kind = item.IsShellFile ? "shell" : "page",
                mode = PageMode.Describe(services.Paths.CanWrite(item.Path)),
                deleted = item.IsDeleted,
                status = item.Status,
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
        "Read a page, its revision hash, compile status and diagnostics. Optional 1-based line numbers refer to physical lines of the stored template source. A shell file returns a record of what the route does instead, which can also be read by line.",
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

    private async Task<ToolExecutionResult> ReadAsync(
        PagePath path,
        int? requestedStart,
        int? requestedEnd,
        ToolServices services,
        CancellationToken cancellationToken) {
        string text;
        string? revisionHash = null;
        DateTimeOffset? savedAt = null;
        string? compileStatus = null;
        object[] diagnostics = [];
        if (services.Paths.IsReserved(path)) {
            text = ShellRoutes.Text(path);
        } else {
            PageTemplate? page = await services.Pages.GetAsync(path, cancellationToken);
            if (page is null) {
                return services.Results.Failure($"Page {ToolPaths.Describe(path)} was not found.", $"{Spec.Verb} {ToolPaths.Describe(path)}: not found");
            }

            text = page.Template;
            revisionHash = page.Hash;
            savedAt = page.SavedAt;
            compileStatus = PageToolData.ImageStatus(page, services.Pages.RuntimeId);
            diagnostics = PageToolData.Diagnostics(page);
        }

        PageToolData.ProjectedSource? projected = PageToolData.ProjectSource(text, requestedStart, requestedEnd, services.Options);
        if (projected is null) {
            return services.Results.Failure("The requested line range is outside the page.", $"{Spec.Verb} {ToolPaths.Describe(path)}: invalid range");
        }

        object data = new {
            path = path.Key,
            kind = services.Paths.IsReserved(path) ? "shell" : "page",
            mode = PageMode.Describe(services.Paths.CanWrite(path)),
            revisionHash,
            savedAt,
            compileStatus,
            diagnostics,
            startLine = projected.StartLine,
            endLine = projected.EndLine,
            totalLines = projected.TotalLines,
            truncated = projected.Truncated,
            source = projected.Text,
        };
        return services.Results.Success(data, $"{Spec.Verb} {ToolPaths.Describe(path)}");
    }
}

/// <summary>Replaces a range of physical template lines if the revision is still the one read.</summary>
internal sealed class EditPageTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "edit_page",
        "Replace an inclusive range of 1-based physical template-source lines if the current revision hash still matches. Lines include literal HTML and template delimiters as authored, not generated C# or rendered HTML. A shell file is refused: the owner changes the shell in code.",
        true,
        [
            new("path", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, true),
            new("endLine", ToolArgumentType.Integer, true),
            new("expectedRevisionHash", ToolArgumentType.String, true),
            new("replacement", ToolArgumentType.String, true),
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
        PageTemplate? current = await services.Pages.GetAsync(path!, cancellationToken);
        if (current is null) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path!)} was not found. Use write_page to create it.", $"{Spec.Verb} target not found");
        }
        if (!string.Equals(current.Hash, expectedHash, StringComparison.Ordinal)) {
            return services.Results.Failure("The page has changed since read_page. Re-read it and reconcile before editing.", $"{Spec.Verb} {ToolPaths.Describe(path!)}: stale revision");
        }

        PageRangeEditResult edited = PageRangeEditor.Replace(
            current.Template,
            values.RequiredInteger("startLine"),
            values.RequiredInteger("endLine"),
            values.RequiredString("replacement"),
            services.Options.MaxPageChars);
        if (!edited.IsSuccess) {
            return services.Results.Failure(edited.Error!, $"{Spec.Verb} {ToolPaths.Describe(path!)}: rejected");
        }

        ConditionalPageSaveResult saved = await services.Pages.SaveIfRevisionAsync(
            path!, edited.Content!, expectedHash, cancellationToken);
        if (!saved.Saved) {
            return services.Results.Failure("The page changed while the edit was being saved. Re-read and reconcile before retrying.", $"{Spec.Verb} {ToolPaths.Describe(path!)}: stale revision");
        }

        return services.Results.Success(new { path = path!.Key, revisionHash = saved.RevisionHash, diagnostics = PageToolData.DiagnosticData(saved.Diagnostics) },
            $"Edited {ToolPaths.Describe(path)} ({saved.Diagnostics.Count} diagnostics)");
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

    private const int MatchLimit = 100;

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
        (List<object> matches, bool truncated) = await FindMatchesAsync<object>(services, [prefix],
            line => line.Contains(query, comparison), recursive: true,
            (path, line, text) => new { path = path.Key, line, text = text[..Math.Min(text.Length, 240)] },
            cancellationToken);

        return services.Results.Success(new { query, matches, truncated }, $"Found {matches.Count} matches");
    }

    // The scan stops one match past the limit, so the caller can say that something was left behind
    // rather than guess from the count. Keys are visited in ordinal order, so a result reads like a
    // listing.
    private static async Task<(List<T> Matches, bool Truncated)> FindMatchesAsync<T>(
        ToolServices services,
        IReadOnlyList<PagePath?> prefixes,
        Func<string, bool> matches,
        bool recursive,
        Func<PagePath, int, string, T> project,
        CancellationToken cancellationToken) {
        List<T> found = [];
        foreach ((PagePath path, string text) in await SourcesAsync(services, prefixes, recursive, cancellationToken)) {
            string[] lines = PageToolData.TemplateSourceLines(text);
            for (int index = 0; index < lines.Length; index++) {
                if (!matches(lines[index])) {
                    continue;
                }
                if (found.Count == MatchLimit) {
                    return (found, true);
                }
                found.Add(project(path, index + 1, lines[index]));
            }
        }

        return (found, false);
    }

    // The pages and shell records a command may read, in key order, so two searches of one tree agree.
    private static async Task<List<(PagePath Path, string Text)>> SourcesAsync(
        ToolServices services,
        IReadOnlyList<PagePath?> prefixes,
        bool recursive,
        CancellationToken cancellationToken) {
        List<(PagePath Path, string Text)> sources = [.. services.Paths.Reserved
            .Where(path => IsInScope(path, prefixes, recursive))
            .Select(path => (path, ShellRoutes.Text(path)))];

        foreach (PageListing head in await services.Pages.ListAsync(cancellationToken)) {
            if (head.IsDeleted || services.Paths.IsReserved(head.Path) || !IsInScope(head.Path, prefixes, recursive)) {
                continue;
            }

            PageTemplate? page = await services.Pages.GetAsync(head.Path, cancellationToken);
            if (page is not null) {
                sources.Add((head.Path, page.Template));
            }
        }

        sources.Sort((left, right) => string.CompareOrdinal(left.Path.Key, right.Path.Key));
        return sources;
    }

    // Whether a key is inside what a command was told to read. A path names a file as readily as a
    // folder, so the key it names is always in scope; what lives below it is in scope only when the
    // caller asked for a search that descends.
    private static bool IsInScope(PagePath key, IReadOnlyList<PagePath?> prefixes, bool recursive) {
        foreach (PagePath? prefix in prefixes) {
            PagePath scope = prefix ?? s_root;
            if (string.Equals(key.Key, scope.Key, StringComparison.Ordinal)) {
                return true;
            }
            if (recursive ? IsBelow(key, scope) : IsDirectlyBelow(key, scope)) {
                return true;
            }
        }

        return false;
    }

    // Whether a key lives anywhere below a path.
    private static bool IsBelow(PagePath key, PagePath prefix) => prefix.Key == "/"
        ? key.Key.Length > 1
        : key.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);

    // Whether a key is an entry of a path: below it, and no deeper than one segment.
    private static bool IsDirectlyBelow(PagePath key, PagePath prefix) {
        if (!IsBelow(key, prefix)) {
            return false;
        }

        string rest = prefix.Key == "/" ? key.Key[1..] : key.Key[(prefix.Key.Length + 1)..];
        return !rest.Contains('/');
    }
}

/// <summary>What the page tools share: the line rule, the listing rows and the diagnostic shape.</summary>
internal static class PageToolData {

    /// <summary>The rows a listing answers with when the caller names no <c>limit</c>.</summary>
    public const int DefaultListingLimit = 50;

    /// <summary>The most rows a listing will answer with, so one call cannot exhaust the result budget.</summary>
    public const int MaxListingLimit = 500;

    /// <summary>The refusal a hash that is not 64 lowercase hexadecimal characters gets.</summary>
    public const string HashRefusal =
        "A revision hash is 64 lowercase hexadecimal characters; use the hash list_revisions returned.";

    /// <summary>The refusal a <c>limit</c> outside 1..<see cref="MaxListingLimit"/> gets.</summary>
    public static string LimitRefusal =>
        $"`limit` must be between 1 and {MaxListingLimit}; omit it for {DefaultListingLimit} rows.";

    /// <summary>Reads a caller's <c>limit</c>, or the default when it named none.</summary>
    /// <param name="values">The validated arguments.</param>
    /// <param name="limit">The number of rows to answer with.</param>
    /// <returns>False when the argument was supplied and is outside the accepted range.</returns>
    public static bool TryReadLimit(ToolValues values, out int limit) {
        limit = values.Integer("limit") ?? DefaultListingLimit;

        return limit is >= 1 and <= MaxListingLimit;
    }

    // One row of a listing: a stored page or one of the shell's own files. A shell file has no head,
    // so it is never deleted and has no compile state.
    public sealed record Entry(PagePath Path, bool IsShellFile, bool IsDeleted, string? Status);

    public static async Task<List<Entry>> EntriesAsync(
        ToolServices services,
        bool includeDeleted,
        PagePath? prefix,
        CancellationToken cancellationToken) {
        List<Entry> entries = [.. services.Paths.Reserved
            .Where(path => IsUnderPrefix(path, prefix))
            .Select(path => new Entry(path, IsShellFile: true, IsDeleted: false, Status: null))];

        foreach (PageListing head in await services.Pages.ListAsync(cancellationToken)) {
            if (services.Paths.IsReserved(head.Path) || !IsUnderPrefix(head.Path, prefix)) {
                continue;
            }
            if (!includeDeleted && head.IsDeleted) {
                continue;
            }
            entries.Add(new Entry(head.Path, IsShellFile: false, head.IsDeleted, head.State.Describe()));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Path.Key, right.Path.Key));
        return entries;
    }

    // The page is shown as it stands against the running build, so a page compiled by an earlier one
    // says Stale rather than repeating diagnostics that are about to be replaced.
    public static string ImageStatus(PageTemplate page, string currentRuntimeId) => PageImageStatus.Describe(
        page.Image?.RuntimeId,
        page.Image?.Bytes is not null,
        page.Image?.Diagnostics.Count > 0,
        currentRuntimeId).Describe();

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
    /// <param name="Text">The lines the caller asked for, or a head-and-tail window when they were too long.</param>
    /// <param name="StartLine">The first 1-based line in the answer.</param>
    /// <param name="EndLine">The last 1-based line in the answer.</param>
    /// <param name="TotalLines">How many lines the whole source has.</param>
    /// <param name="Truncated">Whether the text was windowed because it exceeded the budget.</param>
    public sealed record ProjectedSource(string Text, int StartLine, int EndLine, int TotalLines, bool Truncated);

    /// <summary>Reads a source as a 1-based inclusive line range, windowed to the result budget.</summary>
    /// <param name="content">The stored source.</param>
    /// <param name="requestedStart">The first line wanted, or null for the first.</param>
    /// <param name="requestedEnd">The last line wanted, or null for one line or the rest.</param>
    /// <param name="options">The assistant's limits.</param>
    /// <returns>The range, or null when it is outside the source.</returns>
    /// <remarks>
    /// `read_page` and `read_revision` read source through this one function, so a revision a tool
    /// quotes is numbered and windowed exactly as the live page is (DEC-112).
    /// </remarks>
    public static ProjectedSource? ProjectSource(string content, int? requestedStart, int? requestedEnd, AssistantOptions options) {
        string[] lines = TemplateSourceLines(content);
        int start = requestedStart ?? 1;
        int end = requestedEnd ?? (requestedStart is null ? lines.Length : start);
        if (start < 1 || end < start || end > lines.Length) {
            return null;
        }

        string source = string.Join('\n', lines.Skip(start - 1).Take(end - start + 1));
        bool truncated = source.Length > options.ToolResultBudget;
        if (truncated) {
            int window = Math.Max(1, options.MaxToolResultChars / 5);
            source = source[..Math.Min(window, source.Length)]
                + "\n\u2026[source omitted; request a line range]\u2026\n"
                + source[^Math.Min(window, source.Length)..];
        }

        return new ProjectedSource(source, start, end, lines.Length, truncated);
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

    private static bool IsUnderPrefix(PagePath path, PagePath? prefix) => prefix is null
        || string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
        || prefix.Key == "/"
        || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);
}
