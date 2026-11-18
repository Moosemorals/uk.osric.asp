// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>What the provider's last report says about the request it counted.</summary>
/// <param name="RequestBytes">The exact UTF-8 bytes of the request that report describes: the anchor.</param>
/// <param name="PromptTokens">The prompt tokens the provider reported for that request.</param>
public sealed record TokenCalibration(long RequestBytes, int PromptTokens) {

    /// <summary>Whether both figures are positive and a ratio can be taken.</summary>
    public bool IsUsable => RequestBytes > 0 && PromptTokens > 0;

    /// <summary>The bytes one prompt token cost, as the last report measured it.</summary>
    public double BytesPerToken => RequestBytes / (double)PromptTokens;
}

/// <summary>Estimates a request's token count from the provider's own count of the last one.</summary>
/// <remarks>
/// The provider counts every request it receives and reports the figure, so the estimate is anchored
/// on that exact count and only the bytes appended since are estimated. The margin therefore covers
/// the delta's ratio error rather than the whole conversation, and each step's report corrects the
/// next (<c>assistant-tokens-anchor-on-the-last-report</c>). Masking and dropping change the request by
/// known byte amounts, so they move the anchor by that amount and nothing else. Before the first
/// report there is no anchor, and the whole request is estimated with the densest ratio the tokenizer
/// spike measured (JSON at about 1.6 bytes per token).
/// </remarks>
public sealed class TokenEstimator {

    /// <summary>The conservative ratio used before the provider has reported anything.</summary>
    public const double DefaultBytesPerToken = 1.6;

    /// <summary>How much the estimate is raised above the calibrated ratio; it covers the delta only.</summary>
    public const double DeltaMargin = 1.03;

    /// <summary>The densest ratio a report may imply; below this the estimate is clamped.</summary>
    public const double MinimumBytesPerToken = 1.0;

    /// <summary>The cheapest ratio a report may imply; above this the estimate is clamped.</summary>
    public const double MaximumBytesPerToken = 6.0;

    /// <summary>Builds an estimator from a ratio, clamped to the range the spike measured.</summary>
    /// <param name="bytesPerToken">The bytes one token is expected to cost.</param>
    /// <param name="margin">How much to raise the estimate; one means no margin.</param>
    public TokenEstimator(double bytesPerToken, double margin = DeltaMargin) : this(bytesPerToken, margin, null) {
    }

    private TokenEstimator(double bytesPerToken, double margin, TokenCalibration? anchor) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerToken);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(margin);
        if (!double.IsFinite(bytesPerToken) || !double.IsFinite(margin)) {
            throw new ArgumentOutOfRangeException(nameof(bytesPerToken), "The ratio and margin must be finite.");
        }

        BytesPerToken = Math.Clamp(bytesPerToken, MinimumBytesPerToken, MaximumBytesPerToken);
        Margin = margin;
        Anchor = anchor;
    }

    /// <summary>The bytes one token is expected to cost.</summary>
    public double BytesPerToken { get; }

    /// <summary>How much the estimate is raised above the calibrated ratio.</summary>
    public double Margin { get; }

    /// <summary>The request the provider last counted, or null when it has counted none.</summary>
    public TokenCalibration? Anchor { get; }

    /// <summary>Builds the estimator a provider report supports, anchored on that report.</summary>
    /// <param name="calibration">The last request's bytes and prompt tokens, or null when there is none.</param>
    /// <returns>An estimator whose ratio is the calibration's, clamped, or the default.</returns>
    public static TokenEstimator For(TokenCalibration? calibration) => new(
        calibration is { IsUsable: true } ? calibration.BytesPerToken : DefaultBytesPerToken,
        DeltaMargin,
        calibration is { IsUsable: true } ? calibration : null);

    /// <summary>Estimates the tokens a request would cost.</summary>
    /// <param name="request">The request whose serialized body is measured.</param>
    /// <returns>The estimated prompt tokens, including the margin.</returns>
    public long Estimate(ChatRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        return Estimate(ChatRequestJson.MeasureBytes(request));
    }

    /// <summary>Estimates the tokens a request body of this size would cost.</summary>
    /// <param name="requestBytes">The UTF-8 bytes of the serialized body.</param>
    /// <returns>The estimated prompt tokens, including the margin.</returns>
    public long Estimate(long requestBytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(requestBytes);

        if (Anchor is not { } anchor) {
            return (long)Math.Ceiling(requestBytes / BytesPerToken * Margin);
        }

        // Only what the provider has not counted is estimated: the anchor keeps its exact figure, and
        // the difference between this body and the anchored one is what the margin covers.
        double delta = requestBytes - anchor.RequestBytes;
        long estimated = anchor.PromptTokens + (long)Math.Ceiling(delta / BytesPerToken * Margin);
        return Math.Max(0, estimated);
    }
}
