// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Text.Json;

/// <summary>The arguments of one call, after <see cref="Core.Assistant.ToolArguments"/> has validated them.</summary>
/// <remarks>
/// A tool reads a value by name and gets the CLR type its schema promised, so no tool touches a
/// <see cref="JsonElement"/>. The <c>Required</c> accessors answer a value the schema made mandatory;
/// the parse has already refused a call without it, so reaching one without it is a defect in the
/// table rather than something the model can do.
/// </remarks>
/// <param name="values">The validated arguments.</param>
internal sealed class ToolValues(IReadOnlyDictionary<string, JsonElement> values) {

    /// <summary>A string argument, or null when it was not supplied.</summary>
    /// <param name="name">The argument's name.</param>
    public string? String(string name) =>
        values.TryGetValue(name, out JsonElement value) ? value.GetString() : null;

    /// <summary>An integer argument, or null when it was not supplied.</summary>
    /// <param name="name">The argument's name.</param>
    public int? Integer(string name) =>
        values.TryGetValue(name, out JsonElement value) ? value.GetInt32() : null;

    /// <summary>A boolean argument, which is false when it was not supplied.</summary>
    /// <param name="name">The argument's name.</param>
    public bool Boolean(string name) =>
        values.TryGetValue(name, out JsonElement value) && value.GetBoolean();

    /// <summary>Whether an argument was supplied at all, even when its value is an empty array.</summary>
    /// <param name="name">The argument's name.</param>
    public bool Has(string name) => values.ContainsKey(name);

    /// <summary>The objects in an array argument, each as its own validated argument set.</summary>
    /// <param name="name">The argument's name.</param>
    public IReadOnlyList<ToolValues> Array(string name) {
        if (!values.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array) {
            return [];
        }

        List<ToolValues> items = [];
        foreach (JsonElement item in value.EnumerateArray()) {
            Dictionary<string, JsonElement> properties = [with(StringComparer.Ordinal)];
            foreach (JsonProperty property in item.EnumerateObject()) {
                properties[property.Name] = property.Value.Clone();
            }
            items.Add(new ToolValues(properties));
        }

        return items;
    }

    /// <summary>A required string argument.</summary>
    /// <param name="name">The argument's name.</param>
    public string RequiredString(string name) => String(name) ?? throw Missing(name);

    /// <summary>A required integer argument.</summary>
    /// <param name="name">The argument's name.</param>
    public int RequiredInteger(string name) => Integer(name) ?? throw Missing(name);

    private static InvalidOperationException Missing(string name) =>
        new($"Required tool argument '{name}' is missing.");
}
