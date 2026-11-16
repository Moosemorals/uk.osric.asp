// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>Why a balance read produced the provider's figures or did not.</summary>
public enum AccountBalanceReason {

    /// <summary>The provider answered with a balance.</summary>
    Ok,

    /// <summary>The provider has no balance route, so it answered <c>404</c>.</summary>
    Unsupported,

    /// <summary>The provider answered with an error status other than <c>404</c>.</summary>
    Error,

    /// <summary>The provider did not answer before the deadline.</summary>
    Timeout,

    /// <summary>The provider could not be reached at all.</summary>
    Unreachable,

    /// <summary>The provider's body was not the balance JSON.</summary>
    Invalid,
}

/// <summary>The outcome of reading a provider account's balance: the figures, or the reason there are none.</summary>
/// <remarks>
/// A failure is a value rather than a log entry, because the conversation page and the sampler want
/// different things from the same read: the page shows one sentence for any failure, and the sampler
/// logs when the reason changes rather than once per attempt.
/// </remarks>
public sealed record AccountBalanceRead {

    private AccountBalanceRead(AccountBalance? balance, AccountBalanceReason reason) {
        Balance = balance;
        Reason = reason;
    }

    /// <summary>The provider's figures, or <see langword="null"/> when the read produced no balance.</summary>
    public AccountBalance? Balance { get; }

    /// <summary>Why the read produced the balance or did not.</summary>
    public AccountBalanceReason Reason { get; }

    /// <summary>A read that produced the provider's figures.</summary>
    /// <param name="balance">The provider's figures.</param>
    /// <returns>The read.</returns>
    public static AccountBalanceRead Ok(AccountBalance balance) {
        ArgumentNullException.ThrowIfNull(balance);

        return new(balance, AccountBalanceReason.Ok);
    }

    /// <summary>A read that produced no balance.</summary>
    /// <param name="reason">Why there are no figures; <see cref="AccountBalanceReason.Ok"/> is refused.</param>
    /// <returns>The read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The reason says a balance was read.</exception>
    public static AccountBalanceRead Failed(AccountBalanceReason reason) =>
        reason == AccountBalanceReason.Ok
            ? throw new ArgumentOutOfRangeException(nameof(reason), reason, "A failed read needs a reason other than ok.")
            : new(null, reason);
}
