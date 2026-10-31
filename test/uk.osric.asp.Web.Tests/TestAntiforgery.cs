// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

/// <summary>A token source with no cookie side effects, for endpoint tests.</summary>
internal sealed class TestAntiforgery : IAntiforgery {

    public int TokenRequests { get; private set; }

    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) {
        TokenRequests++;
        return Tokens;
    }

    public AntiforgeryTokenSet GetTokens(HttpContext context) {
        return Tokens;
    }

    public Task<bool> IsRequestValidAsync(HttpContext context) {
        return Task.FromResult(true);
    }

    public Task ValidateRequestAsync(HttpContext context) {
        return Task.CompletedTask;
    }

    public void SetCookieTokenAndHeader(HttpContext context) {
    }

    private static AntiforgeryTokenSet Tokens => new("request-token", "cookie-token", "__RequestVerificationToken", null);
}

/// <summary>The result the antiforgery middleware would have recorded.</summary>
internal sealed class TestAntiforgeryValidationFeature(bool isValid) : IAntiforgeryValidationFeature {

    public bool IsValid => isValid;

    public Exception? Error => isValid ? null : new AntiforgeryValidationException("The antiforgery token was rejected.");
}
