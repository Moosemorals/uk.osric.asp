// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ContextTrimmerTests {

    [Test]
    public void KeepsTheSystemPromptAndNewestUserTurn() {
        ChatRequest request = Request([
            new("system", "prompt"),
            new("user", "old"),
            new("assistant", "old answer"),
            new("user", "latest"),
        ]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([
            new("system", "prompt"),
            new("user", "latest"),
        ])));

        Assert.That(result.Request.Messages.Select(message => message.Content), Is.EqualTo(["prompt", "latest"]));
        Assert.That(result.IsTrimmed, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void DropsToolCallTurnsAsAUnit() {
        ChatRequest request = Request([
            new("system", "s"),
            new("user", "old"),
            new("assistant", "", """[{"id":"call-1"}]"""),
            new("tool", "result", ToolCallId: "call-1"),
            new("assistant", "old answer"),
            new("user", "new"),
        ]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([
            new("system", "s"),
            new("user", "new"),
        ])));

        Assert.That(result.Request.Messages.Select(message => message.Content), Is.EqualTo(["s", "new"]));
        Assert.That(result.Request.Messages.Any(message => message.Role == "tool"), Is.False);
    }

    [Test]
    public void ReportsWhenTheNewestTurnCannotFit() {
        ChatRequest request = Request([new("system", "prompt"), new("user", "too long")]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([new("system", "prompt")])));

        Assert.Multiple(() => {
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Request.Messages.Select(message => message.Role), Is.EqualTo(["system", "user"]),
                "the smallest request is still reported so the page can show its size");
            Assert.That(result.SentBytes, Is.EqualTo(Bytes(result.Request)));
        });
    }

    [Test]
    public void KeepsTheNewestWholeTurnWhenAnOlderTurnMustBeDropped() {
        ChatRequest request = Request([
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
            new("user", "c"), new("assistant", "d"),
        ]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([
            new("system", "s"),
            new("user", "c"), new("assistant", "d"),
        ])));

        Assert.Multiple(() => {
            Assert.That(result.Request.Messages.Select(message => message.Content), Is.EqualTo(["s", "c", "d"]));
            Assert.That(result.DroppedMessages, Is.EqualTo(2));
        });
    }

    [Test]
    public void KeepsEveryTurnWhenTheBudgetAllows() {
        ChatRequest request = Request([
            new("system", "s"),
            new("user", "a"), new("assistant", "b"),
        ]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(request));

        Assert.Multiple(() => {
            Assert.That(result.Request.Messages.Select(message => message.Content), Is.EqualTo(["s", "a", "b"]));
            Assert.That(result.Error, Is.Null);
            Assert.That(result.DroppedMessages, Is.Zero);
            Assert.That(result.IsTrimmed, Is.False);
            Assert.That(result.TrimmingNote, Is.Null);
        });
    }

    [Test]
    public void ATranscriptWithoutASystemPromptKeepsTheNewestTurn() {
        ChatRequest request = Request([new("assistant", "a"), new("user", "b")]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([new("user", "b")])));

        Assert.Multiple(() => {
            Assert.That(result.Request.Messages.Select(message => message.Content), Is.EqualTo(["b"]));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public void ReportsWhenTheSystemPromptAloneIsTooLong() {
        ChatRequest request = Request([new("system", "prompt")]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(request) - 1);

        Assert.That(result.Error, Is.Not.Null.And.Contains("needs").And.Contains("bytes"));
    }

    [Test]
    public void ReportsWhenTheSystemFitsButTheNewestTurnDoesNot() {
        ChatRequest request = Request([new("system", "s"), new("user", "too long")]);

        ContextTrimResult result = ContextTrimmer.Trim(request, Bytes(Request([new("system", "s")])));

        Assert.That(result.Error, Is.Not.Null, "the newest turn may not be split from its result");
    }

    [Test]
    public void TheBudgetIsMeasuredInBytes() {
        ChatRequest request = Request([new("system", "s"), new("user", "0123456789")]);

        Assert.Multiple(() => {
            Assert.That(ContextTrimmer.Trim(request, Bytes(request) - 1).Error, Is.Not.Null);
            Assert.That(ContextTrimmer.Trim(request, Bytes(request)).Error, Is.Null);
        });
    }

    [Test]
    public void TheEnvelopeCountsTowardsTheBudget() {
        ChatRequest request = Request([new("system", "s"), new("user", "0123456789")]);
        long contentCharacters = request.Messages.Sum(message => message.Content.Length);

        Assert.Multiple(() => {
            Assert.That(Bytes(request), Is.GreaterThan(contentCharacters), "the JSON envelope is part of the request");
            Assert.That(ContextTrimmer.Trim(request, contentCharacters).Error, Is.Not.Null);
        });
    }

    [Test]
    public void ANullTranscriptIsRefused() {
        ChatRequest request = Request([new("user", "a")]) with { Messages = null! };

        Assert.That(() => ContextTrimmer.Trim(request, 10), Throws.ArgumentNullException);
    }

    [Test]
    public void ANonPositiveBudgetIsRefused() {
        Assert.That(() => ContextTrimmer.Trim(Request([new("user", "a")]), 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static ChatRequest Request(IReadOnlyList<ChatMessage> messages) => new(
        "model",
        messages,
        [],
        0.2,
        20,
        "reasoning_content",
        IncludeUsage: true);

    private static long Bytes(ChatRequest request) => ChatRequestJson.MeasureBytes(request);
}
