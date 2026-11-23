// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text.Json;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;

[TestFixture]
public sealed class ToolResultsTests {

    [Test]
    public void AResultUnderTheLimitIsSentWhole() {
        ToolResults results = new(TestOptions.BudgetOfResultChars(4_096));

        ToolExecutionResult result = results.Success(new { content = new string('x', 100) }, "Fetched");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(Data(result).GetProperty("content").GetString(), Has.Length.EqualTo(100));
            Assert.That(Data(result).TryGetProperty("truncated", out _), Is.False);
        });
    }

    [Test]
    public void AnOversizedResultIsTruncatedWithItsHeadIntactRatherThanRefused() {
        ToolResults results = new(TestOptions.BudgetOfResultChars(4_096));
        string head = new('a', 20_000);

        ToolExecutionResult result = results.Success(new { content = head }, "Fetched");

        using JsonDocument document = JsonDocument.Parse(result.Content);
        JsonElement envelope = document.RootElement;
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, "a refusal would lose the information and the fact that there was information");
            Assert.That(envelope.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(envelope.GetProperty("note").GetString(), Does.Contain("truncated"));
            Assert.That(envelope.GetProperty("data").GetString(), Does.StartWith("{\"ok\":true,\"data\":{\"content\":\"aaaa"),
                "the head of the answer survives, so the model can read what it began with");
        });
    }

    [Test]
    public void ATruncatedResultIsStillValidJson() {
        ToolResults results = new(TestOptions.BudgetOfResultChars(1_024));

        ToolExecutionResult result = results.Success(new { content = "it's a \"quoted\" \\ value" + new string('x', 5_000) }, "Fetched");

        Assert.That(() => JsonDocument.Parse(result.Content), Throws.Nothing);
    }

    [Test]
    public void ARefusalIsNotCutBecauseThereIsNothingToCut() {
        ToolExecutionResult result = ToolResults.Refusal("The provider has not reported the model's window.", "No model window");

        using JsonDocument document = JsonDocument.Parse(result.Content);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(document.RootElement.GetProperty("error").GetString(), Is.EqualTo("The provider has not reported the model's window."));
        });
    }

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }
}
