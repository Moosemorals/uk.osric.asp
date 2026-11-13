// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;
using uk.osric.asp.Core.Text;

/// <summary>The JSON value kind accepted by one tool argument.</summary>
public enum ToolArgumentType {
    /// <summary>A JSON string.</summary>
    String,
    /// <summary>A signed 32-bit JSON integer.</summary>
    Integer,
    /// <summary>A JSON boolean.</summary>
    Boolean,
    /// <summary>A JSON array of objects whose properties <see cref="ToolArgumentSchema.Items"/> describes.</summary>
    Array,
}

/// <summary>One allowed argument in a tool schema.</summary>
/// <param name="Name">The exact property name.</param>
/// <param name="Type">The JSON value type.</param>
/// <param name="Required">Whether the property must be present.</param>
public sealed record ToolArgumentSchema(string Name, ToolArgumentType Type, bool Required) {
    /// <summary>
    /// The properties of each element when <see cref="Type"/> is <see cref="ToolArgumentType.Array"/>;
    /// null for every other type.
    /// </summary>
    public IReadOnlyList<ToolArgumentSchema>? Items { get; init; }
}

/// <summary>A validated argument object or a safe validation error.</summary>
/// <param name="Values">Cloned JSON values for the declared properties.</param>
/// <param name="Error">A safe validation failure, or null when valid.</param>
public sealed record ToolArgumentsResult(IReadOnlyDictionary<string, JsonElement> Values, string? Error) {
    /// <summary>Whether the argument object passed validation.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>Validates strict, bounded JSON objects used as tool arguments.</summary>
public static class ToolArguments {

    /// <summary>Parses an object against an explicit allow-list and type schema.</summary>
    /// <param name="json">The provider-supplied argument JSON.</param>
    /// <param name="schema">The complete set of accepted argument names and types.</param>
    /// <param name="maximumChars">The maximum serialized argument length.</param>
    public static ToolArgumentsResult Parse(
        string json,
        IReadOnlyList<ToolArgumentSchema> schema,
        int maximumChars) {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        if (json.Length > maximumChars) {
            return Invalid("Tool arguments exceed the configured size limit.");
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            return Invalid("Tool arguments must be a JSON object.");
        }

        using (document) {
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return Invalid("Tool arguments must be a JSON object.");
            }

            string? error = ValidateObject(document.RootElement, schema, parent: null);
            if (error is not null) {
                return Invalid(error);
            }

            Dictionary<string, JsonElement> values = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
                values[property.Name] = property.Value.Clone();
            }

            return new ToolArgumentsResult(values, null);
        }

        static ToolArgumentsResult Invalid(string error) => new(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal), error);
    }

    private static string? ValidateObject(
        JsonElement element,
        IReadOnlyList<ToolArgumentSchema> schema,
        string? parent) {
        if (element.ValueKind != JsonValueKind.Object) {
            return $"Tool argument '{parent}' must be an object.";
        }

        Dictionary<string, ToolArgumentSchema> allowed = schema.ToDictionary(item => item.Name, StringComparer.Ordinal);
        HashSet<string> present = [with(StringComparer.Ordinal)];
        foreach (JsonProperty property in element.EnumerateObject()) {
            if (!allowed.TryGetValue(property.Name, out ToolArgumentSchema? definition)) {
                return $"Unknown tool argument '{MessageText.Sanitize(Qualify(parent, property.Name))}'.";
            }
            if (!present.Add(property.Name)) {
                return $"Tool argument '{Qualify(parent, property.Name)}' was repeated.";
            }

            string? error = ValidateValue(property.Value, definition, Qualify(parent, property.Name));
            if (error is not null) {
                return error;
            }
        }

        foreach (ToolArgumentSchema definition in schema) {
            if (definition.Required && !present.Contains(definition.Name)) {
                return $"Required tool argument '{Qualify(parent, definition.Name)}' is missing.";
            }
        }

        return null;
    }

    private static string? ValidateValue(JsonElement value, ToolArgumentSchema definition, string name) => definition.Type switch {
        ToolArgumentType.String => value.ValueKind == JsonValueKind.String
            ? null
            : $"Tool argument '{name}' must be a string.",
        ToolArgumentType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)
            ? null
            : $"Tool argument '{name}' must be an integer.",
        ToolArgumentType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? null
            : $"Tool argument '{name}' must be a boolean.",
        ToolArgumentType.Array => ValidateArray(value, definition.Items, name),
        _ => null,
    };

    private static string? ValidateArray(JsonElement value, IReadOnlyList<ToolArgumentSchema>? items, string name) {
        if (value.ValueKind != JsonValueKind.Array) {
            return $"Tool argument '{name}' must be an array.";
        }

        IReadOnlyList<ToolArgumentSchema> schema = items ?? [];
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray()) {
            string elementName = $"{name}[{index}]";
            if (item.ValueKind != JsonValueKind.Object) {
                return $"Tool argument '{elementName}' must be an object.";
            }

            string? error = ValidateObject(item, schema, elementName);
            if (error is not null) {
                return error;
            }
            index++;
        }

        return null;
    }

    private static string Qualify(string? parent, string name) => parent is null ? name : $"{parent}.{name}";
}
