// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using uk.osric.asp.Web.Http;

[TestFixture]
public sealed class SecurityHeadersTests {

    private static readonly string[] ExpectedHeaderNames = [
        SecurityHeaders.ContentSecurityPolicy,
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Cross-Origin-Opener-Policy",
        "Cross-Origin-Resource-Policy",
        "Referrer-Policy",
        "Permissions-Policy",
        "X-XSS-Protection",
    ];

    [Test]
    public async Task EveryHeaderIsWrittenWhenTheResponseStarts() {
        DefaultHttpContext context = new();

        await StartAsync(context);

        foreach (string name in ExpectedHeaderNames) {
            Assert.That(context.Response.Headers.ContainsKey(name), Is.True, name);
        }
    }

    [Test]
    public void TheDefaultPolicyForbidsScripts() {
        Assert.That(SecurityHeaders.Default, Does.Contain("script-src 'none'"));
    }

    [Test]
    public void TheEditorPolicyAllowsSameOriginScripts() {
        Assert.That(SecurityHeaders.WithScripts, Does.Contain("script-src 'self'"));
    }

    [Test]
    public void TheTwoPoliciesDifferInExactlyOneDirective() {
        string[] defaults = SecurityHeaders.Default.Split("; ");
        string[] withScripts = SecurityHeaders.WithScripts.Split("; ");

        Assert.That(withScripts, Has.Length.EqualTo(defaults.Length));
        Assert.That(
            defaults.Zip(withScripts).Where(pair => pair.First != pair.Second).Select(pair => pair.First),
            Is.EqualTo(new List<string> { "script-src 'none'" }));
    }

    [Test]
    public async Task TheEditorPolicySurvivesTheResponseStarting() {
        DefaultHttpContext context = new();

        await StartAsync(context, current => {
            SecurityHeaders.AllowScripts(current);
            return Task.CompletedTask;
        });

        Assert.That(context.Response.Headers[SecurityHeaders.ContentSecurityPolicy].ToString(),
            Is.EqualTo(SecurityHeaders.WithScripts));
        Assert.That(context.Response.Headers[SecurityHeaders.ContentSecurityPolicy].Count, Is.EqualTo(1));
    }

    [Test]
    public async Task AHeaderTheEndpointAlreadySetIsKept() {
        DefaultHttpContext context = new();

        await StartAsync(context, current => {
            current.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });

        Assert.That(context.Response.Headers["Referrer-Policy"].ToString(), Is.EqualTo("no-referrer"));
    }

    [Test]
    public async Task AClearedResponseGetsTheDefaultPolicyFromTheErrorPage() {
        DefaultHttpContext context = new();

        await StartAsync(context, current => {
            SecurityHeaders.AllowScripts(current);
            current.Response.Clear();
            return Task.CompletedTask;
        });

        Assert.That(context.Response.Headers[SecurityHeaders.ContentSecurityPolicy].ToString(),
            Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public void AllowingScriptsTwiceStillLeavesOneValue() {
        DefaultHttpContext context = new();

        SecurityHeaders.AllowScripts(context);
        SecurityHeaders.AllowScripts(context);

        Assert.That(context.Response.Headers[SecurityHeaders.ContentSecurityPolicy],
            Is.EqualTo(new StringValues(SecurityHeaders.WithScripts)));
    }

    private static async Task StartAsync(DefaultHttpContext context, RequestDelegate? next = null) {
        TestResponseFeature response = new();
        context.Features.Set<IHttpResponseFeature>(response);

        await SecurityHeaders.ApplyAsync(context, next ?? (_ => Task.CompletedTask));
        await response.StartAsync();
    }

    // The server's own feature runs the OnStarting callbacks; the default one in a unit test does not.
    private sealed class TestResponseFeature : IHttpResponseFeature {

        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) {
            // Nothing in the header set runs when the response completes.
        }

        public async Task StartAsync() {
            HasStarted = true;

            foreach ((Func<object, Task> callback, object state) in Enumerable.Reverse(_starting)) {
                await callback(state);
            }
        }
    }
}
