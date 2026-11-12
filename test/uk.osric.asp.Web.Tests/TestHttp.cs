// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Request and response plumbing shared by the endpoint fixtures.</summary>
internal static class TestHttp {

    // A real DefaultHttpContext throws from RequestServices when nothing supplied one, and the shell
    // reaches for a logger there on the paths a test only sometimes takes. The documented error page
    // also asks which keys the site's own routes own, so the reserved list is here as it is in the
    // host.
    private static readonly IServiceProvider s_services = new ServiceCollection()
        .AddLogging()
        .AddSingleton(new ReservedPaths(ReservedPaths.Default))
        .BuildServiceProvider();

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
        context.RequestServices = s_services;
        return context;
    }

    /// <summary>Builds a request snapshot for pages that never look at one.</summary>
    /// <param name="path">The canonical page key.</param>
    /// <param name="method">The HTTP method.</param>
    public static PageRequest Request(string path = "/", string method = "GET") {
        Dictionary<string, string> empty = [];
        return new PageRequest(method, path, empty, empty, empty, empty);
    }

    /// <summary>Reads everything written to the response body.</summary>
    /// <param name="context">The context created by <see cref="CreateContext"/>.</param>
    public static async Task<string> ReadBodyAsync(HttpContext context) {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
