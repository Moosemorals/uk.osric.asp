// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>
/// The budget row the page draws and the tool call the transcript shows read the same stored JSON,
/// so a change to how either parses it cannot pass unnoticed.
/// </summary>
[TestFixture]
public sealed class AssistantBudgetTests {

    [Test]
    public void TheSnapshotAndTheDisplayedToolCallsReadTheSameJson() {
        Message[] messages = [
            StoredCall("call-1", """{"path":"/one"}"""),
            StoredCall("call-2", """{"path":"/a-longer-one","nested":{"value":1}}"""),
        ];

        BudgetSnapshot snapshot = BudgetSnapshot.Measure(
            [.. messages.Select(message => message.ToChatMessage())], 1, 2, null);
        Dictionary<string, (string Name, string Arguments)> calls = AssistantEndpoint.ReadToolCalls(messages);

        Assert.Multiple(() => {
            Assert.That(calls, Has.Count.EqualTo(2), "the page renders a name for every stored call");
            Assert.That(snapshot.LongestArguments, Is.EqualTo(calls.Values.Max(call => call.Arguments.Length)),
                "the budget row and the transcript must count the same arguments");
        });
    }

    private static Message StoredCall(string id, string arguments) => new() {
        RunId = "run",
        Role = "assistant",
        Content = string.Empty,
        ToolCallsJson = JsonSerializer.Serialize(new[] {
            new { id, type = "function", function = new { name = "read_page", arguments } },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };
}
