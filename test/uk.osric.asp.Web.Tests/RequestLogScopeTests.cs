// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using uk.osric.asp.Web.Logging;

/// <summary>The ambient facts a request puts on every line written while it is answered.</summary>
[TestFixture]
public sealed class RequestLogScopeTests {

    [Test]
    public async Task TheScopeNamesTheExchangeTheAccountAndTheRoutesIdsAsync() {
        RecordingLoggerFactory logs = await RunAsync(context => {
            context.Request.Method = "POST";
            context.Request.Path = "/assistant/42/runs/abc123";
            context.Request.RouteValues["id"] = "42";
            context.Request.RouteValues["runId"] = "abc123";
            context.Request.RouteValues["hash"] = "deadbeef";
            context.User = Authenticated("osric");
        });

        IReadOnlyDictionary<string, object?> scope = logs.Entries.Single().Scope;

        Assert.Multiple(() => {
            Assert.That(scope["RequestId"], Is.EqualTo("trace-1"));
            Assert.That(scope["Method"], Is.EqualTo("POST"));
            Assert.That(scope["Path"], Is.EqualTo("/assistant/42/runs/abc123"));
            Assert.That(scope["UserName"], Is.EqualTo("osric"));
            Assert.That(scope["Id"], Is.EqualTo("42"), "a route value that names something becomes a field, capitalised");
            Assert.That(scope["RunId"], Is.EqualTo("abc123"));
            Assert.That(scope.ContainsKey("hash"), Is.False, "a route value that names nothing is not a field");
        });
    }

    [Test]
    public async Task AnAnonymousRequestCarriesNoUserNameAsync() {
        RecordingLoggerFactory logs = await RunAsync(context => context.Request.Path = "/about");

        IReadOnlyDictionary<string, object?> scope = logs.Entries.Single().Scope;

        Assert.Multiple(() => {
            Assert.That(scope.ContainsKey("UserName"), Is.False, "an account that is not known is not a field");
            Assert.That(scope["Path"], Is.EqualTo("/about"), "the rest of the scope is still there");
        });
    }

    [Test]
    public async Task TheRouteSaysWhichIdTheEndpointWasAskedForAsync() {
        RecordingLoggerFactory logs = await RunAsync(context => {
            context.Request.RouteValues["id"] = "42";
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/assistant/{id:long}/runs/{runId}"),
                order: 0,
                metadata: [],
                displayName: "assistant"));
        });

        IReadOnlyDictionary<string, object?> scope = logs.Entries.Single().Scope;

        Assert.Multiple(() => {
            Assert.That(scope["Id"], Is.EqualTo("42"));
            Assert.That(scope["Route"], Is.EqualTo("/assistant/{id:long}/runs/{runId}"),
                "an id on its own does not say what it identifies");
        });
    }

    // The scope is opened before routing and authentication have run, so the test drives the whole
    // pipeline: the log line is written at the end of it, exactly as an endpoint would.
    private static async Task<RecordingLoggerFactory> RunAsync(Action<HttpContext> arrange) {
        RecordingLoggerFactory logs = new();
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(logs)
            .BuildServiceProvider();

        DefaultHttpContext context = new() {
            RequestServices = services,
            TraceIdentifier = "trace-1",
        };
        arrange(context);

        IApplicationBuilder app = new ApplicationBuilder(services);
        app.UseRequestLogScope();
        app.UseRequestLogSubject();
        app.Run(next => {
            next.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("uk.osric.asp.Probe")
                .LogInformation("the endpoint answered");
            return Task.CompletedTask;
        });

        await app.Build().Invoke(context);

        return logs;
    }

    private static ClaimsPrincipal Authenticated(string userName) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userName)], "test"));
}
