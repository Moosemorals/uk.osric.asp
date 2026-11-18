// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ContextBudgetTests {

    // The provider's own documented example: deepseek-flash.
    private static ContextBudget Shipped => ContextBudget.Create(1_048_576, 393_216, 131_072);

    [Test]
    public void ThePromptGetsTheWindowLessTheReplyFloor() {
        Assert.Multiple(() => {
            Assert.That(Shipped.ContextWindow, Is.EqualTo(1_048_576));
            Assert.That(Shipped.MinimumOutputTokens, Is.EqualTo(131_072));
            Assert.That(Shipped.PromptBudget, Is.EqualTo(917_504));
        });
    }

    [Test]
    public void TheFractionsFollowTheReportedWindowRatherThanAStoredNumber() {
        ContextBudget larger = ContextBudget.Create(2_097_152, 393_216, 131_072);

        Assert.Multiple(() => {
            Assert.That(Shipped.ToolResultChars, Is.EqualTo(183_500));
            Assert.That(Shipped.MessageChars, Is.EqualTo(Shipped.ToolResultChars));
            Assert.That(Shipped.MemoryCharsPerRun, Is.EqualTo(45_875));
            Assert.That(larger.ToolResultChars, Is.GreaterThan(Shipped.ToolResultChars));
        });
    }

    [Test]
    public void AnEmptyConversationAsksForThePublishedCeiling() {
        Assert.That(Shipped.MaxTokens(1_000), Is.EqualTo(393_216));
    }

    [Test]
    public void APromptAtNineHundredThousandAsksForWhatIsLeft() {
        Assert.That(Shipped.MaxTokens(900_000), Is.EqualTo(148_576));
    }

    [Test]
    public void APromptAtTheBudgetAsksForTheFloorAndNoLess() {
        Assert.Multiple(() => {
            Assert.That(Shipped.MaxTokens(Shipped.PromptBudget), Is.EqualTo(131_072));
            Assert.That(Shipped.MaxTokens(Shipped.ContextWindow), Is.EqualTo(131_072));
            Assert.That(Shipped.MaxTokens(Shipped.ContextWindow * 2), Is.EqualTo(131_072));
        });
    }

    [Test]
    public void TheMarginIsThreePercentOfThePromptBudget() {
        Assert.That(Shipped.EstimateMargin, Is.EqualTo(27_525));
    }

    [Test]
    public void AFloorThatLeavesTooLittleForThePromptIsRefused() {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => ContextBudget.Create(1_048_576, 393_216, 1_048_576 - 1_000))!;

        Assert.That(error.Message, Does.Contain("16384"));
    }

    [Test]
    public void ANonPositiveFigureIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => ContextBudget.Create(0, 1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => ContextBudget.Create(1_048_576, 0, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => ContextBudget.Create(1_048_576, 1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
