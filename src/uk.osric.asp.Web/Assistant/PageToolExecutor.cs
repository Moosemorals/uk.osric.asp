// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>What one tool call produced, as the model receives it.</summary>
internal sealed record ToolExecutionResult(bool IsSuccess, string Content, string Summary);

/// <summary>Runs the page tools the model asks for: read, write, list and delete.</summary>
internal sealed class PageToolExecutor(
    IServiceScopeFactory scopeFactory,
    PagePathPolicy paths,
    AssistantOptions options) {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, ToolArgumentSchema[]> s_schemas =
        new Dictionary<string, ToolArgumentSchema[]>(StringComparer.Ordinal) {
            ["list_pages"] = [new("prefix", ToolArgumentType.String, false), new("includeDeleted", ToolArgumentType.Boolean, false)],
            ["read_page"] = [new("path", ToolArgumentType.String, true), new("startLine", ToolArgumentType.Integer, false), new("endLine", ToolArgumentType.Integer, false)],
            ["edit_page"] = [new("path", ToolArgumentType.String, true), new("startLine", ToolArgumentType.Integer, true), new("endLine", ToolArgumentType.Integer, true), new("expectedRevisionHash", ToolArgumentType.String, true), new("replacement", ToolArgumentType.String, true)],
            ["write_page"] = [new("path", ToolArgumentType.String, true), new("content", ToolArgumentType.String, true)],
            ["delete_page"] = [new("path", ToolArgumentType.String, true)],
            ["search_pages"] = [new("query", ToolArgumentType.String, true), new("prefix", ToolArgumentType.String, false), new("caseSensitive", ToolArgumentType.Boolean, false)],
            ["shell"] = [new("command", ToolArgumentType.String, true)],
        };

    public static IReadOnlyList<ToolDefinition> Definitions { get; } = [
        Definition("list_pages", "List live page keys under an optional prefix.", [new("prefix", "string", false), new("includeDeleted", "boolean", false)], false),
        Definition("read_page", "Read a page, its revision hash, compile status and diagnostics. Optional 1-based line numbers refer to physical lines of the stored template source.", [new("path", "string", true), new("startLine", "integer", false), new("endLine", "integer", false)], false),
        Definition("edit_page", "Replace an inclusive range of 1-based physical template-source lines if the current revision hash still matches. Lines include literal HTML and template delimiters as authored, not generated C# or rendered HTML.", [new("path", "string", true), new("startLine", "integer", true), new("endLine", "integer", true), new("expectedRevisionHash", "string", true), new("replacement", "string", true)], true),
        Definition("write_page", "Create or deliberately replace a complete page through the normal compile-and-save path.", [new("path", "string", true), new("content", "string", true)], true),
        Definition("delete_page", "Tombstone a page while retaining its revisions.", [new("path", "string", true)], true),
        Definition("search_pages", "Search live page source for a literal substring.", [new("query", "string", true), new("prefix", "string", false), new("caseSensitive", "boolean", false)], false),
        Definition("shell", "Run a read-only virtual-shell command over stored pages.", [new("command", "string", true)], false),
    ];

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
        IReadOnlyList<PageHeadSummary> all = await pages.ListAsync(cancellationToken);
        PageHeadSummary[] matching = [.. all
            .Where(item => !paths.IsProtected(item.Path)
                && (includeDeleted || !item.IsDeleted)
                && IsUnderPrefix(item.Path, prefix))
            .OrderBy(item => item.Path.Key, StringComparer.Ordinal)];
        bool truncated = matching.Length > 500;
        object data = new {
            pages = matching.Take(500).Select(item => new { path = item.Path.Key, deleted = item.IsDeleted }),
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
            compileStatus = ImageStatus(page),
            diagnostics = Diagnostics(page),
            startLine = start,
            endLine = end,
            totalLines = lines.Length,
            truncated,
            source,
        };
        return Success(data, $"Read {Describe(path)}");
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

        IReadOnlyList<uk.osric.asp.Core.Compilation.PageDiagnostic> diagnostics =
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
        PagePath? prefix = OptionalPrefix(String(arguments, "prefix"), out string? prefixError);
        if (prefixError is not null) {
            return Failure(prefixError, "Search refused");
        }

        StringComparison comparison = Boolean(arguments, "caseSensitive")
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        List<object> matches = [];
        foreach (PageHeadSummary head in await pages.ListAsync(cancellationToken)) {
            if (head.IsDeleted || paths.IsProtected(head.Path) || !IsUnderPrefix(head.Path, prefix)) {
                continue;
            }
            PageTemplate? page = await pages.GetAsync(head.Path, cancellationToken);
            if (page is null) {
                continue;
            }
            string[] lines = ReadLines(page.Template);
            for (int index = 0; index < lines.Length && matches.Count < 100; index++) {
                if (lines[index].Contains(query, comparison)) {
                    matches.Add(new { path = head.Path.Key, line = index + 1, text = lines[index][..Math.Min(lines[index].Length, 240)] });
                }
            }
            if (matches.Count == 100) {
                break;
            }
        }

        return Success(new { query, matches, truncated = matches.Count == 100 }, $"Found {matches.Count} matches");
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
        string? prefixText = command.Name is "ls" or "find" ? command.Arguments.FirstOrDefault()
            : command.Name == "grep"
                ? command.Arguments.Count > (command.Arguments[0] == "-i" ? 2 : 1) ? command.Arguments[^1] : null
                : null;
        PagePath? prefix = OptionalPrefix(prefixText, out string? prefixError);
        if (prefixError is not null) {
            return Failure(prefixError, "Shell command refused");
        }

        if (command.Name is "ls" or "find") {
            IReadOnlyList<PageHeadSummary> heads = await pages.ListAsync(cancellationToken);
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
            List<string> found = [];
            foreach (PageHeadSummary head in await pages.ListAsync(cancellationToken)) {
                if (head.IsDeleted || paths.IsProtected(head.Path) || !IsUnderPrefix(head.Path, prefix)) {
                    continue;
                }
                PageTemplate? page = await pages.GetAsync(head.Path, cancellationToken);
                if (page is null) {
                    continue;
                }
                string[] lines = ReadLines(page.Template);
                for (int index = 0; index < lines.Length && found.Count < 100; index++) {
                    if (lines[index].Contains(query, comparison)) {
                        found.Add($"{head.Path.Key}:{index + 1}: {lines[index]}");
                    }
                }
                if (found.Count == 100) {
                    break;
                }
            }
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
            "wc" => sourceLines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "stat" => $"{checkedPath.Path!.Key}\n{target.Template.Length} chars\n{target.Hash}\n{target.SavedAt:O}\n{ImageStatus(target)}",
            _ => "",
        };
        if (output.Length > options.MaxToolResultChars) {
            output = output[..options.MaxToolResultChars] + "\n[truncated]";
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

    private PagePath? OptionalPrefix(string? text, out string? error) {
        error = null;
        if (text is null) {
            return null;
        }
        PagePath prefix = PagePath.Normalize(text);
        if (!prefix.IsAddressable) {
            error = "The prefix is not an addressable page path.";
            return null;
        }
        return prefix;
    }

    private static ToolDefinition Definition(
        string name,
        string description,
        (string Name, string Type, bool Required)[] properties,
        bool needsApproval) {
        Dictionary<string, object> propertySchemas = new(StringComparer.Ordinal);
        foreach ((string propertyName, string type, _) in properties) {
            propertySchemas[propertyName] = new { type };
        }
        string[] required = [.. properties.Where(property => property.Required).Select(property => property.Name)];
        string parameters = JsonSerializer.Serialize(new {
            type = "object",
            properties = propertySchemas,
            required,
            additionalProperties = false,
        }, s_jsonOptions);
        return new ToolDefinition(name, description, parameters, needsApproval);
    }

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

    private static string ImageStatus(PageTemplate page) => page.Image switch {
        null => "Not compiled",
        { Bytes: null } => "Compile failed",
        { Diagnostics.Count: > 0 } => "Compiled with diagnostics",
        _ => "Compiled",
    };

    private static object[] Diagnostics(PageTemplate page) => page.Image is null
        ? []
        : DiagnosticData(page.Image.Diagnostics);

    private static object[] DiagnosticData(IReadOnlyList<uk.osric.asp.Core.Compilation.PageDiagnostic> diagnostics) => [.. diagnostics.Select(diagnostic => new {
        code = DiagnosticCode(diagnostic),
        severity = diagnostic.Severity.ToString(),
        line = diagnostic.Position?.Line,
        column = diagnostic.Position?.Column,
        message = diagnostic.Message,
    })];

    private static string? DiagnosticCode(uk.osric.asp.Core.Compilation.PageDiagnostic diagnostic) {
        int separator = diagnostic.Message.IndexOf(':');
        return separator > 0 ? diagnostic.Message[..separator] : null;
    }

    private static int ParseCount(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
