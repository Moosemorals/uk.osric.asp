// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Security.Claims;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>Captures the credential a render presented, for a job that may outlive it.</summary>
/// <remarks>
/// One authority per render, not one per layer. A principal with no handle behind it — no sign-in
/// cookie and no run claim — is not a credential the host can keep alive, so it captures nothing and a
/// job start from that render is refused as anonymous, exactly as one from a stranger is.
/// </remarks>
internal sealed class PageJobAuthorityFactory(
    ViewTokenStore runs,
    PageJobAuthorityVerifier verifier,
    ILoggerFactory loggers) {

    /// <summary>The credential the request presented, or <see langword="null"/> when it presented none.</summary>
    /// <param name="principal">The identity authentication validated.</param>
    /// <param name="request">The request, for the sign-in cookie's key.</param>
    public PageJobAuthority? For(ClaimsPrincipal principal, HttpRequest request) {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);

        if (principal.Identity?.IsAuthenticated != true) {
            return null;
        }

        string? name = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(name)) {
            return null;
        }

        PageUser owner = new(name);
        ILogger logger = loggers.CreateLogger<PageJobAuthority>();

        // A view's handle is the run's id, never the bearer secret the request presented.
        if (principal.FindFirstValue(ViewToken.Claim) is { } runId) {
            return new PageJobAuthority(
                owner, runs.ForRun(runId)?.SecurityStamp, null, runId, verifier, logger);
        }

        string? key = request.Cookies[ShellCookies.Authentication];
        if (string.IsNullOrEmpty(key)) {
            return null;
        }

        string? stamp = principal.FindFirstValue(EditorPolicy.SecurityStampClaim);

        return new PageJobAuthority(owner, stamp, key, null, verifier, logger);
    }
}
