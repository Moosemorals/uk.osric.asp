// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class TokenEstimatorTests {

    [Test]
    public void TheFirstRequestUsesTheConservativeDefault() {
        TokenEstimator estimator = TokenEstimator.For(null);

        Assert.Multiple(() => {
            Assert.That(estimator.BytesPerToken, Is.EqualTo(1.6));
            Assert.That(estimator.Anchor, Is.Null);
            // 1,600 bytes at 1.6 bytes per token, with the three percent margin.
            Assert.That(estimator.Estimate(1_600), Is.EqualTo(1_030));
        });
    }

    [Test]
    public void TheAnchoredEstimateIsTheReportedCountPlusTheDelta() {
        TokenEstimator estimator = TokenEstimator.For(new TokenCalibration(4_000, 1_000));

        Assert.Multiple(() => {
            Assert.That(estimator.BytesPerToken, Is.EqualTo(4.0));
            Assert.That(estimator.Estimate(4_000), Is.EqualTo(1_000), "an unchanged request costs what the provider counted");
            Assert.That(estimator.Estimate(4_400), Is.EqualTo(1_103), "a hundred tokens of new content is a hundred and three, not a whole request");
        });
    }

    [Test]
    public void AMaskedPayloadIsSubtractedFromTheAnchor() {
        TokenEstimator estimator = TokenEstimator.For(new TokenCalibration(4_000, 1_000));

        // Masking 400 bytes of old payload is a delta of a hundred tokens, not a new estimate.
        Assert.That(estimator.Estimate(3_600), Is.EqualTo(897));
    }

    [Test]
    public void TheMarginIsThreePercentOfTheDelta() {
        Assert.That(new TokenEstimator(2.0, 1.03).Estimate(2_000), Is.EqualTo(1_030));
    }

    [Test]
    public void AnImplausiblyLowRatioIsClamped() {
        Assert.That(TokenEstimator.For(new TokenCalibration(1_000, 2_000)).BytesPerToken, Is.EqualTo(1.0));
    }

    [Test]
    public void AnImplausiblyHighRatioIsClamped() {
        Assert.That(TokenEstimator.For(new TokenCalibration(100_000, 1_000)).BytesPerToken, Is.EqualTo(6.0));
    }

    [Test]
    public void ARatioWithAZeroCountIsIgnored() {
        Assert.Multiple(() => {
            Assert.That(TokenEstimator.For(new TokenCalibration(0, 1_000)).BytesPerToken, Is.EqualTo(1.6));
            Assert.That(TokenEstimator.For(new TokenCalibration(1_000, 0)).BytesPerToken, Is.EqualTo(1.6));
            Assert.That(TokenEstimator.For(new TokenCalibration(1_000, 0)).Anchor, Is.Null);
        });
    }

    [Test]
    public void TheEstimateIncludesTheToolDefinitions() {
        ChatRequest bare = Request([]);
        ChatRequest withTool = Request([Tool]);

        Assert.That(TokenEstimator.For(null).Estimate(withTool), Is.GreaterThan(TokenEstimator.For(null).Estimate(bare)));
    }

    [Test]
    public void TheSameRequestEstimatesTheSame() {
        ChatRequest request = Request([Tool]);
        long first = TokenEstimator.For(null).Estimate(request);

        long second = TokenEstimator.For(null).Estimate(request);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void ANegativeByteCountIsRefused() {
        Assert.That(() => TokenEstimator.For(null).Estimate(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ANonPositiveRatioIsRefused() {
        Assert.That(() => new TokenEstimator(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static ToolDefinition Tool { get; } = new(
        "read_page",
        "Reads a page.",
        false,
        """{"type":"object","properties":{"path":{"type":"string"}}}""");

    private static ChatRequest Request(IReadOnlyList<ToolDefinition> tools) => new(
        "model",
        [new ChatMessage("system", "prompt"), new ChatMessage("user", "a question")],
        tools,
        0.2,
        20,
        "reasoning_content");
}
