// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Authentication;

[TestFixture]
public sealed class ViewTokenStoreTests {

    [Test]
    public void AMintedTokenResolvesToItsRunAndItsOwner() {
        ViewTokenStore store = NewStore();

        ViewToken token = store.Mint("run-1", "osric", "stamp-1");

        Assert.Multiple(() => {
            Assert.That(store.TryResolve(token.Secret, out ViewToken? resolved), Is.True);
            Assert.That(resolved, Is.SameAs(token));
            Assert.That(token.RunId, Is.EqualTo("run-1"));
            Assert.That(token.Owner, Is.EqualTo("osric"));
            Assert.That(token.SecurityStamp, Is.EqualTo("stamp-1"));
            Assert.That(token.Secret, Has.Length.GreaterThanOrEqualTo(32));
        });
    }

    [Test]
    public void ATokenIsFoundByItsRun() {
        ViewTokenStore store = NewStore();
        ViewToken token = store.Mint("run-1", "osric", "stamp-1");

        Assert.Multiple(() => {
            Assert.That(store.ForRun("run-1"), Is.SameAs(token));
            Assert.That(store.ForRun("run-2"), Is.Null);
        });
    }

    [Test]
    public void AForgottenTokenNoLongerResolves() {
        ViewTokenStore store = NewStore();
        ViewToken token = store.Mint("run-1", "osric", "stamp-1");

        store.Forget(token);

        Assert.Multiple(() => {
            Assert.That(store.TryResolve(token.Secret, out _), Is.False, "a run that has ended holds no view");
            Assert.That(store.ForRun("run-1"), Is.Null);
            Assert.That(store.Count, Is.Zero);
        });
    }

    [Test]
    public void TwoRunsNeverShareAToken() {
        ViewTokenStore store = NewStore();

        ViewToken first = store.Mint("run-1", "osric", "stamp-1");
        ViewToken second = store.Mint("run-2", "osric", "stamp-1");

        Assert.Multiple(() => {
            Assert.That(first.Secret, Is.Not.EqualTo(second.Secret));
            Assert.That(store.TryResolve(second.Secret, out ViewToken? resolved), Is.True);
            Assert.That(resolved!.RunId, Is.EqualTo("run-2"));
        });
    }

    [TestCase("")]
    [TestCase("not-a-token")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void AnUnknownSecretResolvesToNothing(string secret) {
        ViewTokenStore store = NewStore();
        _ = store.Mint("run-1", "osric", "stamp-1");

        Assert.That(store.TryResolve(secret, out ViewToken? token), Is.False);
        Assert.That(token, Is.Null);
    }

    [Test]
    public void ASecondTokenForOneRunIsRefused() {
        ViewTokenStore store = NewStore();
        _ = store.Mint("run-1", "osric", "stamp-1");

        Assert.That(() => store.Mint("run-1", "osric", "stamp-1"), Throws.InvalidOperationException);
    }

    [Test]
    public void TheStoreHoldsNoMoreThanTheActiveRunLimit() {
        ViewTokenStore store = NewStore();
        _ = store.Mint("run-1", "osric", "stamp-1");
        _ = store.Mint("run-2", "osric", "stamp-1");

        Assert.Multiple(() => {
            Assert.That(store.Count, Is.EqualTo(TestOptions.Assistant.MaxActiveRuns));
            Assert.That(() => store.Mint("run-3", "osric", "stamp-1"), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void ATokenNeverPrintsItsSecret() {
        ViewTokenStore store = NewStore();
        ViewToken token = store.Mint("run-1", "osric", "stamp-1");

        Assert.Multiple(() => {
            Assert.That(token.ToString(), Does.Not.Contain(token.Secret));
            Assert.That(token.ToString(), Does.Contain("run-1"));
        });
    }

    private static ViewTokenStore NewStore() => new(TestOptions.Assistant);
}
