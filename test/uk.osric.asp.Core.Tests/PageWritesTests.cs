// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

/// <summary>
/// The changed-pages card trusts the tool result envelope, so every shape it must refuse is a test:
/// a refused write changed nothing, and a tool that only read a page changed nothing either.
/// </summary>
[TestFixture]
public sealed class PageWritesTests {

    [Test]
    public void AFactSayingTheToolWritesNoPagesYieldsNothing() {
        string result = """{"ok":true,"data":{"path":"/blog/hello","revisionHash":"abc"},"error":null}""";

        Assert.That(PageWrites.Written(changesPages: false, result), Is.Null);
    }

    [Test]
    public void ARefusedWriteYieldsNothing() {
        const string result = """{"ok":false,"data":null,"error":"Write refused"}""";

        Assert.That(PageWrites.Written(changesPages: true, result), Is.Null);
    }

    [Test]
    public void AFactSayingTheToolWritesPagesYieldsItsPath() {
        string result = """{"ok":true,"data":{"path":"/blog/hello","revisionHash":"abc"},"error":null}""";

        Assert.That(PageWrites.Written(changesPages: true, result)?.Key, Is.EqualTo("/blog/hello"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("{")]
    [TestCase("[]")]
    [TestCase("""{"ok":true}""")]
    [TestCase("""{"ok":true,"data":{"path":null}}""")]
    [TestCase("""{"ok":true,"data":{"path":7}}""")]
    public void AResultWithoutARecognisablePathYieldsNothing(string? result) {
        Assert.That(PageWrites.Written(changesPages: true, result), Is.Null);
    }

    [Test]
    public void APathNoRequestCanAddressYieldsNothing() {
        const string result = """{"ok":true,"data":{"path":"/hello?world"},"error":null}""";

        Assert.That(PageWrites.Written(changesPages: true, result), Is.Null);
    }

    [Test]
    public void ThePathIsReturnedInItsCanonicalSpelling() {
        const string result = """{"ok":true,"data":{"path":"/Blog//Hello"},"error":null}""";

        Assert.That(PageWrites.Written(changesPages: true, result)?.Key, Is.EqualTo("/blog/hello"));
    }
}
