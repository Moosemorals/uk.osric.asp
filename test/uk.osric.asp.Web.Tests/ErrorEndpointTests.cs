// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Endpoints;

[TestFixture]
public sealed class ErrorEndpointTests {

    [Test]
    public async Task UnknownErrorReturnsServerErrorWithGenericMessageAsync() {
        DefaultHttpContext context = CreateContext(null);

        await ErrorEndpoint.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("An unexpected error occurred."));
    }

    [Test]
    public async Task UserFacingExceptionMessageIsShownAsync() {
        DefaultHttpContext context = CreateContext(new UserFacingException("The page could not be saved."));

        await ErrorEndpoint.HandleAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("The page could not be saved."));
    }

    [Test]
    public async Task WrappedUserFacingMessageIsNotTrustedAsync() {
        InvalidOperationException failure = new("Template gave up.", new UserFacingException("Template gave up."));
        DefaultHttpContext context = CreateContext(failure);

        await ErrorEndpoint.HandleAsync(context);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Not.Contain("Template gave up."));
        Assert.That(body, Does.Contain("An unexpected error occurred."));
    }

    [Test]
    public async Task MessageAndReferenceAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext(new UserFacingException("<script>alert('x')</script>"));
        context.TraceIdentifier = "<trace>&";

        await ErrorEndpoint.HandleAsync(context);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Not.Contain("<script>"));
        Assert.That(body, Does.Contain("&lt;script&gt;"));
        Assert.That(body, Does.Contain("&lt;trace&gt;&amp;"));
    }

    private static DefaultHttpContext CreateContext(Exception? error) {
        DefaultHttpContext context = TestHttp.CreateContext(null);
        context.TraceIdentifier = "trace";
        if (error is not null) {
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature {
                Error = error,
                Path = "/boom",
            });
        }
        return context;
    }
}
