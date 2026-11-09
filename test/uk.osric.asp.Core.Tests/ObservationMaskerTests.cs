// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ObservationMaskerTests {

    [Test]
    public void TheNewestEightResultsAreKept() {
        ChatMessage[] messages = [.. Enumerable.Range(0, 9).Select(index => Result(120, $"call-{index}"))];

        IReadOnlyList<int> maskable = ObservationMasker.MaskableMessages(messages);

        Assert.Multiple(() => {
            Assert.That(maskable, Is.EqualTo([0]), "only the ninth-newest result is outside the window");
            Assert.That(ObservationMasker.MaskableMessages(messages[1..]), Is.Empty,
                "the eight newest results are kept");
        });
    }

    [Test]
    public void TheNewestTwoCallsKeepTheirArguments() {
        ChatMessage[] messages = [
            Call("call-1", LongArguments(1200)),
            Call("call-2", LongArguments(1200)),
            Call("call-3", LongArguments(1200)),
        ];

        Assert.That(ObservationMasker.MaskableMessages(messages), Is.EqualTo([0]));
    }

    [Test]
    public void AnOlderResultIsReplacedWithAPlaceholderThatKeepsItsIdAndName() {
        MaskOutcome outcome = ObservationMasker.MaskMessage(Result(120, "call-1"));

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
        ChatMessage[] messages = [
            Call("call-1", """{"path":"/blog","content":"CONTENT"}""".Replace("CONTENT", new string('x', 1200), StringComparison.Ordinal)),
            Call("call-2", LongArguments(1200)),
            Call("call-3", LongArguments(1200)),
        ];

        MaskOutcome outcome = ObservationMasker.MaskMessage(messages[ObservationMasker.MaskableMessages(messages)[0]]);

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
        MaskOutcome outcome = ObservationMasker.MaskMessage(Result(3, "call-1"));

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(outcome.Message.Content, Is.EqualTo("xxx"));
        });
    }

    [Test]
    public void ALongArgumentStringIsElidedAndItsKeysAndShortValuesSurvive() {
        MaskOutcome outcome = ObservationMasker.MaskMessage(Call("call-1",
            """{"path":"/blog","draft":true,"count":3,"content":"CONTENT"}""".Replace("CONTENT", new string('x', 1200), StringComparison.Ordinal)));

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
        MaskOutcome outcome = ObservationMasker.MaskMessage(Call("call-1",
            $$"""{"outer":{"content":"{{new string('x', 1200)}}"},"list":["{{new string('y', 1200)}}"]}"""));

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
        string content = new('x', ObservationMasker.MaximumArgumentLength);

        MaskOutcome outcome = ObservationMasker.MaskMessage(Call("call-1", $$"""{"content":"{{content}}"}"""));

        Assert.That(outcome.Changed, Is.False);
    }

    [Test]
    public void InvalidArgumentJsonIsLeftAlone() {
        ChatMessage message = Call("call-1", "{not json");

        MaskOutcome outcome = ObservationMasker.MaskMessage(message);

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(outcome.Message.ToolCallsJson, Is.EqualTo(message.ToolCallsJson));
        });
    }

    [Test]
    public void TheNewestUserMessageIsNeverMasked() {
        ChatMessage[] messages = [new("user", new string('x', 5_000))];

        MaskOutcome outcome = ObservationMasker.MaskMessage(messages[0]);

        Assert.Multiple(() => {
            Assert.That(outcome.Changed, Is.False);
            Assert.That(ObservationMasker.MaskableMessages(messages), Is.Empty);
        });
    }

    [Test]
    public void MaskingTheSameMessageTwiceGivesTheSameBytes() {
        MaskOutcome once = ObservationMasker.MaskMessage(Result(120, "call-1"));
        MaskOutcome twice = ObservationMasker.MaskMessage(once.Message);

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

    private static string LongArguments(int length) =>
        $$"""{"path":"/blog","content":"{{new string('x', length)}}"}""";

    private static string ArgumentsOf(ChatMessage message) {
        using JsonDocument calls = JsonDocument.Parse(message.ToolCallsJson!);
        return calls.RootElement[0].GetProperty("function").GetProperty("arguments").GetString()!;
    }
}
