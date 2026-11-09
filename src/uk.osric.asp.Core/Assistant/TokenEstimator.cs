// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>What the provider's last report says about the bytes in one request.</summary>
/// <param name="RequestBytes">The exact UTF-8 bytes of the request that report describes.</param>
/// <param name="PromptTokens">The prompt tokens the provider reported for that request.</param>
public sealed record TokenCalibration(long RequestBytes, int PromptTokens) {

    /// <summary>Whether both figures are positive and a ratio can be taken.</summary>
    public bool IsUsable => RequestBytes > 0 && PromptTokens > 0;

    /// <summary>The bytes one prompt token cost, as the last report measured it.</summary>
    public double BytesPerToken => RequestBytes / (double)PromptTokens;
}

/// <summary>Estimates a request's token count from its exact byte count and the provider's last report.</summary>
/// <remarks>
/// This is deliberately an estimate, not a tokenizer: the assistant has no DeepSeek vocabulary to
/// count with, and the provider reports the exact figure after each step. The ratio is the previous
/// request's bytes divided by its reported prompt tokens, so it learns the conversation's content
/// mixture; before the first report it falls back to the densest case the tokenizer spike measured
/// (JSON at about 1.6 bytes per token). A ten-percent margin covers the chat template and content
/// that tokenizes denser than the previous request.
/// </remarks>
public sealed class TokenEstimator {

    /// <summary>The conservative ratio used before the provider has reported anything.</summary>
    public const double DefaultBytesPerToken = 1.6;

    /// <summary>How much the estimate is raised above the calibrated ratio.</summary>
    public const double EstimateMargin = 1.1;

    /// <summary>The densest ratio a report may imply; below this the estimate is clamped.</summary>
    public const double MinimumBytesPerToken = 1.0;

    /// <summary>The cheapest ratio a report may imply; above this the estimate is clamped.</summary>
    public const double MaximumBytesPerToken = 6.0;

    /// <summary>Builds an estimator from a ratio, clamped to the range the spike measured.</summary>
    /// <param name="bytesPerToken">The bytes one token is expected to cost.</param>
    /// <param name="margin">How much to raise the estimate; one means no margin.</param>
    public TokenEstimator(double bytesPerToken, double margin = EstimateMargin) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerToken);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(margin);
        if (!double.IsFinite(bytesPerToken) || !double.IsFinite(margin)) {
            throw new ArgumentOutOfRangeException(nameof(bytesPerToken), "The ratio and margin must be finite.");
        }

        BytesPerToken = Math.Clamp(bytesPerToken, MinimumBytesPerToken, MaximumBytesPerToken);
        Margin = margin;
    }

    /// <summary>The bytes one token is expected to cost.</summary>
    public double BytesPerToken { get; }

    /// <summary>How much the estimate is raised above the calibrated ratio.</summary>
    public double Margin { get; }

    /// <summary>Builds the estimator a provider report supports, or the conservative default.</summary>
    /// <param name="calibration">The last request's bytes and prompt tokens, or null when there is none.</param>
    /// <returns>An estimator whose ratio is the calibration's, clamped, or the default.</returns>
    public static TokenEstimator For(TokenCalibration? calibration) =>
        new(calibration is { IsUsable: true } ? calibration.BytesPerToken : DefaultBytesPerToken);

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
        return (long)Math.Ceiling(requestBytes / BytesPerToken * Margin);
    }
}
