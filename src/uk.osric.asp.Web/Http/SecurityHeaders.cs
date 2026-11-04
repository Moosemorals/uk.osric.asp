// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

/// <summary>The header set every response carries, and the one deliberate way to widen it.</summary>
/// <remarks>
/// A default set here cannot be forgotten by an endpoint, and reaches responses no endpoint owns,
/// such as a routing 404 or 405. The headers are written when the response starts, so an endpoint
/// that sets one first — or an exception handler that clears the response — is respected.
/// </remarks>
internal static class SecurityHeaders {

    /// <summary>The name of the policy that varies by page.</summary>
    public const string ContentSecurityPolicy = "Content-Security-Policy";

    private static readonly (string Name, string Value)[] Headers = [
        ("X-Content-Type-Options", "nosniff"),
        // Ignored by a few old engines, so it sits beside the frame-ancestors directive.
        ("X-Frame-Options", "DENY"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Cross-Origin-Resource-Policy", "same-origin"),
        ("Referrer-Policy", "same-origin"),
        ("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()"),
        // With a CSP the advice is to disable the legacy auditor, not let it rewrite the DOM.
        ("X-XSS-Protection", "0"),
    ];

    /// <summary>The policy every response gets: no script at all.</summary>
    public static string Default => Policy("'none'");

    /// <summary>The policy for the editor: its own module, and nothing else.</summary>
    public static string WithScripts => Policy("'self'");

    /// <summary>Writes the header set for a response that has not set one of its own.</summary>
    public static async Task ApplyAsync(HttpContext context, RequestDelegate next) {
        context.Response.OnStarting(() => {
            WriteIfAbsent(context, ContentSecurityPolicy, Default);

            foreach ((string name, string value) in Headers) {
                WriteIfAbsent(context, name, value);
            }

            return Task.CompletedTask;
        });

        await next(context);
    }

    /// <summary>Widens this response's policy to allow same-origin scripts.</summary>
    /// <param name="context">The response that is about to load a script.</param>
    public static void AllowScripts(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Headers[ContentSecurityPolicy] = WithScripts;
    }

    private static void WriteIfAbsent(HttpContext context, string name, string value) {
        if (!context.Response.Headers.ContainsKey(name)) {
            context.Response.Headers[name] = value;
        }
    }

    private static string Policy(string scriptSources) =>
        $"default-src 'none'; script-src {scriptSources}; style-src 'self'; img-src 'self'; "
        + "connect-src 'self'; form-action 'self'; object-src 'none'; base-uri 'none'; "
        + "frame-ancestors 'none'";
}
