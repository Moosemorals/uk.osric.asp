// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Users;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class UserCommandsTests {

    private const string Password = "correct horse battery staple";

    private readonly FakeUserStore _users = new();
    private readonly FakePasswordService _passwords = new();
    private readonly ScriptedUserConsole _console = new();

    [Test]
    public async Task AddStoresTheNormalisedAccountAsync() {
        QueuePassword(Password);

        int exit = await RunAsync("add", " Osric ");

        Assert.That(exit, Is.Zero);
        Assert.That(_users.Users.Keys, Is.EqualTo(["osric"]));
        Assert.That(_users.Users["osric"].PasswordHash, Is.EqualTo("hash:" + Password));
        Assert.That(_users.Users["osric"].SecurityStamp, Is.Not.Empty);
        Assert.That(_console.Output, Has.Some.Contains("Added 'osric'."));
    }

    [Test]
    public async Task AddRejectsAnExistingNameAsync() {
        _users.Users["owner"] = User("owner");
        QueuePassword(Password);

        int exit = await RunAsync("add", "owner");

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(_console.Errors, Has.Some.Contains("already exists"));
    }

    [Test]
    public async Task AddRejectsAMismatchedConfirmationAsync() {
        _console.AddPassword(Password);
        _console.AddPassword("different password");

        int exit = await RunAsync("add", "owner");

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(_console.Errors, Has.Some.Contains("do not match"));
        Assert.That(_users.Users, Is.Empty);
    }

    [Test]
    public async Task AddRejectsAShortPasswordAsync() {
        QueuePassword("short");

        int exit = await RunAsync("add", "owner");

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(_console.Errors, Has.Some.Contains("at least"));
        Assert.That(_users.Users, Is.Empty);
    }

    [Test]
    public async Task AddReadsOnePasswordFromStandardInputAsync() {
        _console.AddPassword(Password);

        int exit = await RunAsync("add", "owner", "--password-stdin");

        Assert.That(exit, Is.Zero);
        Assert.That(_users.Users, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PasswdChangesTheHashAndRotatesTheStampAsync() {
        LocalUser original = User("owner");
        _users.Users["owner"] = original;
        QueuePassword("a different long password");

        int exit = await RunAsync("passwd", "owner");

        Assert.That(exit, Is.Zero);
        Assert.That(_users.Users["owner"].PasswordHash, Is.EqualTo("hash:a different long password"));
        Assert.That(_users.Users["owner"].SecurityStamp, Is.Not.EqualTo(original.SecurityStamp));
        Assert.That(_console.Output, Has.Some.Contains("Password changed for 'owner'."));
    }

    [Test]
    public async Task PasswdRejectsAMissingAccountAsync() {
        int exit = await RunAsync("passwd", "missing");

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(_console.Errors, Has.Some.Contains("No account named 'missing'."));
    }

    [Test]
    public async Task ListPrintsNamesWithoutHashesAsync() {
        _users.Users["alpha"] = User("alpha");
        _users.Users["zed"] = User("zed");

        int exit = await RunAsync("list");

        Assert.That(exit, Is.Zero);
        Assert.That(_console.Output, Has.Some.Contains("alpha"));
        Assert.That(_console.Output, Has.Some.Contains("zed"));
        Assert.That(_console.Output, Has.None.Contains("hash"));
    }

    [Test]
    public async Task ListReportsAnEmptyStoreAsync() {
        int exit = await RunAsync("list");

        Assert.That(exit, Is.Zero);
        Assert.That(_console.Output, Is.EqualTo(["No accounts."]));
    }

    [Test]
    public async Task RemoveDeletesTheAccountAsync() {
        _users.Users["owner"] = User("owner");

        int exit = await RunAsync("remove", "owner");

        Assert.That(exit, Is.Zero);
        Assert.That(_users.Users, Is.Empty);
        Assert.That(_console.Output, Has.Some.Contains("Removed 'owner'."));
    }

    [Test]
    public async Task RemoveRejectsAMissingAccountAsync() {
        int exit = await RunAsync("remove", "missing");

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(_console.Errors, Has.Some.Contains("No account named 'missing'."));
    }

    [TestCase("add")]
    [TestCase("passwd")]
    [TestCase("remove")]
    public async Task AMissingNameWritesUsageAsync(string command) {
        int exit = await RunAsync(command);

        Assert.That(exit, Is.EqualTo(2));
        Assert.That(_console.Errors, Has.Some.Contains("usage: users"));
    }

    [Test]
    public async Task AnUnknownCommandWritesUsageAsync() {
        int exit = await RunAsync("explode");

        Assert.That(exit, Is.EqualTo(2));
        Assert.That(_console.Errors, Has.Some.Contains("usage: users"));
    }

    [Test]
    public void TheUsersCommandIsRecognised() {
        Assert.That(UserCommands.IsCommand(["users", "list"]), Is.True);
        Assert.That(UserCommands.IsCommand(["user"]), Is.False);
        Assert.That(UserCommands.IsCommand([]), Is.False);
    }

    private Task<int> RunAsync(params string[] args) {
        return UserCommands.RunAsync(args, _users, _passwords, _console, CancellationToken.None);
    }

    private void QueuePassword(string password) {
        _console.AddPassword(password);
        _console.AddPassword(password);
    }

    private static LocalUser User(string name) {
        return new LocalUser {
            UserName = name,
            PasswordHash = "hash:old",
            SecurityStamp = "old-stamp",
            CreatedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
        };
    }
}
