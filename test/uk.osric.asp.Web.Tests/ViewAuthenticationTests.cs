// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Users;

/// <summary>The scheme that reads a run's token, and the guard that holds it to two rules.</summary>
[TestFixture]
public sealed class ViewAuthenticationTests {

    [Test]
    public async Task ARequestWithNoBearerHeaderIsNotThisSchemeAtAllAsync() {
        Harness harness = NewHarness();

        AuthenticateResult result = await harness.AuthenticateAsync(header: null);

        Assert.That(result.None, Is.True, "the cookie keeps every request that is not a view");
    }

    [Test]
    public async Task AMintedTokenAuthenticatesTheRunAndItsOwnerAsync() {
        Harness harness = NewHarness();
        ViewToken token = harness.Store.Mint("run-1", "owner", "stamp");

        AuthenticateResult result = await harness.AuthenticateAsync($"Bearer {token.Secret}");

        Assert.Multiple(() => {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("owner"));
            Assert.That(result.Principal!.FindFirstValue(ViewToken.Claim), Is.EqualTo("run-1"));
            Assert.That(result.Principal!.Identity!.AuthenticationType, Is.EqualTo(ViewToken.AuthenticationScheme));
        });
    }

    [Test]
    public async Task AnUnknownTokenFailsRatherThanBecomingAnonymousAsync() {
        Harness harness = NewHarness();

        AuthenticateResult result = await harness.AuthenticateAsync("Bearer not-a-token");

        Assert.That(result.Failure, Is.Not.Null, "the guard turns this into a 401, not a stranger's view");
    }

    [Test]
    public async Task ATokenWhoseAccountIsGoneFailsAsync() {
        Harness harness = NewHarness();
        ViewToken token = harness.Store.Mint("run-1", "deleted", "stamp");

        AuthenticateResult result = await harness.AuthenticateAsync($"Bearer {token.Secret}");

        Assert.That(result.Failure, Is.Not.Null);
    }

    [Test]
    public async Task ATokenWhoseStampHasMovedFailsAsync() {
        Harness harness = NewHarness(User("owner", "rotated"));
        ViewToken token = harness.Store.Mint("run-1", "owner", "stamp");

        AuthenticateResult result = await harness.AuthenticateAsync($"Bearer {token.Secret}");

        Assert.That(result.Failure, Is.Not.Null, "users passwd mid-run takes the view away");
    }

    [Test]
    public async Task ABearerHeaderThatDidNotAuthenticateIsRefusedWithAChallengeAsync() {
        DefaultHttpContext context = NewHarness().Context("Bearer not-a-token");

        bool ran = await GuardAsync(context);

        Assert.Multiple(() => {
            Assert.That(ran, Is.False, "the page never runs for a token that did not verify");
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(context.Response.Headers.WWWAuthenticate.ToString(), Is.EqualTo(ViewToken.HeaderScheme));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Does.Contain("no-store"));
        });
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task AViewMayRead(string method) {
        DefaultHttpContext context = NewHarness().Context($"Bearer {Secret()}", ViewPrincipal());

        bool ran = await GuardAsync(context, method);

        Assert.That(ran, Is.True);
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public async Task AViewMayNotWrite(string method) {
        DefaultHttpContext context = NewHarness().Context($"Bearer {Secret()}", ViewPrincipal());

        bool ran = await GuardAsync(context, method);

        Assert.Multiple(() => {
            Assert.That(ran, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status405MethodNotAllowed));
            Assert.That(context.Response.Headers.Allow.ToString(), Is.EqualTo("GET, HEAD"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Does.Contain("no-store"));
        });
    }

    [Test]
    public async Task ARequestWithoutABearerHeaderIsLeftAloneAsync() {
        DefaultHttpContext context = NewHarness().Context(header: null);

        bool ran = await GuardAsync(context, "POST");

        Assert.Multiple(() => {
            Assert.That(ran, Is.True, "an ordinary anonymous post is unchanged");
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        });
    }

    [Test]
    public async Task ACookieIdentityThatIsNotAViewKeepsEveryMethodItHadAsync() {
        ClaimsIdentity cookie = new([new Claim(ClaimTypes.NameIdentifier, "owner")], "Cookies");
        DefaultHttpContext context = NewHarness().Context($"Bearer {Secret()}", new ClaimsPrincipal(cookie));

        bool ran = await GuardAsync(context, "POST");

        Assert.That(ran, Is.True, "a signed-in browser is not held to a view's scope");
    }

    private static ViewTokenStore Store() => new(TestOptions.Assistant);

    private static string Secret() => Convert.ToBase64String(new byte[32]);

    /// <summary>The scheme, its token store, and the accounts it validates against.</summary>
    private sealed class Harness(FakeUserStore users, ViewTokenStore store) {
        private readonly ServiceProvider _services = new ServiceCollection().AddSingleton<IUserStore>(users).BuildServiceProvider();

        public FakeUserStore Users { get; } = users;

        public ViewTokenStore Store { get; } = store;

        public IAuthenticationHandler Handler { get; } = new ViewAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            store);

        public DefaultHttpContext Context(string? header, ClaimsPrincipal? principal = null) {
            DefaultHttpContext context = new() { RequestServices = _services };
            if (header is not null) {
                context.Request.Headers.Authorization = header;
            }

            if (principal is not null) {
                context.User = principal;
            }

            return context;
        }

        public async Task<AuthenticateResult> AuthenticateAsync(string? header) {
            DefaultHttpContext context = Context(header);
            await Handler.InitializeAsync(
                new AuthenticationScheme(ViewToken.AuthenticationScheme, null, typeof(ViewAuthenticationHandler)),
                context);

            return await Handler.AuthenticateAsync();
        }
    }

    private static ClaimsPrincipal ViewPrincipal() {
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ViewToken.Claim, "run-1")],
            ViewToken.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    private static LocalUser User(string name, string stamp) => new() {
        UserName = name,
        PasswordHash = "hash",
        SecurityStamp = stamp,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static Harness NewHarness(params LocalUser[] accounts) {
        FakeUserStore users = new();
        foreach (LocalUser user in accounts) {
            users.Users[user.UserName] = user;
        }

        if (users.Users.Count == 0) {
            users.Users["owner"] = User("owner", "stamp");
        }

        return new Harness(users, Store());
    }

    private static async Task<bool> GuardAsync(DefaultHttpContext context, string method = "GET") {
        context.Request.Method = method;
        bool ran = false;
        await ViewGuard.ApplyAsync(context, _ => {
            ran = true;

            return Task.CompletedTask;
        });

        return ran;
    }


    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T> {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
