// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.WebTests;

using Microsoft.AspNetCore.Http;

/// <summary>Request and response plumbing shared by the endpoint fixtures.</summary>
internal static class TestHttp {

    /// <summary>Builds a context whose body can be read back after the handler has run.</summary>
    /// <param name="path">The route's <c>path</c> value.</param>
    /// <param name="method">The HTTP method to present.</param>
    /// <param name="requestPath">The request path, used by endpoints that redirect to themselves.</param>
    public static DefaultHttpContext CreateContext(string? path, string method = "GET", string requestPath = "/") {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = requestPath;
        context.Request.RouteValues["path"] = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Reads everything written to the response body.</summary>
    /// <param name="context">The context created by <see cref="CreateContext"/>.</param>
    public static async Task<string> ReadBodyAsync(HttpContext context) {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
