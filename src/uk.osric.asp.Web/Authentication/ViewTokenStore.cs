// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Security.Cryptography;
using System.Text;
using uk.osric.asp.Web.Assistant;

/// <summary>The view tokens of the runs that are executing right now.</summary>
/// <remarks>
/// <para>
/// One entry per live run, at most <c>Assistant:MaxActiveRuns</c> of them, and nowhere but this
/// dictionary: no table, no file, no key-ring entry, no configuration key. A restart ends every run
/// (<c>assistant-a-run-is-not-resumed</c>) and therefore every token, which is the lifetime the run
/// already has rather than a second one to reason about.
/// </para>
/// <para>
/// Resolution scans the entries rather than indexing the secret. There are at most two, and a lookup
/// by value could only answer faster, not more safely: the secret is 256 random bits, so finding one
/// is already the only way in, and the comparison itself is fixed-time.
/// </para>
/// </remarks>
internal sealed class ViewTokenStore(AssistantOptions options) {

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ViewToken> _byRun = [with(StringComparer.Ordinal)];

    /// <summary>Creates the token for a run that is about to execute.</summary>
    /// <param name="runId">The run's id.</param>
    /// <param name="owner">The account the run belongs to.</param>
    /// <param name="securityStamp">The owner's stamp when the run started, or null when the account is gone.</param>
    /// <returns>The token, which <see cref="Forget"/> must be given when the run ends.</returns>
    /// <exception cref="InvalidOperationException">Every active run already holds one.</exception>
    public ViewToken Mint(string runId, string owner, string? securityStamp) {
        ViewToken token = ViewToken.Create(runId, owner, securityStamp);
        lock (_gate) {
            // A second token for a run would leave the first unverifiable if it overwrote the entry,
            // which is the same failure a second antiforgery token is refused for.
            if (_byRun.ContainsKey(runId) || _byRun.Count >= options.MaxActiveRuns) {
                throw new InvalidOperationException(
                    $"A view token cannot be minted for run {runId}: the store already holds {_byRun.Count} of at most {options.MaxActiveRuns}.");
            }

            _byRun.Add(runId, token);
        }

        return token;
    }

    /// <summary>The token a run is presenting, or null when it holds none.</summary>
    /// <param name="runId">The run's id.</param>
    public ViewToken? ForRun(string runId) {
        lock (_gate) {
            return _byRun.GetValueOrDefault(runId);
        }
    }

    /// <summary>Resolves a presented secret, comparing it in fixed time.</summary>
    /// <param name="secret">The value the request presented.</param>
    /// <param name="token">The token it belongs to, when it belongs to one.</param>
    /// <returns><see langword="true"/> when the secret is a live run's.</returns>
    public bool TryResolve(string secret, out ViewToken? token) {
        token = null;
        if (string.IsNullOrEmpty(secret)) {
            return false;
        }

        byte[] presented = Encoding.UTF8.GetBytes(secret);
        lock (_gate) {
            foreach (ViewToken candidate in _byRun.Values) {
                byte[] expected = Encoding.UTF8.GetBytes(candidate.Secret);
                // The lengths are equal for every token this store mints; the check is what lets the
                // comparison be the only thing that varies with the value.
                if (presented.Length == expected.Length
                    && CryptographicOperations.FixedTimeEquals(presented, expected)) {
                    token = candidate;

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Forgets a run's token, which is what makes it dead.</summary>
    /// <param name="token">The token <see cref="Mint"/> returned.</param>
    public void Forget(ViewToken token) {
        ArgumentNullException.ThrowIfNull(token);

        lock (_gate) {
            if (_byRun.TryGetValue(token.RunId, out ViewToken? held)
                && string.Equals(held.Secret, token.Secret, StringComparison.Ordinal)) {
                _ = _byRun.Remove(token.RunId);
            }
        }
    }

    /// <summary>How many runs hold a token, for a test and for the startup line.</summary>
    public int Count {
        get {
            lock (_gate) {
                return _byRun.Count;
            }
        }
    }
}
