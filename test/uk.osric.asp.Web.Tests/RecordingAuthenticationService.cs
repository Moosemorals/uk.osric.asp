// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

/// <summary>Records what a login handler asked the authentication stack to do.</summary>
internal sealed class RecordingAuthenticationService : IAuthenticationService {

    public string? SignedInScheme { get; private set; }

    public ClaimsPrincipal? SignedInPrincipal { get; private set; }

    public AuthenticationProperties? SignedInProperties { get; private set; }

    public string? SignedOutScheme { get; private set; }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) {
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        return Task.CompletedTask;
    }

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        return Task.CompletedTask;
    }

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) {
        SignedInScheme = scheme;
        SignedInPrincipal = principal;
        SignedInProperties = properties;
        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        SignedOutScheme = scheme;
        return Task.CompletedTask;
    }
}
