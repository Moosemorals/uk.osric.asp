// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;

/// <summary>A flagged tool call the owner is asked to allow or refuse.</summary>
/// <param name="Name">The tool the call names.</param>
/// <param name="Description">What the tool does, as the model was told.</param>
/// <param name="Arguments">The call's arguments, exactly as the model sent them.</param>
internal sealed record ApprovalRequest(string Name, string Description, string Arguments);

/// <summary>A spent budget the owner is asked to top up.</summary>
/// <param name="Budget">Which budget ran out, in <see cref="RunBudget"/>'s words.</param>
/// <param name="Limit">The limit the run reached.</param>
/// <param name="Window">What continuing buys.</param>
/// <param name="Message">The sentence the card shows.</param>
internal sealed record ContinuationRequest(string Budget, int Limit, int Window, string Message);

/// <summary>What the owner decided, for the kinds whose answer is a choice.</summary>
/// <param name="Decision">The word the kind's form asked for.</param>
internal sealed record OwnerResolution(string Decision);

/// <summary>Reads and writes the two halves of an interrupt, so no caller hand-builds one.</summary>
/// <remarks>
/// The shapes live here rather than on the record because the record is deliberately opaque: the store
/// never parses a request or a resolution, which is what lets a new kind arrive with no schema change.
/// A row this build cannot read is treated as absent rather than as an error, because a transcript is
/// more useful than a stack trace.
/// </remarks>
internal static class InterruptPayloads {

    /// <summary>The stored form of a request or a resolution.</summary>
    /// <param name="payload">The value to store.</param>
    public static string Write(object payload) => JsonSerializer.Serialize(payload, JsonSerialization.Options);

    /// <summary>The approval a request describes, or null when it is not one this build understands.</summary>
    /// <param name="requestJson">The stored request.</param>
    public static ApprovalRequest? Approval(string? requestJson) => Read<ApprovalRequest>(requestJson);

    /// <summary>The continuation a request describes, or null when it is not one this build understands.</summary>
    /// <param name="requestJson">The stored request.</param>
    public static ContinuationRequest? Continuation(string? requestJson) => Read<ContinuationRequest>(requestJson);

    /// <summary>The questions a request describes, or null when it is not one this build understands.</summary>
    /// <param name="requestJson">The stored request.</param>
    public static OwnerQuestionSet? Question(string? requestJson) => OwnerQuestions.Read(requestJson);

    /// <summary>The answers a resolution records, or null when it is not one this build understands.</summary>
    /// <param name="resolutionJson">The stored resolution.</param>
    public static OwnerAnswerSet? Answers(string? resolutionJson) => OwnerQuestions.ReadAnswers(resolutionJson);

    /// <summary>The decision a resolution records, or null when it is not one this build understands.</summary>
    /// <param name="resolutionJson">The stored resolution.</param>
    public static OwnerResolution? Resolution(string? resolutionJson) => Read<OwnerResolution>(resolutionJson);

    private static T? Read<T>(string? json) where T : class {
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<T>(json, JsonSerialization.Options);
        } catch (JsonException) {
            return null;
        }
    }
}
