// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

/// <summary>The assistant's tools, in declaration order, by the name that calls them.</summary>
/// <remarks>
/// The table BusyBox keeps: one row per tool, and everything about a tool is on its type. The model's
/// <c>Definitions</c>, the dispatcher, the approval gate and the transcript's "did this change a page"
/// question all read this one list, so a tool is unreachable only by being unregistered. The order is
/// not sorted: it is part of the request body and so of the provider's cached prefix.
/// </remarks>
internal sealed class ToolRegistry {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, AssistantTool> _byName;

    /// <summary>Builds the table from the tools, keeping the order they were declared in.</summary>
    /// <param name="tools">Every tool the assistant answers.</param>
    public ToolRegistry(params AssistantTool[] tools) {
        ArgumentNullException.ThrowIfNull(tools);

        All = [.. tools];
        _byName = All.ToDictionary(tool => tool.Spec.Name, StringComparer.Ordinal);
        Definitions = [.. All.Select(tool => new ToolDefinition(
            tool.Spec.Name,
            tool.Spec.Description,
            tool.Spec.NeedsApproval,
            ParametersJson(tool.Spec.Arguments)))];
    }

    /// <summary>The assistant's own tools.</summary>
    public static ToolRegistry Default { get; } = new(
        new ListPagesTool(),
        new ReadPageTool(),
        new DiffPageTool(),
        new EditPageTool(),
        new WritePageTool(),
        new DeletePageTool(),
        new SearchPagesTool(),
        new CheckScriptTool(),
        new CheckScriptTextTool(),
        new FetchTool(),
        new ShellTool(),
        new RememberTool(),
        new UpdateMemoryTool(),
        new ForgetMemoryTool(),
        new SearchMemoriesTool());

    /// <summary>Every tool, in declaration order.</summary>
    public IReadOnlyList<AssistantTool> All { get; }

    /// <summary>What the model is offered, derived from the same list in the same order.</summary>
    public IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <summary>The tool a name calls, or null when the assistant does not answer it.</summary>
    /// <param name="name">The tool name.</param>
    public AssistantTool? Find(string name) => _byName.GetValueOrDefault(name);

    private static string ParametersJson(IReadOnlyList<ToolArgumentSchema> arguments) {
        Dictionary<string, object> properties = [];
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
}
