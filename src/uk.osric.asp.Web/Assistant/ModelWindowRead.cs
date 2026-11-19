// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Why the provider has, or has not, told this host what the configured model's limits are.</summary>
internal enum ModelWindowReason {
    /// <summary>The provider answered with the model's window and output limit.</summary>
    Ok,

    /// <summary>The provider answered, and the configured model is not in its list.</summary>
    ModelMissing,

    /// <summary>The provider answered, and the entry omits the window or the output limit.</summary>
    Incomplete,

    /// <summary>The provider did not answer in time.</summary>
    Timeout,

    /// <summary>The provider could not be reached.</summary>
    Unreachable,

    /// <summary>The provider answered with an error status.</summary>
    Error,

    /// <summary>The provider's answer was not the model list.</summary>
    Invalid,

    /// <summary>Nothing has been read yet.</summary>
    Unknown,
}

/// <summary>What the provider's model list says about the configured model.</summary>
/// <param name="ContextWindow">The window the model accepts, in tokens, when it was reported.</param>
/// <param name="MaximumOutputTokens">The largest reply the model accepts, in tokens, when it was reported.</param>
/// <param name="Reason">Whether the figures are there, and why not when they are not.</param>
internal sealed record ModelWindowRead(long? ContextWindow, int? MaximumOutputTokens, ModelWindowReason Reason) {

    /// <summary>The state before anything has been read.</summary>
    public static ModelWindowRead Unknown { get; } = new(null, null, ModelWindowReason.Unknown);

    /// <summary>Whether both figures were reported.</summary>
    public bool IsAvailable => Reason == ModelWindowReason.Ok && ContextWindow is not null && MaximumOutputTokens is not null;

    /// <summary>A read that found both figures.</summary>
    internal static ModelWindowRead Ok(long contextWindow, int maximumOutputTokens) =>
        new(contextWindow, maximumOutputTokens, ModelWindowReason.Ok);

    /// <summary>A read that produced no window, with the reason.</summary>
    internal static ModelWindowRead Failed(ModelWindowReason reason) => new(null, null, reason);

    /// <summary>Whether this answer names the same figures as <paramref name="other"/>.</summary>
    /// <param name="other">The read to compare with.</param>
    /// <returns>True when both carry the same window and output limit.</returns>
    public bool SameAs(ModelWindowRead other) {
        ArgumentNullException.ThrowIfNull(other);
        return ContextWindow == other.ContextWindow && MaximumOutputTokens == other.MaximumOutputTokens;
    }
}
