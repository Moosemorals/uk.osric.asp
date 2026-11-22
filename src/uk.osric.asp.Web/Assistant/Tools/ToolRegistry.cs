// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;

/// <summary>The assistant's tools, in declaration order, by the name that calls them.</summary>
/// <remarks>
/// The table BusyBox keeps: one row per tool, and everything about a tool is on its type. The model's
/// <c>Definitions</c>, the dispatcher, the approval gate and the transcript's "did this change a page"
/// question all read this one list, so a tool is unreachable only by being unregistered. The order is
/// not sorted: it is part of the request body and so of the provider's cached prefix.
/// </remarks>
internal sealed class ToolRegistry {

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
        new ListRevisionsTool(),
        new RestoreRevisionTool(),
        new EditPageTool(),
        new WritePageTool(),
        new DeletePageTool(),
        new MovePageTool(),
        new SearchPagesTool(),
        new CheckTool(),
        new FetchTool(),
        new ViewTool(),
        new ShellTool(),
        new RememberTool(),
        new UpdateMemoryTool(),
        new ForgetMemoryTool(),
        new SearchMemoriesTool(),
        new ReferenceTool(),
        new AskUserTool());

    /// <summary>Every tool, in declaration order.</summary>
    public IReadOnlyList<AssistantTool> All { get; }

    /// <summary>What the model is offered, derived from the same list in the same order.</summary>
    public IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <summary>The tool a name calls, or null when the assistant does not answer it.</summary>
    /// <param name="name">The tool name.</param>
    public AssistantTool? Find(string name) => _byName.GetValueOrDefault(name);

    private static string ParametersJson(IReadOnlyList<ToolArgumentSchema> arguments) =>
        JsonSerializer.Serialize(ObjectSchema(arguments), JsonSerialization.Options);

    private static object ObjectSchema(IReadOnlyList<ToolArgumentSchema> arguments) {
        Dictionary<string, object> properties = [];
        foreach (ToolArgumentSchema argument in arguments) {
            properties[argument.Name] = ValueSchema(argument);
        }

        string[] required = [.. arguments.Where(argument => argument.Required).Select(argument => argument.Name)];

        return new {
            type = "object",
            properties,
            required,
            additionalProperties = false,
        };
    }

    private static Dictionary<string, object?> ValueSchema(ToolArgumentSchema argument) {
        Dictionary<string, object?> schema = new(StringComparer.Ordinal) {
            ["type"] = JsonTypeOf(argument.Type),
        };
        if (argument.Type == ToolArgumentType.Array) {
            schema["items"] = ObjectSchema(argument.Items ?? []);
        }
        // Emitted only when it is set, so every tool that describes no argument sends the bytes it
        // has always sent and the provider's cached prefix does not move.
        if (argument.Description is { } description) {
            schema["description"] = description;
        }
        // A closed set is a constraint rather than a sentence, so a language that arrives later
        // costs a row in the set and no edit to a description.
        if (argument.Values is { Count: > 0 } values) {
            schema["enum"] = values;
        }

        return schema;
    }

    private static string JsonTypeOf(ToolArgumentType type) => type switch {
        ToolArgumentType.String => "string",
        ToolArgumentType.Integer => "integer",
        ToolArgumentType.Boolean => "boolean",
        ToolArgumentType.Array => "array",
        _ => "string",
    };
}
