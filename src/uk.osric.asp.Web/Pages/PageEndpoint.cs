// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;

/// <summary>
/// The endpoint that serves pages, and the only one that does so.
/// </summary>
internal sealed class PageEndpoint() {

    // The body is buffered, so a form post is read in full before the page runs; anything larger than
    // this is rejected rather than held in memory.
    private const long MaxFormBytes = 1024 * 1024;

    public static async Task HandleAsync(
        HttpContext context,
        PageRenderer pages,
        ILoggerFactory loggerFactory,
        PageEnvironment environment) {

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);

        // A key is a path, so a decoded delimiter means the URL names no page. Checked before the
        // redirect, because redirecting to a key that cannot exist would send the browser in circles.
        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (!string.Equals(context.Request.Path.Value, path.Key, StringComparison.Ordinal)) {
            // The query is carried across, because the page is handed it and dropping it would change
            // what the page sees for a URL that is merely written with the wrong case.
            Redirect.Permanent(context, path.Key + context.Request.QueryString.ToUriComponent());
            return;
        }

        // A page that appeared by another route - a direct database write, an import - must not be
        // half-served for a key the site's own routing already answers for.
        if (environment.Reserved.IsReserved(path)) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (context.Request.ContentLength > MaxFormBytes) {
            await WriteTooLargeAsync(context, path);
            return;
        }

        // A chunked POST has no ContentLength to check, so its body is read here instead.
        if (context.Request.ContentLength is null && !await TryBufferFormAsync(context)) {
            await WriteTooLargeAsync(context, path);
            return;
        }

        PageRequest snapshot;
        try {
            snapshot = PageRequestSnapshot.Create(context.Request);
        } catch (InvalidDataException exception) {
            // The form parser refuses a key or value past its limit. That is the client's fault, so
            // it is a 400 rather than the error page - and Debug, because a reader's browser sending
            // an unparseable form is not something anybody can act on.
            loggerFactory.CreateLogger<PageEndpoint>()
                .LogDebug(exception, "The request body for {Path} could not be read as a form", path.Key);
            await WriteBadRequestAsync(context, path);
            return;
        }

        // Identity comes only from the principal UseAuthentication validated, never from a header or
        // a cookie the page could read. It is mapped once, so the guard and the CSRF check agree.
        PageUser? user = MapUser(context.User);
        bool authenticated = context.User.Identity?.IsAuthenticated == true;

        // A signed-in visitor's form is exactly where a cross-site post becomes an action by the
        // owner, so the host checks it rather than leaving it to the page author. An anonymous post
        // has no cookie-backed identity to forge and is unchanged.
        if (authenticated
            && HttpMethods.IsPost(context.Request.Method)
            && !await environment.Antiforgery.IsRequestValidAsync(context)) {
            await WriteForgedRequestAsync(context, path);
            return;
        }

        StringWriter output = new();

        // A HEAD request runs the page to produce the representation and its validators, so its state
        // is readable but never written, and it cannot POST to a remote system.
        bool isHead = HttpMethods.IsHead(context.Request.Method);
        PageStorageScope storage = new(
            path,
            environment.StateStore,
            environment.Storage,
            writable: !isHead);

        // The fetch allowance belongs to the render, so every layer built from this request shares
        // one counter rather than each getting the whole budget.
        RenderFetchBudget budget = new(environment.Fetch.MaxRequestsPerRender);
        PageHttpFetch fetch = new(
            path,
            isHead,
            context.RequestAborted,
            environment.Fetcher,
            environment.Fetch,
            budget,
            environment.Metrics,
            loggerFactory.CreateLogger(SimpleContext.PageCategory));
        PageFeatures features = new PageFeatures()
            .Add<IPageStorage>(storage)
            .Add<IHttpFetch>(fetch);

        SimpleContext page = new(
            loggerFactory,
            snapshot,
            context.RequestAborted,
            output,
            features,
            environment.Render.MaxBodyChars,
            user,
            LoginRedirect.For(path, context.Request.QueryString),
            () => AntiforgeryField.Hidden(environment.Antiforgery.GetAndStoreTokens(context)));

        RenderResult result = await pages.RenderAsync(path, page, context.RequestAborted);

