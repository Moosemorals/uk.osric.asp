// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
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
        ReservedPaths reserved,
        RenderOptions options) {

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
        if (reserved.IsReserved(path)) {
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
        } catch (InvalidDataException) {
            // The form parser refuses a key or value past its limit. That is the client's fault, so
            // it is a 400 rather than the error page.
            await WriteBadRequestAsync(context, path);
            return;
        }

        StringWriter output = new();
        SimpleContext page = new(
            loggerFactory,
            snapshot,
            context.RequestAborted,
            output,
            EmptyPageStorage.Instance,
            DisabledPageFetcher.Instance,
            options.MaxBodyChars);

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
                await WritePageAsync(context, page.ToResponse(), page.GetBody());
                break;
        }
    }

    // The page's body is buffered until here, so the response is written in one go: status and
    // headers first, and nothing at all when the page redirected or the request was a HEAD.
    private static async Task WritePageAsync(HttpContext context, PageResponse response, string body) {
        if (response.Location is not null) {
            Redirect.To(context, response.Status, response.Location);
            return;
        }

        context.Response.StatusCode = response.Status;
        context.Response.ContentType = response.ContentType;
        foreach ((string name, string value) in response.Headers) {
            context.Response.Headers[name] = value;
        }

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        EntityTagHeaderValue? entityTag = null;
        if (response.Status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices) {
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

    private static async Task WriteTimeoutAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} is taking too long.");
    }

    private static async Task WriteBadRequestAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} rejected: the form could not be read.");
    }

    private static async Task WriteTooLargeAsync(HttpContext context, PagePath path) {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync($"{path} rejected: the request body is too large.");
    }

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
