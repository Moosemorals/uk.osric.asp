// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Errors;

using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Writes a failed response for whichever audience asked for it.</summary>
/// <remarks>
/// Nobody is handed an empty body. A browser gets the document shell with a heading, an
/// explanation and links that lead somewhere useful; a program gets an RFC 9457 problem document.
/// Call sites that know something specific say it; <see cref="ErrorMessage.ForStatus"/> covers
/// everything that does not, which is what the status-code fallback in the host uses.
/// <para>
/// The document also offers the editor for the page the request named, when the reader is signed in
/// and the key is one the page route owns. That is derived from the request rather than passed in,
/// so no call site can forget it, and a shell route's failure cannot offer to edit a page that does
/// not exist.
/// </para>
/// </remarks>
internal static class ErrorResponses {

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes the error as HTML or as a problem document, whichever the request accepts.</summary>
    /// <param name="context">The failing request.</param>
    /// <param name="statusCode">The status to send.</param>
    /// <param name="message">What to say.</param>
    /// <param name="cancellationToken">Cancels the write; the request's own token when none is given.</param>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        ErrorMessage message,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        cancellationToken = cancellationToken.CanBeCanceled ? cancellationToken : context.RequestAborted;

        return ErrorAudience.PrefersJson(context.Request)
            ? WriteJsonAsync(context, statusCode, message, cancellationToken)
            : WriteHtmlAsync(context, statusCode, message, cancellationToken: cancellationToken);
    }

    /// <summary>Writes the error as a document: heading, explanation, recovery links, code and reference.</summary>
    /// <param name="context">The failing request.</param>
    /// <param name="statusCode">The status to send.</param>
    /// <param name="message">What to say.</param>
    /// <param name="detailsHtml">
    /// Extra markup written between the explanation and the recovery links, for a view that knows
    /// something the message cannot carry. It is the caller's markup, not a request value, so nothing
    /// here encodes it.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async Task WriteHtmlAsync(
        HttpContext context,
        int statusCode,
        ErrorMessage message,
        string? detailsHtml = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        SecurityHeaders.NoStoreIfAbsent(context);
        await ShellDocument.BeginAsync(context, message.Title,
            SiteNavigation.For(context), statusCode, cancellationToken: cancellationToken);
        await context.Response.WriteAsync($"<h1>{WebUtility.HtmlEncode(message.Title)}</h1>", cancellationToken);
        await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message.Summary)}</p>", cancellationToken);
        if (detailsHtml is not null) {
            await context.Response.WriteAsync(detailsHtml, cancellationToken);
        }
        IReadOnlyList<NavLink> remedies = Remedies(context, message.Remedies);
        if (remedies.Count > 0) {
            await context.Response.WriteAsync(PageDocument.Nav("Recovery navigation", remedies, "editor-menu"), cancellationToken);
        }
        // The code is the same identifier the problem document carries, so a person reporting a
        // failure and a program logging one name it the same way.
        await context.Response.WriteAsync($"<p class=\"error-code\">Error code: <code>{WebUtility.HtmlEncode(message.Code)}</code></p>", cancellationToken);
        if (!string.IsNullOrEmpty(context.TraceIdentifier)) {
            await context.Response.WriteAsync($"<p>Reference: {WebUtility.HtmlEncode(context.TraceIdentifier)}</p>", cancellationToken);
        }
        await ShellDocument.EndAsync(context, cancellationToken);
    }

    /// <summary>Writes the error as an RFC 9457 problem document, with the message code as an extension member.</summary>
    /// <param name="context">The failing request.</param>
    /// <param name="statusCode">The status to send.</param>
    /// <param name="message">What to say.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async Task WriteJsonAsync(
        HttpContext context,
        int statusCode,
        ErrorMessage message,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        SecurityHeaders.NoStoreIfAbsent(context);
        ProblemDetails problem = new() {
            Title = message.Title,
            Status = statusCode,
            Detail = message.Summary,
            Instance = context.Request.Path.Value,
        };
        problem.Extensions["code"] = message.Code;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, s_jsonOptions), cancellationToken);
    }

    /// <summary>The error as an <see cref="IResult"/>, for an endpoint that has to return one.</summary>
    /// <param name="statusCode">The status to send.</param>
    /// <param name="message">What to say.</param>
    /// <remarks>
    /// The result writes through <see cref="WriteJsonAsync"/>, so an endpoint that answers with a
    /// result and one that writes the response directly cannot drift into two problem shapes.
    /// </remarks>
    public static IResult Problem(int statusCode, ErrorMessage message) {
        ArgumentNullException.ThrowIfNull(message);

        return new ProblemResult(statusCode, message);
    }

    private sealed record ProblemResult(int StatusCode, ErrorMessage Message) : IResult {
        public Task ExecuteAsync(HttpContext context) => WriteJsonAsync(context, StatusCode, Message);
    }

    // The editor link belongs to the page that failed, and only the request knows whether it named
    // one: a shell route's document must not offer to edit /metrics. The link is added only when no
    // remedy already leads to that editor URL, so NotFound's "Create this page" keeps its wording.
    private static IReadOnlyList<NavLink> Remedies(HttpContext context, IReadOnlyList<NavLink> remedies) {
        if (context.User.Identity?.IsAuthenticated != true
            || context.RequestServices?.GetService<ReservedPaths>() is not { } reserved) {
            return remedies;
        }

        PagePath path = PagePath.Normalize(context.Request.Path.Value);
        if (!path.IsAddressable || reserved.IsReserved(path)) {
            return remedies;
        }

        string editor = PageUrls.Editor(path, EditorPaths.Of(context.RequestServices));
        foreach (NavLink remedy in remedies) {
            if (string.Equals(remedy.Href, editor, StringComparison.Ordinal)) {
                return remedies;
            }
        }

        return [new(editor, "Edit this page"), .. remedies];
    }
}
