// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Primitives;
using System.Security.Claims;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
// The page contract has a diagnostic value of its own, so this file names the compiler's.
using PageDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;

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
        PageEnvironment environment,
        PageEditor? editor = null) {
        ArgumentNullException.ThrowIfNull(context);

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

        // The script policy belongs to the key's extension, not to the revision and not to whatever
        // content type the page later sets. A page may widen `none` with AllowScripts; it never
        // narrows a `self` row.
        if (environment.ContentTypes.ScriptsForPath(path.Key) == PageScriptPolicy.Self) {
            SecurityHeaders.AllowScripts(context);
        }

        // A versioned URL names one fixed representation, so it answers GET and HEAD only. The check
        // is before any body is read: nothing about an immutable asset needs a request body.
        string? versionHash = null;
        if (context.Request.Query.TryGetValue("v", out StringValues versions)) {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) {
                await WriteMethodNotAllowedAsync(context);
                return;
            }

            // A repeated, empty or malformed hash names nothing, and nothing here falls back to the
            // live head: a URL that promised one byte sequence must never deliver another.
            if (versions.Count != 1 || !RevisionHash.IsCanonical(versions[0])) {
                environment.Metrics.RecordAsset(PageMetrics.AssetUnknown);
                await NotFound.WriteAsync(context, path);
                return;
            }

            versionHash = versions[0];
        }

        PageSelection? selection = null;
        if (versionHash is not null) {
            PageTemplate? selected = await pages.GetRevisionByHashAsync(versionHash, context.RequestAborted);
            if (selected is null || !string.Equals(selected.Path.Key, path.Key, StringComparison.Ordinal)) {
                environment.Metrics.RecordAsset(PageMetrics.AssetUnknown);
                await NotFound.WriteAsync(context, path);
                return;
            }

            if (PageAssetPolicy.IsAsset(selected.TemplateHasCode)) {
                await WriteSourceAsync(context, path, selected.Template, versionHash, environment);

                return;
            }

            selection = new PageSelection(selected, IsHistorical: true);
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

        // A HEAD request runs the page to produce the representation and its validator, so its state
        // is readable but never written, and it cannot POST to a remote system.
        bool isHead = HttpMethods.IsHead(context.Request.Method);

        if (selection is null) {
            PageTemplate? head = await pages.GetPageAsync(path, context.RequestAborted);
            if (head is not null) {
                if (PageAssetPolicy.IsAsset(head.TemplateHasCode)) {
                    // Nothing is compiled or executed for this revision, and no render slot is taken:
                    // the stored source is the body, on this path, and a bare URL is not cacheable.
                    await WriteSourceAsync(context, path, head.Template, versionHash: null, environment);

                    return;
                }

                selection = new PageSelection(head, IsHistorical: false);
            }
        }

        // One read per render: a page picks its sheets while it runs, and AssetUrl answers from this
        // map without an await.
        IReadOnlyDictionary<string, string> assets = selection is null
            ? PageAssetMap.Empty
            : await PageAssetMap.ForRequestAsync(
                context, environment.ReadStore, environment.Read.MaxRowsPerList, context.RequestAborted);

        // The visitor's session is resolved once, before any layer runs, so two layers of one chain
        // cannot see different sessions. The row is only minted once something writes to it.
        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, environment.SessionStore, environment.SessionCookie, user, context.RequestAborted);

        // The antiforgery pair is minted once per request: a page and the layout around it must be
        // verifiable against the same token, and a second would leave the first unverifiable.
        string? antiforgeryField = null;

        // One body, one fetch allowance and one template allowance for the whole chain: the chrome is
        // part of the response, so a layout must not be able to buffer, fetch or read beside the
        // page's own budgets.
        PageChain chain = new(
            environment,
            editor,
            loggerFactory,
            snapshot,
            user,
            session,
            isHead,
            context.RequestAborted,
            new BodyBudget(environment.Render.MaxBodyChars),
            new RenderFetchBudget(environment.Fetch.MaxRequestsPerRender),
            new ReadBudget(environment.Read.MaxSourceCharsPerRender),
            assets,
            LoginRedirect.For(path, context.Request.QueryString),
            () => antiforgeryField ??= AntiforgeryField.Hidden(environment.Antiforgery.GetAndStoreTokens(context)));

        PageLayoutState contentLayout = new(environment.Layout.Default);
        StringWriter output = new();
        SimpleContext page = chain.CreateLayer(path, output, contentLayout, isLayout: false);

        // One slot and one deadline for the whole chain, so a wrapped page costs one render rather
        // than two.
        await using PageRenderer.RenderSession render = await pages.OpenSessionAsync(path, context.RequestAborted);
        RenderResult result = selection is null
            ? await render.RenderAsync(path, page)
            : await render.RenderAsync(path, page, selection);

        switch (result.Status) {
            case RenderStatus.NotFound:
                await NotFound.WriteAsync(context, path);
                return;
            case RenderStatus.CompileFailed:
                await WriteCompileErrorAsync(context, result.Diagnostics);
                return;
            case RenderStatus.Timeout:
                await WriteTimeoutAsync(context, path);
                return;
        }

        if (result.IsSource) {
            // The first render of a seed may have learned the revision has no code. That answer is
            // now stored, so the next request takes the fast path above. Nothing wraps a no-code
            // page: its source is the complete representation.
            await WriteSourceAsync(context, path, page.GetBody(), versionHash, environment);

            return;
        }

        if (versionHash is not null) {
            environment.Metrics.RecordAsset(PageMetrics.AssetNonAsset);
        }

        Task write = (await chain.WrapAsync(render, path, contentLayout, page.ToResponse(), page.GetBody())) switch {
            PageChainResult.Failed failure => ErrorResponses.WriteAsync(context, failure.StatusCode, failure.Error),
            PageChainResult.Served served => WritePageAsync(context, served.Response, served.Body, environment, session),
        };

        await write;
    }

    // The shell promises a year only for a page with no code, and only under a URL that names one
    // revision. The hash is the validator, so a browser that revalidates anyway gets a cheap 304.
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private static async Task WriteSourceAsync(
        HttpContext context,
        PagePath path,
        string source,
        string? versionHash,
        PageEnvironment environment) {
        byte[] body = Encoding.UTF8.GetBytes(source);
        context.Response.ContentType = environment.ContentTypes.ForPath(path.Key);

        if (versionHash is not null) {
            context.Response.Headers.CacheControl = ImmutableCacheControl;
            context.Response.Headers.ETag = $"\"{versionHash}\"";

            if (IsNotModified(context, versionHash)) {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                environment.Metrics.RecordAsset(PageMetrics.AssetNotModified);
                return;
            }

            environment.Metrics.RecordAsset(PageMetrics.AssetServed);
        } else {
            SecurityHeaders.NoStore(context);
            context.Response.Headers.Remove("ETag");
            environment.Metrics.RecordAsset(PageMetrics.AssetServed);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentLength = body.LongLength;

        if (!HttpMethods.IsHead(context.Request.Method)) {
            await context.Response.Body.WriteAsync(body.AsMemory(), context.RequestAborted);
        }
    }

    private static bool IsNotModified(HttpContext context, string hash) {
        foreach (string? value in context.Request.Headers.IfNoneMatch) {
            if (value is null) {
                continue;
            }

            foreach (string candidate in value.Split(',')) {
                string trimmed = candidate.Trim();

                if (string.Equals(trimmed, "*", StringComparison.Ordinal)) {
                    return true;
                }

                if (trimmed.StartsWith("W/", StringComparison.Ordinal)) {
                    trimmed = trimmed[2..];
                }

                if (trimmed.Length >= 2
                    && trimmed[0] == '"'
                    && trimmed[^1] == '"'
                    && string.Equals(trimmed[1..^1], hash, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        return false;
    }

    // A versioned URL is a fixed representation, so nothing about it needs a request body, and a
    // revision whose code writes must never be reachable by a cached URL.
    private static Task WriteMethodNotAllowedAsync(HttpContext context) {
        SecurityHeaders.NoStore(context);
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD";
        context.Response.ContentLength = 0;

        return Task.CompletedTask;
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
        PageEnvironment environment,
        PageSessionRequest session) {
        if (response.Location is not null) {
            // A redirect can carry the promise of a page the visitor is not yet allowed to see, so it
            // is never cached, whoever asked.
            SecurityHeaders.NoStore(context);
            WriteSessionCookie(context, environment, session);
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

        // Every page that runs code is per-visitor: a page session can be written by any render, so
        // the body has no business in a shared cache and no business inviting a conditional request.
        // Copies first, then overrides, so a page cannot weaken it with SetHeader.
        SecurityHeaders.NoStore(context);
        context.Response.Headers.Remove("ETag");
        WriteSessionCookie(context, environment, session);

        if (response.Status is >= StatusCodes.Status200OK
            and not StatusCodes.Status204NoContent
            and not StatusCodes.Status304NotModified) {
            context.Response.ContentLength = bodyBytes.LongLength;
        }

        if (!HttpMethods.IsHead(context.Request.Method)) {
            await context.Response.Body.WriteAsync(bodyBytes.AsMemory(), context.RequestAborted);
        }
    }

    // Minted lazily, so a page that only reads costs a visitor no cookie and the next write is the
    // first thing that needs one.
    private static void WriteSessionCookie(HttpContext context, PageEnvironment environment, PageSessionRequest session) {
        if (session.NeedsCookie) {
            PageSessionCookie.Write(context, session.Id, environment.SessionCookie);
        }
    }

    private static async Task WriteCompileErrorAsync(HttpContext context, IReadOnlyList<PageDiagnostic> diagnostics) {
        await ShellDocument.BeginAsync(context, "Compilation failed",
            SiteNavigation.For(context), StatusCodes.Status500InternalServerError);
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
