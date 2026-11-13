// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Errors;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>One failure, worded for the person who hit it: what went wrong and what to do next.</summary>
/// <param name="Code">A stable machine-readable name, sent to programs as the problem's extension member.</param>
/// <param name="Title">A short summary, which becomes the page heading.</param>
/// <param name="Summary">One or two sentences naming the problem in plain words.</param>
/// <param name="Remedies">Where the reader can go to recover, in reading order. Empty for an error that is only ever written as a problem document.</param>
internal sealed record ErrorMessage(
    string Code,
    string Title,
    string Summary,
    IReadOnlyList<NavLink> Remedies) {

    /// <summary>The wording for a status no call site described, so no response is ever blank.</summary>
    /// <param name="statusCode">The response status to explain.</param>
    /// <param name="isAuthenticated">Whether the reader is signed in, which adds the editor's links.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static ErrorMessage ForStatus(int statusCode, bool isAuthenticated, string editorPath) => statusCode switch {
        400 => new("request.bad", "Bad request",
            "The request could not be understood. Reload the page and try again.", DefaultRemedies(isAuthenticated, editorPath)),
        401 => new("auth.required", "Sign in required",
            "You need to sign in before this site will show that.", DefaultRemedies(isAuthenticated, editorPath)),
        403 => new("auth.forbidden", "No access",
            "This account is not allowed to open that. Sign in as the owner, or return to the home page.",
            DefaultRemedies(isAuthenticated, editorPath)),
        404 => new("route.not_found", "Not found",
            "That address does not exist on this site. Check the link, or use one of the links below.",
            DefaultRemedies(isAuthenticated, editorPath)),
        405 => new("route.method_not_allowed", "Not allowed",
            "That address does not accept this kind of request. Go back and follow a link or a form instead.",
            DefaultRemedies(isAuthenticated, editorPath)),
        409 => new("request.conflict", "The request conflicts with the current state",
            "Something changed while you were working. Reload the page, then try again.",
            DefaultRemedies(isAuthenticated, editorPath)),
        413 => new("request.too_large", "The request is too large",
            "The body this site received is bigger than it accepts. Shorten it and try again.",
            DefaultRemedies(isAuthenticated, editorPath)),
        429 => new("request.rate_limited", "Too many requests",
            "There have been too many attempts in a short time. Wait a minute, then try again.",
            DefaultRemedies(isAuthenticated, editorPath)),
        503 => new("server.unavailable", "Temporarily unavailable",
            "The site could not finish that request. Try again in a moment.", DefaultRemedies(isAuthenticated, editorPath)),
        504 => new("server.timeout", "The request timed out",
            "That took too long to finish. Try again, or ask for less in one go.", DefaultRemedies(isAuthenticated, editorPath)),
        _ => new("server.error", "Something went wrong",
            "An unexpected error occurred. Please try again.", DefaultRemedies(isAuthenticated, editorPath)),
    };

    /// <summary>The wording for a form the host refused before an endpoint could read it.</summary>
    /// <param name="isAuthenticated">Whether the reader is signed in.</param>
    /// <remarks>
    /// A stale antiforgery token is the most common 400 a person sees, and the one case that has to
    /// promise the work was not lost with the form.
    /// </remarks>
    public static ErrorMessage FormExpired(bool isAuthenticated, string editorPath) => new(
        "request.form_expired",
        "The form expired",
        "The form could not be verified, so nothing was saved. Reload the page to get a fresh form, then try again.",
        DefaultRemedies(isAuthenticated, editorPath));

    /// <summary>The links every page offers: the editor when signed in, the way back always.</summary>
    /// <param name="isAuthenticated">Whether the reader is signed in.</param>
    /// <param name="editorPath">The editor's own key, which is configuration.</param>
    public static IReadOnlyList<NavLink> DefaultRemedies(bool isAuthenticated, string editorPath) => isAuthenticated
        ? [new(editorPath, "Page list"), new("/", "Return to the home page")]
        : [new("/", "Return to the home page"), new("/login", "Sign in")];
}
