// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class RunUsageTests {

    [Test]
    public void ATokenUsageAddsEveryReportedCount() {
        RunUsage usage = RunUsage.Empty
            .Add(new TokenUsage(10, 4, 6, 2))
            .Add(new TokenUsage(20, 5, 12, 1));

        Assert.Multiple(() => {
            Assert.That(usage.PromptTokens, Is.EqualTo(30));
            Assert.That(usage.CompletionTokens, Is.EqualTo(9));
            Assert.That(usage.CachedTokens, Is.EqualTo(18));
            Assert.That(usage.ReasoningTokens, Is.EqualTo(3));
        });
    }

    [Test]
    public void TheOutputIsTheLastStepNotTheSum() {
        RunUsage usage = RunUsage.Empty
            .Add(new TokenUsage(10, 4))
            .Add(new TokenUsage(20, 5));

        Assert.That(usage.OutputTokens, Is.EqualTo(5));
    }

    [Test]
    public void ANullCompletionKeepsTheLastOutput() {
        RunUsage usage = RunUsage.Empty
            .Add(new TokenUsage(10, 4))
            .Add(new TokenUsage(20, null));

        Assert.Multiple(() => {
            Assert.That(usage.CompletionTokens, Is.EqualTo(4));
            Assert.That(usage.OutputTokens, Is.EqualTo(4));
        });
    }

    [Test]
    public void ANullUsageChangesNothing() {
        RunUsage usage = new(1, 2, 3, 4, 5);

        Assert.That(usage.Add(null), Is.SameAs(usage));
    }

    [Test]
    public void TheTotalIsPromptPlusCompletion() {
        RunUsage usage = new(10, 4, 6, 2, 1);

        Assert.That(usage.TotalTokens, Is.EqualTo(14));
    }
}
