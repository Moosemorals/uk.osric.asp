// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class StoredTicketDataFormatTests {

    private const string CookieName = ".uk.osric.asp.auth";
    private static readonly DateTimeOffset Expiry = DateTimeOffset.UtcNow.AddDays(30);

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private ServiceProvider _provider = null!;
    private AuthTicketStore _tickets = null!;
    private HttpContextAccessor _http = null!;
    private StoredTicketDataFormat _format = null!;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("auth-tickets");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void SetUp() {
        _databasePath = s_template!.CreateFile("auth-tickets");
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(DatabaseTemplate.ConnectionString(_databasePath)));
        services.AddSingleton<AuthTicketStore>();
        _http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        services.AddSingleton<IHttpContextAccessor>(_http);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        _tickets = _provider.GetRequiredService<AuthTicketStore>();
        _format = new StoredTicketDataFormat(
            new TicketDataFormat(new EphemeralDataProtectionProvider().CreateProtector("tests")),
            _tickets,
            _http,
            CookieName);
    }

    [TearDown]
    public void TearDown() {
        _provider.Dispose();
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public void ProtectedValueIsAShortKeyAndRoundTrips() {
        AuthenticationTicket ticket = Ticket("owner", "stamp", Expiry);

        string key = _format.Protect(ticket);
        AuthenticationTicket? loaded = _format.Unprotect(key);

        Assert.That(key, Has.Length.EqualTo(22), "128 bits are 22 base64url characters");
        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(loaded?.Principal.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("owner"));
            Assert.That(loaded?.Principal.FindFirstValue(EditorPolicy.SecurityStampClaim), Is.EqualTo("stamp"));
            // The ticket serializer stores the expiry to the second.
            Assert.That(loaded?.Properties.ExpiresUtc, Is.EqualTo(Expiry).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void AnUnknownKeyDoesNotUnprotect() {
        Assert.That(_format.Unprotect("not-a-key"), Is.Null);
    }

    [Test]
    public void SigningInWithoutACookieGetsANewKey() {
        string first = _format.Protect(Ticket("owner", "stamp", Expiry));
        string second = _format.Protect(Ticket("owner", "stamp", Expiry));

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void RenewingKeepsTheKeyTheBrowserAlreadyHas() {
        string key = _format.Protect(Ticket("owner", "old", Expiry));
        RequestCarries(key);

        DateTimeOffset extended = Expiry.AddDays(30);
        string renewed = _format.Protect(Ticket("owner", "new", extended));

        AuthenticationTicket? loaded = _format.Unprotect(key);
        Assert.That(renewed, Is.EqualTo(key));
        Assert.That(loaded, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(loaded?.Principal.FindFirstValue(EditorPolicy.SecurityStampClaim), Is.EqualTo("new"));
            Assert.That(loaded?.Properties.ExpiresUtc, Is.EqualTo(extended).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void SigningInAsAnotherAccountReplacesTheKey() {
        string alice = _format.Protect(Ticket("alice", "stamp", Expiry));
        RequestCarries(alice);
        StoredTicketDataFormat.MarkSignIn(_http.HttpContext!);

        string bob = _format.Protect(Ticket("bob", "stamp", Expiry));

        Assert.That(bob, Is.Not.EqualTo(alice));
        Assert.That(_format.Unprotect(alice), Is.Null, "the superseded session must not stay reachable");
        Assert.That(_format.Unprotect(bob), Is.Not.Null);
    }

    [Test]
    public void SigningInReplacesAKeyTheBrowserAlreadyHas() {
        string previous = _format.Protect(Ticket("owner", "old", Expiry));
        RequestCarries(previous);
        StoredTicketDataFormat.MarkSignIn(_http.HttpContext!);

        string fresh = _format.Protect(Ticket("owner", "new", Expiry));

        Assert.That(fresh, Is.Not.EqualTo(previous));
        Assert.That(_format.Unprotect(previous), Is.Null, "the previous session ends at sign-in");
        Assert.That(_format.Unprotect(fresh)?.Principal.FindFirstValue(EditorPolicy.SecurityStampClaim), Is.EqualTo("new"));
    }

    [Test]
    public void RenewingAfterRevocationDoesNotResurrectTheSession() {
        string key = _format.Protect(Ticket("owner", "stamp", Expiry));
        Assert.That(_tickets.Remove(key), Is.True);
        RequestCarries(key);

        string renewed = _format.Protect(Ticket("owner", "stamp", Expiry.AddDays(1)));

        Assert.That(renewed, Is.EqualTo(key), "a renewal has no other key to fall back on");
        Assert.That(_format.Unprotect(renewed), Is.Null, "a revoked key must not become live again");
    }

    [Test]
    public void RemovingAKeyStopsItUnprotecting() {
        string key = _format.Protect(Ticket("owner", "stamp", Expiry));

        Assert.That(_tickets.Remove(key), Is.True);
        Assert.That(_format.Unprotect(key), Is.Null);
    }

    [Test]
    public void SweepingRemovesOnlyExpiredTickets() {
        string expired = _format.Protect(Ticket("owner", "stamp", DateTimeOffset.UtcNow.AddMinutes(-1)));
        string live = _format.Protect(Ticket("owner", "stamp", Expiry));

        int removed = _tickets.RemoveExpired(DateTime.UtcNow);

        Assert.Multiple(() => {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(_format.Unprotect(expired), Is.Null);
            Assert.That(_format.Unprotect(live), Is.Not.Null);
        });
    }

    private static AuthenticationTicket Ticket(string userName, string stamp, DateTimeOffset expires) {
        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, userName),
                new Claim(EditorPolicy.SecurityStampClaim, stamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = expires },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private void RequestCarries(string key) {
        _http.HttpContext = new DefaultHttpContext();
        _http.HttpContext.Request.Headers.Cookie = $"{CookieName}={key}";
    }
}
