// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Text.Json;

/// <summary>Writes the tool result envelope, so no tool can bypass the configured bound.</summary>
/// <param name="options">The assistant's configured limits.</param>
internal sealed class ToolResults(AssistantOptions options) {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A result carrying data.</summary>
    /// <param name="data">What the tool produced.</param>
    /// <param name="summary">One line for the transcript.</param>
    public ToolExecutionResult Success(object data, string summary) =>
        Bounded(new { ok = true, data, error = (string?)null }, summary);

    /// <summary>A refusal, or a call that could not be completed.</summary>
    /// <param name="error">The refusal.</param>
    /// <param name="summary">One line for the transcript.</param>
    public ToolExecutionResult Failure(string error, string summary) =>
        Bounded(new { ok = false, data = (object?)null, error }, summary, success: false);

    /// <summary>A result, unless it is over the configured size and must be replaced by a refusal.</summary>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <param name="summary">One line for the transcript.</param>
    /// <param name="success">Whether the call did what it was asked.</param>
    public ToolExecutionResult Bounded(object envelope, string summary, bool success = true) {
        string json = JsonSerializer.Serialize(envelope, s_jsonOptions);
        return json.Length <= options.MaxToolResultChars
            ? new ToolExecutionResult(success, json, summary)
            : new ToolExecutionResult(false,
                "{\"ok\":false,\"data\":null,\"error\":\"Tool result exceeds the configured size limit.\"}",
                "Tool result too large");
    }
}
