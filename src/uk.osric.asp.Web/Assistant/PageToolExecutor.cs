// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Globalization;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What one tool call produced, as the model receives it.</summary>
internal sealed record ToolExecutionResult(bool IsSuccess, string Content, string Summary);

/// <summary>One tool as it is offered to the model and validated when it is called.</summary>
/// <param name="Name">The tool's stable name.</param>
/// <param name="Description">The provider-visible description.</param>
/// <param name="NeedsApproval">Whether RequireApproval mode pauses before this tool runs.</param>
/// <param name="Arguments">The complete set of accepted arguments.</param>
internal sealed record ToolSpec(
    string Name,
    string Description,
    bool NeedsApproval,
    IReadOnlyList<ToolArgumentSchema> Arguments);

/// <summary>Runs the page tools the model asks for: read, write, list, check and delete.</summary>
internal sealed class PageToolExecutor(
    IServiceScopeFactory scopeFactory,
    PagePathPolicy paths,
    AssistantOptions options) {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private const int MatchLimit = 100;

    // One table: the schema a call is validated against and the tool the model is offered are both
    // derived from it, so an argument cannot be described to the model and refused at execution.
    private static readonly ToolSpec[] s_tools = [
        new("list_pages", "List live page keys under an optional prefix, each with its compile state.", false, [
            new("prefix", ToolArgumentType.String, false),
            new("includeDeleted", ToolArgumentType.Boolean, false),
        ]),
        new("read_page", "Read a page, its revision hash, compile status and diagnostics. Optional 1-based line numbers refer to physical lines of the stored template source.", false, [
            new("path", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, false),
            new("endLine", ToolArgumentType.Integer, false),
        ]),
        new("edit_page", "Replace an inclusive range of 1-based physical template-source lines if the current revision hash still matches. Lines include literal HTML and template delimiters as authored, not generated C# or rendered HTML.", true, [
            new("path", ToolArgumentType.String, true),
            new("startLine", ToolArgumentType.Integer, true),
            new("endLine", ToolArgumentType.Integer, true),
            new("expectedRevisionHash", ToolArgumentType.String, true),
            new("replacement", ToolArgumentType.String, true),
        ]),
        new("write_page", "Create or deliberately replace a complete page through the normal compile-and-save path.", true, [
            new("path", ToolArgumentType.String, true),
            new("content", ToolArgumentType.String, true),
        ]),
        new("delete_page", "Tombstone a page while retaining its revisions.", true, [
            new("path", ToolArgumentType.String, true),
        ]),
        new("search_pages", "Search live page source for a literal substring.", false, [
            new("query", ToolArgumentType.String, true),
            new("prefix", ToolArgumentType.String, false),
            new("caseSensitive", ToolArgumentType.Boolean, false),
        ]),
        new("check_script",
            "Check the JavaScript a stored page serves. Reads the page by path and parses its literal "
            + "text as an ES module. The key's extension is not consulted: a page that is not "
            + "JavaScript will generally produce syntax errors. Checks syntax only; imports are not "
            + "resolved and nothing is executed.",
            false,
            [new("path", ToolArgumentType.String, true)]),
        new("check_script_text",
            "Check the JavaScript in a complete page template supplied as content, without storing "
            + "it. Parses the literal text as an ES module. Pass the whole page, not an edit_page "
            + "replacement fragment: a fragment can be invalid alone even when the edit is correct. "
            + "Checks syntax only; imports are not resolved and nothing is executed.",
            false,
            [new("content", ToolArgumentType.String, true)]),
        new("shell", "Run a read-only virtual-shell command over stored pages.", false, [
            new("command", ToolArgumentType.String, true),
        ]),
    ];

    public static IReadOnlyList<ToolDefinition> Definitions { get; } =
        [.. s_tools.Select(tool => new ToolDefinition(
            tool.Name,
            tool.Description,
            tool.NeedsApproval,
            ParametersJson(tool.Arguments)))];

    private static readonly IReadOnlyDictionary<string, ToolArgumentSchema[]> s_schemas =
        s_tools.ToDictionary(tool => tool.Name, tool => tool.Arguments.ToArray(), StringComparer.Ordinal);

    public async Task<ToolExecutionResult> ExecuteAsync(
        string name,
        string argumentsJson,
        CancellationToken cancellationToken) {
        if (!s_schemas.TryGetValue(name, out ToolArgumentSchema[]? schema)) {
            string unknown = MessageText.Sanitize(name);
            return Failure($"Unknown tool '{unknown}'.", $"Unknown tool {unknown}");
        }

        ToolArgumentsResult parsed = ToolArguments.Parse(argumentsJson, schema, options.MaxToolArgumentChars);
        if (!parsed.IsSuccess) {
            return Failure(parsed.Error!, $"Invalid arguments for {name}");
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        PageEditor pages = scope.ServiceProvider.GetRequiredService<PageEditor>();
        return name switch {
            "list_pages" => await ListPagesAsync(parsed.Values, pages, cancellationToken),
            "read_page" => await ReadPageAsync(parsed.Values, pages, cancellationToken),
            "edit_page" => await EditPageAsync(parsed.Values, pages, cancellationToken),
            "write_page" => await WritePageAsync(parsed.Values, pages, cancellationToken),
            "delete_page" => await DeletePageAsync(parsed.Values, pages, cancellationToken),
            "search_pages" => await SearchPagesAsync(parsed.Values, pages, cancellationToken),
            "check_script" => await CheckScriptAsync(parsed.Values, pages, cancellationToken),
            "check_script_text" => CheckScriptText(parsed.Values),
            "shell" => await ShellAsync(parsed.Values, pages, cancellationToken),
            _ => Failure("Unknown tool.", $"Unknown tool {MessageText.Sanitize(name)}"),
        };
    }

    private async Task<ToolExecutionResult> ListPagesAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        string? prefixText = String(arguments, "prefix");
        PagePath? prefix = prefixText is null ? null : PagePath.Normalize(prefixText);
        if (prefix is { IsAddressable: false }) {
            return Failure("The prefix is not an addressable page path.", "Invalid page prefix");
        }

        bool includeDeleted = Boolean(arguments, "includeDeleted");
        IReadOnlyList<PageListing> all = await pages.ListAsync(cancellationToken);
        PageListing[] matching = [.. all
            .Where(item => !paths.IsProtected(item.Path)
                && (includeDeleted || !item.IsDeleted)
                && IsUnderPrefix(item.Path, prefix))
            .OrderBy(item => item.Path.Key, StringComparer.Ordinal)];
        bool truncated = matching.Length > 500;
        object data = new {
            pages = matching.Take(500).Select(item => new {
                path = item.Path.Key,
                deleted = item.IsDeleted,
                status = item.State.Describe(),
            }),
            truncated,
        };
        return Success(data, $"Listed {Math.Min(matching.Length, 500)} pages");
    }

    private async Task<ToolExecutionResult> ReadPageAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        PathValidationResult checkedPath = paths.Validate(String(arguments, "path"), write: false);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Read refused");
        }
        return await ReadPageAtAsync(checkedPath.Path!, Integer(arguments, "startLine"), Integer(arguments, "endLine"), pages, cancellationToken);
    }

    private async Task<ToolExecutionResult> ReadPageAtAsync(
        PagePath path,
        int? requestedStart,
        int? requestedEnd,
        PageEditor pages,
        CancellationToken cancellationToken) {
        PageTemplate? page = await pages.GetAsync(path, cancellationToken);
        if (page is null) {
            return Failure($"Page {Describe(path)} was not found.", $"Read {Describe(path)}: not found");
        }

        string[] lines = ReadLines(page.Template);
        int start = requestedStart ?? 1;
        int end = requestedEnd ?? (requestedStart is null ? lines.Length : start);
        if (start < 1 || end < start || end > lines.Length) {
            return Failure("The requested line range is outside the page.", $"Read {Describe(path)}: invalid range");
        }

        string source = string.Join('\n', lines.Skip(start - 1).Take(end - start + 1));
        bool truncated = source.Length > options.MaxToolResultChars / 2;
        if (truncated) {
            int window = Math.Max(1, options.MaxToolResultChars / 5);
            source = source[..Math.Min(window, source.Length)]
                + "\n\u2026[source omitted; request a line range]\u2026\n"
                + source[^Math.Min(window, source.Length)..];
        }

        object data = new {
            path = path.Key,
            revisionHash = page.Hash,
            savedAt = page.SavedAt,
            compileStatus = ImageStatus(page, pages.RuntimeId),
            diagnostics = Diagnostics(page),
            startLine = start,
            endLine = end,
            totalLines = lines.Length,
            truncated,
            source,
        };
        return Success(data, $"Read {Describe(path)}");
    }

    private async Task<ToolExecutionResult> CheckScriptAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        PathValidationResult checkedPath = paths.Validate(String(arguments, "path"), write: false);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Check refused");
        }

        PagePath path = checkedPath.Path!;
        PageTemplate? page = await pages.GetAsync(path, cancellationToken);
        if (page is null) {
            return Failure($"Page {Describe(path)} was not found.", $"Check {Describe(path)}: not found");
        }

        return CheckTemplate(page.Template, path);
    }

    private ToolExecutionResult CheckScriptText(IReadOnlyDictionary<string, JsonElement> arguments) =>
        CheckTemplate(String(arguments, "content")!, path: null);

    // A skeleton that could not be built is reported beside the parser's own errors, so a page with
    // an unterminated template block says so instead of failing the tool call.
    private ToolExecutionResult CheckTemplate(string template, PagePath? path) {
        ScriptSkeletonResult skeleton = TemplateScriptSkeleton.Create(template);
        IReadOnlyList<PageDiagnostic> diagnostics = skeleton.IsSuccess
            ? JavaScriptValidator.Validate(skeleton.Source)
            : skeleton.Diagnostics;
        bool valid = diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);

        string where = path is null ? "supplied text" : Describe(path);
        string summary = diagnostics.Count == 0
            ? $"Checked {where}: valid"
            : $"Checked {where}: {diagnostics.Count} problem{(diagnostics.Count == 1 ? string.Empty : "s")}";

        return path is null
            ? Success(new { valid, diagnostics = ScriptDiagnostics(diagnostics) }, summary)
            : Success(new { path = path.Key, valid, diagnostics = ScriptDiagnostics(diagnostics) }, summary);
    }

    private async Task<ToolExecutionResult> EditPageAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        string pathText = String(arguments, "path")!;
        PathValidationResult checkedPath = paths.Validate(pathText, write: true);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Edit refused");
        }

        string expectedHash = String(arguments, "expectedRevisionHash")!;
        PageTemplate? current = await pages.GetAsync(checkedPath.Path!, cancellationToken);
        if (current is null) {
            return Failure($"Page {Describe(checkedPath.Path!)} was not found. Use write_page to create it.", "Edit target not found");
        }
        if (!string.Equals(current.Hash, expectedHash, StringComparison.Ordinal)) {
            return Failure("The page has changed since read_page. Re-read it and reconcile before editing.", $"Edit {Describe(checkedPath.Path!)}: stale revision");
        }

        PageRangeEditResult edited = PageRangeEditor.Replace(
            current.Template,
            Integer(arguments, "startLine")!.Value,
            Integer(arguments, "endLine")!.Value,
            String(arguments, "replacement")!,
            options.MaxPageChars);
        if (!edited.IsSuccess) {
            return Failure(edited.Error!, $"Edit {Describe(checkedPath.Path!)}: rejected");
        }

        ConditionalPageSaveResult saved = await pages.SaveIfRevisionAsync(
            checkedPath.Path!, edited.Content!, expectedHash, cancellationToken);
        if (!saved.Saved) {
            return Failure("The page changed while the edit was being saved. Re-read and reconcile before retrying.", $"Edit {Describe(checkedPath.Path!)}: stale revision");
        }

        return Success(new { path = checkedPath.Path!.Key, revisionHash = saved.RevisionHash, diagnostics = DiagnosticData(saved.Diagnostics) },
            $"Edited {Describe(checkedPath.Path)} ({saved.Diagnostics.Count} diagnostics)");
    }

    private async Task<ToolExecutionResult> WritePageAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        PathValidationResult checkedPath = paths.Validate(String(arguments, "path"), write: true);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Write refused");
        }

        string content = String(arguments, "content")!;
        if (content.Length > options.MaxPageChars) {
            return Failure("The page exceeds Assistant:MaxPageChars.", $"Write {Describe(checkedPath.Path!)}: too large");
        }

        IReadOnlyList<Core.Compilation.PageDiagnostic> diagnostics =
            await pages.SaveAsync(checkedPath.Path!, content, cancellationToken);
        PageTemplate saved = (await pages.GetAsync(checkedPath.Path!, cancellationToken))!;
        return Success(new { path = checkedPath.Path!.Key, revisionHash = saved.Hash, diagnostics = DiagnosticData(diagnostics) },
            $"Wrote {Describe(checkedPath.Path)} ({diagnostics.Count} diagnostics)");
    }

    private async Task<ToolExecutionResult> DeletePageAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        PathValidationResult checkedPath = paths.Validate(String(arguments, "path"), write: true);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Delete refused");
        }

        bool deleted = await pages.DeleteAsync(checkedPath.Path!, cancellationToken);
        return Success(new { path = checkedPath.Path!.Key, deleted },
            deleted ? $"Deleted {Describe(checkedPath.Path)}" : $"Delete {Describe(checkedPath.Path)}: not live");
    }

    private async Task<ToolExecutionResult> SearchPagesAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        string query = String(arguments, "query")!;
        if (query.Length == 0) {
            return Failure("The search query must not be empty.", "Search query is empty");
        }
        PathValidationResult checkedPrefix = PagePathPolicy.ValidatePrefix(String(arguments, "prefix"));
        if (!checkedPrefix.IsSuccess) {
            return Failure(checkedPrefix.Error!, "Search refused");
        }

        PagePath? prefix = checkedPrefix.Path;

        StringComparison comparison = Boolean(arguments, "caseSensitive")
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        (List<object> matches, bool truncated) = await FindMatchesAsync<object>(pages, prefix, query, comparison,
            (head, line, text) => new { path = head.Path.Key, line, text = text[..Math.Min(text.Length, 240)] },
            cancellationToken);

        return Success(new { query, matches, truncated }, $"Found {matches.Count} matches");
    }

    private async Task<ToolExecutionResult> ShellAsync(
        IReadOnlyDictionary<string, JsonElement> arguments,
        PageEditor pages,
        CancellationToken cancellationToken) {
        VirtualShellParseResult parsed = VirtualShellParser.Parse(String(arguments, "command")!);
        if (parsed.Command is null) {
            return Failure(parsed.Error!, "Shell command refused");
        }

        VirtualShellCommand command = parsed.Command;
        string? prefixText = command.Name switch {
            "ls" or "find" => command.Arguments.Count > 0 ? command.Arguments[0] : null,
            "grep" => command.Arguments.Count > (command.Arguments[0] == "-i" ? 2 : 1) ? command.Arguments[^1] : null,
            _ => null,
        };
        PathValidationResult checkedPrefix = PagePathPolicy.ValidatePrefix(prefixText);
        if (!checkedPrefix.IsSuccess) {
            return Failure(checkedPrefix.Error!, "Shell command refused");
        }

        PagePath? prefix = checkedPrefix.Path;

        if (command.Name is "ls" or "find") {
            IReadOnlyList<PageListing> heads = await pages.ListAsync(cancellationToken);
            string shellOutput = string.Join('\n', heads.Where(head => !head.IsDeleted && !paths.IsProtected(head.Path) && IsUnderPrefix(head.Path, prefix))
                .OrderBy(head => head.Path.Key, StringComparer.Ordinal).Take(500).Select(head => head.Path.Key));
            return Success(new { output = shellOutput }, prefix is null ? command.Name : $"{command.Name} {Describe(prefix)}");
        }

        string pathText = command.Name switch {
            "head" or "tail" => command.Arguments[2],
            "wc" or "stat" or "cat" => command.Arguments[^1],
            "grep" => prefix?.Key ?? "/",
            _ => "",
        };
        if (command.Name == "grep") {
            int queryIndex = command.Arguments[0] == "-i" ? 1 : 0;
            string query = command.Arguments[queryIndex];
            StringComparison comparison = command.Arguments[0] == "-i" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            (List<string> found, _) = await FindMatchesAsync(pages, prefix, query, comparison,
                (head, line, text) => $"{head.Path.Key}:{line}: {text}",
                cancellationToken);

            return Success(new { output = string.Join('\n', found) }, $"grep {MessageText.Sanitize(query)}");
        }

        PathValidationResult checkedPath = paths.Validate(pathText, write: false);
        if (!checkedPath.IsSuccess) {
            return Failure(checkedPath.Error!, "Shell path refused");
        }
        PageTemplate? target = await pages.GetAsync(checkedPath.Path!, cancellationToken);
        if (target is null) {
            return Failure($"Page {Describe(checkedPath.Path!)} was not found.", $"{command.Name} {Describe(checkedPath.Path!)}: not found");
        }
        string[] sourceLines = ReadLines(target.Template);
        string output = command.Name switch {
            "cat" => target.Template,
            "head" => string.Join('\n', sourceLines.Take(ParseCount(command.Arguments[1]))),
            "tail" => string.Join('\n', sourceLines.TakeLast(ParseCount(command.Arguments[1]))),
            "wc" => sourceLines.Length.ToString(CultureInfo.InvariantCulture),
            "stat" => $"{checkedPath.Path!.Key}\n{target.Template.Length} chars\n{target.Hash}\n{target.SavedAt:O}\n{ImageStatus(target, pages.RuntimeId)}",
            _ => "",
        };
        // Half the budget, because the output still has to fit back inside the JSON envelope with
        // room for escaping; the same rule read_page uses.
        int budget = options.MaxToolResultChars / 2;
        if (output.Length > budget) {
            output = output[..budget] + "\n[truncated]";
        }
        return Success(new { output }, $"{command.Name} {Describe(checkedPath.Path!)}");
    }

    private ToolExecutionResult Success(object data, string summary) => Bounded(new { ok = true, data, error = (string?)null }, summary);

    private ToolExecutionResult Failure(string error, string summary) => Bounded(new { ok = false, data = (object?)null, error }, summary, success: false);

    private ToolExecutionResult Bounded(object envelope, string summary, bool success = true) {
        string json = JsonSerializer.Serialize(envelope, s_jsonOptions);
        return json.Length <= options.MaxToolResultChars
            ? new ToolExecutionResult(success, json, summary)
            : new ToolExecutionResult(false,
                "{\"ok\":false,\"data\":null,\"error\":\"Tool result exceeds the configured size limit.\"}",
                "Tool result too large");
    }

    // Both search_pages and the shell's grep walk the same live pages under the same rules, so the
    // scan lives here once and the caller decides what a match becomes. It looks one match past the
    // limit so the caller can say whether anything was left behind rather than guessing from the count.
    private async Task<(List<T> Matches, bool Truncated)> FindMatchesAsync<T>(
        PageEditor pages,
        PagePath? prefix,
        string query,
        StringComparison comparison,
        Func<PageListing, int, string, T> project,
        CancellationToken cancellationToken) {
        List<T> matches = [];

        foreach (PageListing head in await pages.ListAsync(cancellationToken)) {
            if (head.IsDeleted || paths.IsProtected(head.Path) || !IsUnderPrefix(head.Path, prefix)) {
                continue;
            }

            PageTemplate? page = await pages.GetAsync(head.Path, cancellationToken);
            if (page is null) {
                continue;
            }

            string[] lines = ReadLines(page.Template);
            for (int index = 0; index < lines.Length; index++) {
                if (!lines[index].Contains(query, comparison)) {
                    continue;
                }
                if (matches.Count == MatchLimit) {
                    return (matches, true);
                }
                matches.Add(project(head, index + 1, lines[index]));
            }
        }

        return (matches, false);
    }

    private static string ParametersJson(IReadOnlyList<ToolArgumentSchema> arguments) {
        Dictionary<string, object> properties = new(StringComparer.Ordinal);
        foreach (ToolArgumentSchema argument in arguments) {
            properties[argument.Name] = new { type = JsonTypeOf(argument.Type) };
        }

        string[] required = [.. arguments.Where(argument => argument.Required).Select(argument => argument.Name)];

        return JsonSerializer.Serialize(new {
            type = "object",
            properties,
            required,
            additionalProperties = false,
        }, s_jsonOptions);
    }

    private static string JsonTypeOf(ToolArgumentType type) => type switch {
        ToolArgumentType.String => "string",
        ToolArgumentType.Integer => "integer",
        ToolArgumentType.Boolean => "boolean",
        _ => "string",
    };

    private static string? String(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out JsonElement value) ? value.GetString() : null;

    private static int? Integer(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out JsonElement value) ? value.GetInt32() : null;

    private static bool Boolean(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.TryGetValue(name, out JsonElement value) && value.GetBoolean();

    private static string Describe(PagePath path) => MessageText.Sanitize(path.Key);

    private static bool IsUnderPrefix(PagePath path, PagePath? prefix) => prefix is null
        || string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
        || prefix.Key == "/"
        || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);

    private static string[] ReadLines(string content) {
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

    // The page is shown as it stands against the running build, so a page compiled by an earlier one
    // says Stale rather than repeating diagnostics that are about to be replaced.
    private static string ImageStatus(PageTemplate page, string currentRuntimeId) => PageImageStatus.Describe(
        page.Image?.RuntimeId,
        page.Image?.Bytes is not null,
        page.Image?.Diagnostics.Count > 0,
        currentRuntimeId).Describe();

    private static object[] Diagnostics(PageTemplate page) => page.Image is null
        ? []
        : DiagnosticData(page.Image.Diagnostics);

    private static object[] DiagnosticData(IReadOnlyList<Core.Compilation.PageDiagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => new {
            code = DiagnosticCode(diagnostic),
            severity = diagnostic.Severity.ToString(),
            line = diagnostic.Position?.Line,
            column = diagnostic.Position?.Column,
            message = diagnostic.Message,
        })];

    private static object[] ScriptDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => new {
            line = diagnostic.Position?.Line,
            column = diagnostic.Position?.Column,
            message = diagnostic.Message,
        })];

    private static string? DiagnosticCode(Core.Compilation.PageDiagnostic diagnostic) {
        int separator = diagnostic.Message.IndexOf(':');
        return separator > 0 ? diagnostic.Message[..separator] : null;
    }

    private static int ParseCount(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}
