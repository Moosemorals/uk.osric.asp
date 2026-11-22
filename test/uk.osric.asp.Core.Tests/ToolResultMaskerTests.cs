// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ToolResultMaskerTests {

    [Test]
    public void EveryToolResultIsMaskableOldestFirst() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 9).Select(index => Result(120, $"call-{index}"))];

        Assert.That(ToolResultMasker.MaskableMessages(messages), Is.EqualTo([0, 1, 2, 3, 4, 5, 6, 7, 8]),
            "nothing is exempt, so the newest result is maskable too");
    }

    [Test]
    public void ACallBurstIsMaskableOldestFirst() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 64).Select(index => Call($"call-{index}", LongArguments(1200)))];

        IReadOnlyList<int> maskable = ToolResultMasker.MaskableMessages(messages);

        Assert.Multiple(() => {
            Assert.That(maskable, Has.Count.EqualTo(64), "a 64-call burst leaves nothing exempt");
            Assert.That(maskable[0], Is.Zero);
            Assert.That(maskable[^1], Is.EqualTo(63));
        });
    }

    [Test]
    public void AnOlderResultIsReplacedWithAPlaceholderThatKeepsItsIdAndName() {
        MaskOutcome outcome = Result(120, "call-1").Mask();

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.True);
            Assert.That(outcome.MaskedResults, Is.EqualTo(1));
            Assert.That(outcome.MaskedArguments, Is.Zero);
            Assert.That(outcome.Message.Content, Is.EqualTo("[tool result elided: 120 chars]"));
            Assert.That(outcome.Message.ToolCallId, Is.EqualTo("call-1"));
            Assert.That(outcome.Message.ToolName, Is.EqualTo("read_page"));
        });
    }

    [Test]
    public void AnOldCallKeepsItsNameAndLosesItsLongArgument() {
        ChatMessage message = Call("call-1", LongArguments(1200));

        MaskOutcome outcome = message.Mask();

        using JsonDocument calls = JsonDocument.Parse(outcome.Message.ToolCallsJson!);
        JsonElement call = calls.RootElement[0];
        using JsonDocument arguments = JsonDocument.Parse(call.GetProperty("function").GetProperty("arguments").GetString()!);

        Assert.Multiple(() => {
            Assert.That(outcome.MaskedArguments, Is.EqualTo(1));
            Assert.That(call.GetProperty("id").GetString(), Is.EqualTo("call-1"));
            Assert.That(call.GetProperty("type").GetString(), Is.EqualTo("function"));
            Assert.That(call.GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("write_page"));
            Assert.That(arguments.RootElement.GetProperty("path").GetString(), Is.EqualTo("/blog"));
            Assert.That(arguments.RootElement.GetProperty("content").GetString(), Is.EqualTo("[elided 1200 chars]"));
        });
    }

    [Test]
    public void AResultWhosePlaceholderWouldNotBeSmallerIsKept() {
        MaskOutcome outcome = Result(3, "call-1").Mask();

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(outcome.Message.Content, Is.EqualTo("xxx"));
        });
    }

    [Test]
    public void ALongArgumentStringIsElidedAndItsKeysAndShortValuesSurvive() {
        MaskOutcome outcome = Call("call-1",
            """{"path":"/blog","draft":true,"count":3,"content":"CONTENT"}""".Replace("CONTENT", new string('x', 1200), StringComparison.Ordinal)).Mask();

        using JsonDocument arguments = JsonDocument.Parse(ArgumentsOf(outcome.Message));

        Assert.Multiple(() => {
            Assert.That(outcome.MaskedArguments, Is.EqualTo(1));
            Assert.That(arguments.RootElement.GetProperty("path").GetString(), Is.EqualTo("/blog"));
            Assert.That(arguments.RootElement.GetProperty("draft").GetBoolean(), Is.True);
            Assert.That(arguments.RootElement.GetProperty("count").GetInt32(), Is.EqualTo(3));
            Assert.That(arguments.RootElement.GetProperty("content").GetString(), Is.EqualTo("[elided 1200 chars]"));
        });
    }

    [Test]
    public void ANestedArgumentStringIsElided() {
        MaskOutcome outcome = Call("call-1",
            $$"""{"outer":{"content":"{{new string('x', 1200)}}"},"list":["{{new string('y', 1200)}}"]}""").Mask();

        using JsonDocument arguments = JsonDocument.Parse(ArgumentsOf(outcome.Message));

        Assert.Multiple(() => {
            Assert.That(outcome.MaskedArguments, Is.EqualTo(2));
            Assert.That(arguments.RootElement.GetProperty("outer").GetProperty("content").GetString(),
                Is.EqualTo("[elided 1200 chars]"));
            Assert.That(arguments.RootElement.GetProperty("list")[0].GetString(), Is.EqualTo("[elided 1200 chars]"));
        });
    }

    [Test]
    public void AnArgumentAtTheLimitIsKept() {
        string content = new('x', ToolResultMasker.MaximumArgumentLength);

        MaskOutcome outcome = Call("call-1", $$"""{"content":"{{content}}"}""").Mask();

        Assert.That(outcome.Changed, Is.False);
    }

    [Test]
    public void InvalidArgumentJsonIsLeftAlone() {
        ChatMessage message = Call("call-1", "{not json");

        MaskOutcome outcome = message.Mask();

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(outcome.Message.ToolCallsJson, Is.EqualTo(message.ToolCallsJson));
        });
    }

    [Test]
    public void TheNewestUserMessageIsNeverMasked() {
        ChatMessage[] messages = [new("user", new string('x', 5_000))];

        MaskOutcome outcome = messages[0].Mask();

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(ToolResultMasker.MaskableMessages(messages), Is.Empty);
        });
    }

    [Test]
    public void MaskingTheSameMessageTwiceGivesTheSameBytes() {
        MaskOutcome once = Result(120, "call-1").Mask();
        MaskOutcome twice = once.Message.Mask();

        Assert.Multiple(() => {
            Assert.That(twice.Changed, Is.False);
            Assert.That(twice.Message, Is.EqualTo(once.Message));
        });
    }

    private static ChatMessage Result(int length, string id) =>
        new("tool", new string('x', length), ToolCallId: id, ToolName: "read_page");

    private static ChatMessage Call(string id, string arguments) => new(
        "assistant",
        "",
        JsonSerializer.Serialize(new[] {
            new { id, type = "function", function = new { name = "write_page", arguments } },
        }));

    [Test]
    public void AMaskedCallKeepsItsMarkupAndNonAsciiText() {
        string arguments = JsonSerializer.Serialize(new {
            path = "/café",
            content = new string('x', 2_000),
            note = "<b>bold</b>",
        });

        MaskOutcome outcome = Call("call-1", arguments).Mask();

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.True, "the long content is elided");
            Assert.That(outcome.Message.ToolCallsJson, Does.Contain("<b>bold</b>"));
            Assert.That(outcome.Message.ToolCallsJson, Does.Contain("café"));
            Assert.That(outcome.Message.ToolCallsJson, Does.Not.Contain("\\u003C"));
            Assert.That(outcome.Message.ToolCallsJson, Does.Not.Contain("\\u00E9"));
        });
    }

    private static string LongArguments(int length) =>
        $$"""{"path":"/blog","content":"{{new string('x', length)}}"}""";

    private static string ArgumentsOf(ChatMessage message) {
        using JsonDocument calls = JsonDocument.Parse(message.ToolCallsJson!);
        return calls.RootElement[0].GetProperty("function").GetProperty("arguments").GetString()!;
    }
}
