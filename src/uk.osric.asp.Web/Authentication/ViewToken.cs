// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Security.Cryptography;

/// <summary>One run's view of the site: the secret it presents and the owner it acts as.</summary>
/// <remarks>
/// <para>
/// It is not a password, not a session and not a sign-in. It exists because the process is already
/// acting as this owner for this run, and it is dead when the run is; nothing stores it, so there is
/// nothing to rotate and nothing to leak.
/// </para>
/// <para>
/// A class rather than a record, for the reason <c>PageCredential</c> is one: the generated
/// <c>ToString</c> would print <see cref="Secret"/>, and a record's is printed by every scope that
/// holds it.
/// </para>
/// </remarks>
internal sealed class ViewToken {

    /// <summary>The claim that tells the guard a request carries a view.</summary>
    public const string Claim = "uk.osric.asp.view";

    /// <summary>The authentication scheme's name, which is also the identity's authentication type.</summary>
    public const string AuthenticationScheme = "View";

    /// <summary>The scheme the secret is presented with in the <c>Authorization</c> header.</summary>
    public const string HeaderScheme = "Bearer";

    private ViewToken(string secret, string runId, string owner, string? securityStamp) {
        Secret = secret;
        RunId = runId;
        Owner = owner;
        SecurityStamp = securityStamp;
    }

    /// <summary>The secret the fetcher presents as <c>Authorization: Bearer</c>.</summary>
    public string Secret { get; }

    /// <summary>The run this token belongs to, which is what the claim carries.</summary>
    public string RunId { get; }

    /// <summary>The account the run belongs to.</summary>
    public string Owner { get; }

    /// <summary>The owner's revocation stamp when the run started, or null when the account was gone.</summary>
    public string? SecurityStamp { get; }

    /// <summary>Creates a token for one run.</summary>
    /// <param name="runId">The run's id.</param>
    /// <param name="owner">The account the run belongs to.</param>
    /// <param name="securityStamp">The account's stamp, read when the run started.</param>
    internal static ViewToken Create(string runId, string owner, string? securityStamp) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        // 32 random bytes, base64: long enough that a lookup by value is the only way to find one, so
        // the search leaks nothing a guess could use.
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return new ViewToken(secret, runId, owner, securityStamp);
    }

    /// <summary>Names the run and its owner, never the secret.</summary>
    /// <returns>A line safe to log.</returns>
    public override string ToString() => $"view token for run {RunId} as {Owner}";
}