        switch (result.Status) {
            case RenderStatus.NotFound:
                await NotFound.WriteAsync(context, path);
                break;
            case RenderStatus.CompileFailed:
                await WriteCompileErrorAsync(context, result.Diagnostics);
                break;
            case RenderStatus.Timeout:
                await WriteTimeoutAsync(context, path);
                break;
            default:
                await WritePageAsync(context, page.ToResponse(), page.GetBody(), authenticated);
                break;
        }
    }

    // An authenticated identity without a name is anonymous as far as a page is concerned: a
    // half-built principal must fail closed rather than hand the page a nameless user.
    private static PageUser? MapUser(ClaimsPrincipal principal) {
        if (principal.Identity?.IsAuthenticated != true) {
            return null;
        }

        string? name = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrEmpty(name) ? null : new PageUser(name);
    }

    // The page's body is buffered until here, so the response is written in one go: status and
    // headers first, and nothing at all when the page redirected or the request was a HEAD.
    private static async Task WritePageAsync(
        HttpContext context,
        PageResponse response,
        string body,
        bool authenticated) {
        if (response.Location is not null) {
            // A redirect can carry the promise of a page the visitor is not yet allowed to see, so it
            // is never cached, whoever asked.
            SecurityHeaders.NoStore(context);
            Redirect.To(context, response.Status, response.Location);
            return;
        }

        // A redirect carries no document to run a script in, so its policy is left alone.
        if (response.AllowsScripts) {
            SecurityHeaders.AllowScripts(context);
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = response.ContentType;
        foreach ((string name, string value) in response.Headers) {
            context.Response.Headers[name] = value;
        }

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        EntityTagHeaderValue? entityTag = null;

        if (authenticated) {
            // Copies first, then overrides, so a page cannot weaken it with SetHeader. A response
            // rendered for one owner must never be a shared cache's answer to another visitor, and a
            // validator would invite exactly that revalidation.
            SecurityHeaders.NoStore(context);
            context.Response.Headers.Remove("ETag");
        } else if (response.Status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices) {
            byte[] digest = SHA256.HashData(bodyBytes);
            entityTag = new EntityTagHeaderValue($"\"{Convert.ToHexString(digest)}\"");
            context.Response.Headers["ETag"] = entityTag.ToString();
        }

        if (entityTag is not null
            && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && context.Request.GetTypedHeaders().IfNoneMatch?.Any(candidate =>
                candidate.Tag.Equals(EntityTagHeaderValue.Any.Tag, StringComparison.Ordinal)
                    || candidate.Tag.Equals(entityTag.Tag, StringComparison.Ordinal)) == true) {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentType = null;
            context.Response.ContentLength = null;
            return;
        }

        if (response.Status is >= StatusCodes.Status200OK
            and not StatusCodes.Status204NoContent
            and not StatusCodes.Status304NotModified) {
            context.Response.ContentLength = bodyBytes.LongLength;
        }

        if (!HttpMethods.IsHead(context.Request.Method)) {
            await context.Response.Body.WriteAsync(bodyBytes.AsMemory(), context.RequestAborted);
        }
    }

    private static async Task WriteCompileErrorAsync(HttpContext context, IReadOnlyList<PageDiagnostic> diagnostics) {
        await ShellDocument.BeginAsync(context, "Compilation failed", StatusCodes.Status500InternalServerError);
        await context.Response.WriteAsync("<h1>Compilation failed</h1>");
        await context.Response.WriteAsync("<p>Handler failed to compile.</p>");
        await context.Response.WriteAsync(PageLayout.Diagnostics(DiagnosticLine.From(diagnostics)));
        await ShellDocument.EndAsync(context);
    }

    private static Task WriteTimeoutAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status504GatewayTimeout, new ErrorMessage(
            "page.timeout",
            "The page took too long",
            $"{path} is taking too long.",
            ErrorMessage.DefaultRemedies(context.User.Identity?.IsAuthenticated == true)));

    private static Task WriteBadRequestAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "page.form_unreadable",
            "The form could not be read",
            $"{path} rejected: the form could not be read.",
            ErrorMessage.DefaultRemedies(context.User.Identity?.IsAuthenticated == true)));

    private static Task WriteForgedRequestAsync(HttpContext context, PagePath path) {
        // The check happens before the page runs, so nothing the page might have written or stored
        // exists; no-store keeps the refusal itself out of a cache.
        SecurityHeaders.NoStore(context);

        return ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "page.form_unverified",
            "The form could not be verified",
            $"{path} rejected: the form could not be verified. Go back, reload the page and try again.",
            ErrorMessage.DefaultRemedies(isAuthenticated: true)));
    }

    private static Task WriteTooLargeAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, new ErrorMessage(
            "page.body_too_large",
            "The request body is too large",
            $"{path} rejected: the request body is larger than this site accepts.",
            ErrorMessage.DefaultRemedies(context.User.Identity?.IsAuthenticated == true)));

    // The header check above cannot see a chunked body, so read it here instead: bounded, then
    // handed back as a seekable stream for the form to parse. The snapshot buffers the body anyway;
    // this just makes that buffer the cap's size rather than the server's.
    private static async Task<bool> TryBufferFormAsync(HttpContext context) {
        if (!context.Request.HasFormContentType) {
            return true;
        }

        MemoryStream buffer = new();
        byte[] chunk = new byte[16 * 1024];

        while (buffer.Length <= MaxFormBytes) {
            int read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted);
            if (read == 0) {
                buffer.Position = 0;
                context.Request.Body = buffer;
                return true;
            }

            buffer.Write(chunk, 0, read);
        }

        return false;
    }
}
