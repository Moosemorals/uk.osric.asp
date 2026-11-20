// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

/// <summary>What a job's borrowed credential answers, and what makes it dead.</summary>
/// <remarks>
/// The checks are the ones the cookie and the view make on every request: the account still exists
/// with the stamp the credential captured, and its handle still resolves. These tests prove each half
/// can end it, because a job that keeps a write authority forever is the thing this exists to stop.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageJobAuthorityTests {

    private const string Stamp = "stamp";
    private static readonly PageUser Owner = new("owner");

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private ServiceProvider _provider = null!;
    private FakeUserStore _users = null!;
    private AuthTicketStore _tickets = null!;
    private ViewTokenStore _views = null!;
    private PageJobAuthorityVerifier _verifier = null!;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("job-authority");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void SetUp() {
        _databasePath = s_template!.CreateFile("job-authority");
        _users = new FakeUserStore();
        _users.Users[Owner.Name] = new LocalUser {
            UserName = Owner.Name,
            PasswordHash = "unused",
            SecurityStamp = Stamp,
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        ServiceCollection services = new();
        services.AddSingleton<IUserStore>(_users);
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(DatabaseTemplate.ConnectionString(_databasePath)));
        services.AddSingleton<AuthTicketStore>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        _tickets = _provider.GetRequiredService<AuthTicketStore>();
        _views = new ViewTokenStore(TestOptions.Assistant);
        _verifier = new PageJobAuthorityVerifier(
            _provider.GetRequiredService<IServiceScopeFactory>(), _tickets, _views, TimeProvider.System);
    }

    [TearDown]
    public void TearDown() {
        _provider.Dispose();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    public async Task ALiveViewCredentialIsLiveAsync() {
        ViewToken token = _views.Mint("run-1", Owner.Name, Stamp);
        PageJobAuthority authority = View(token.RunId);

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.True);
    }

    [Test]
    public async Task AViewCredentialDiesWithItsRunAsync() {
        ViewToken token = _views.Mint("run-1", Owner.Name, Stamp);
        PageJobAuthority authority = View(token.RunId);
        _views.Forget(token);

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False,
            "the run ending is what revokes a view's authority");
    }

    [Test]
    public async Task AViewCredentialDiesWhenTheAccountChangesAsync() {
        ViewToken token = _views.Mint("run-1", Owner.Name, Stamp);
        PageJobAuthority authority = View(token.RunId);
        _ = await _users.ChangePasswordAsync(Owner.Name, "hash", "rotated", CancellationToken.None);

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False,
            "the view handler compares the account's stamp, so the job must too");
    }

    [Test]
    public async Task ACookieCredentialIsLiveWhileItsTicketIsAsync() {
        _tickets.Add(Ticket("key", DateTime.UtcNow.AddDays(30)));
        PageJobAuthority authority = Cookie("key");

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.True);
    }

    [Test]
    public async Task ACookieCredentialDiesWithItsTicketAsync() {
        _tickets.Add(Ticket("key", DateTime.UtcNow.AddDays(30)));
        PageJobAuthority authority = Cookie("key");
        _ = _tickets.Remove("key");

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False,
            "sign-out deletes the row, so the next check finds nothing");
    }

    [Test]
    public async Task AnExpiredCookieCredentialIsDeadBeforeTheSweepAsync() {
        _tickets.Add(Ticket("key", DateTime.UtcNow.AddSeconds(-1)));
        PageJobAuthority authority = Cookie("key");

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False,
            "the sweep runs hourly, so the check must compare the expiry itself");
    }

    [Test]
    public async Task ACookieCredentialDiesWhenTheAccountIsRemovedAsync() {
        _tickets.Add(Ticket("key", DateTime.UtcNow.AddDays(30)));
        PageJobAuthority authority = Cookie("key");
        _ = await _users.RemoveAsync(Owner.Name, CancellationToken.None);

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False);
    }

    [Test]
    public async Task ACredentialWithNoHandleIsDeadAsync() {
        PageJobAuthority authority = new(Owner, Stamp, null, null, _verifier, NullLogger.Instance);

        Assert.That(await authority.IsLiveAsync(CancellationToken.None), Is.False);
    }

    private PageJobAuthority View(string runId) =>
        new(Owner, Stamp, null, runId, _verifier, NullLogger.Instance);

    private PageJobAuthority Cookie(string key) =>
        new(Owner, Stamp, key, null, _verifier, NullLogger.Instance);

    private static AuthTicket Ticket(string key, DateTime expiresAt) => new() {
        Key = key,
        UserName = Owner.Name,
        ProtectedTicket = "protected",
        ExpiresAt = expiresAt,
    };
}
