// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;
using uk.osric.asp.Core.Text;

/// <summary>The JSON value kind accepted by one tool argument.</summary>
public enum AssistantToolArgumentType {
    /// <summary>A JSON string.</summary>
    String,
    /// <summary>A signed 32-bit JSON integer.</summary>
    Integer,
    /// <summary>A JSON boolean.</summary>
    Boolean,
}

/// <summary>One allowed argument in a tool schema.</summary>
/// <param name="Name">The exact property name.</param>
/// <param name="Type">The JSON value type.</param>
/// <param name="Required">Whether the property must be present.</param>
public sealed record AssistantToolArgumentSchema(string Name, AssistantToolArgumentType Type, bool Required);

/// <summary>A validated argument object or a safe validation error.</summary>
/// <param name="Values">Cloned JSON values for the declared properties.</param>
/// <param name="Error">A safe validation failure, or null when valid.</param>
public sealed record AssistantToolArgumentsResult(IReadOnlyDictionary<string, JsonElement> Values, string? Error) {
    /// <summary>Whether the argument object passed validation.</summary>
    public bool Success => Error is null;
}

/// <summary>Validates strict, bounded JSON objects used as tool arguments.</summary>
public static class AssistantToolArguments {

    /// <summary>Parses an object against an explicit allow-list and type schema.</summary>
    /// <param name="json">The provider-supplied argument JSON.</param>
    /// <param name="schema">The complete set of accepted argument names and types.</param>
    /// <param name="maximumChars">The maximum serialized argument length.</param>
    public static AssistantToolArgumentsResult Parse(
        string json,
        IReadOnlyList<AssistantToolArgumentSchema> schema,
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

            Dictionary<string, AssistantToolArgumentSchema> allowed = schema.ToDictionary(item => item.Name, StringComparer.Ordinal);
            Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
                if (!allowed.TryGetValue(property.Name, out AssistantToolArgumentSchema? definition)) {
                    return Invalid($"Unknown tool argument '{MessageText.Sanitize(property.Name)}'.");
                }
                if (!values.TryAdd(property.Name, property.Value.Clone())) {
                    return Invalid($"Tool argument '{property.Name}' was repeated.");
                }
                if (!Matches(property.Value, definition.Type)) {
                    return Invalid($"Tool argument '{property.Name}' must be a {TypeName(definition.Type)}.");
                }
            }

            foreach (AssistantToolArgumentSchema definition in schema) {
                if (definition.Required && !values.ContainsKey(definition.Name)) {
                    return Invalid($"Required tool argument '{definition.Name}' is missing.");
                }
            }

            return new AssistantToolArgumentsResult(values, null);
        }

        static AssistantToolArgumentsResult Invalid(string error) => new(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal), error);
    }

    private static bool Matches(JsonElement value, AssistantToolArgumentType type) => type switch {
        AssistantToolArgumentType.String => value.ValueKind == JsonValueKind.String,
        AssistantToolArgumentType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
        AssistantToolArgumentType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false,
    };

    private static string TypeName(AssistantToolArgumentType type) => type switch {
        AssistantToolArgumentType.String => "string",
        AssistantToolArgumentType.Integer => "integer",
        AssistantToolArgumentType.Boolean => "boolean",
        _ => "supported value",
    };
}
