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

    [TestCase("write_page")]
    [TestCase("edit_page")]
    public void ASuccessfulPageWriteYieldsItsPath(string toolName) {
        string result = """{"ok":true,"data":{"path":"/blog/hello","revisionHash":"abc"},"error":null}""";

        Assert.That(PageWrites.Written(toolName, result)?.Key, Is.EqualTo("/blog/hello"));
    }

    [TestCase("read_page")]
    [TestCase("delete_page")]
    [TestCase("list_pages")]
    [TestCase(null)]
    public void AToolThatDoesNotWriteAPageYieldsNothing(string? toolName) {
        string result = """{"ok":true,"data":{"path":"/blog/hello"},"error":null}""";

        Assert.That(PageWrites.Written(toolName, result), Is.Null);
    }

    [Test]
    public void AFactSayingTheToolWritesNoPagesYieldsNothing() {
        string result = """{"ok":true,"data":{"path":"/blog/hello","revisionHash":"abc"},"error":null}""";

        Assert.That(PageWrites.Written(changesPages: false, result), Is.Null);
    }

    [Test]
    public void ARefusedWriteYieldsNothing() {
        const string result = """{"ok":false,"data":null,"error":"Write refused"}""";

        Assert.That(PageWrites.Written("write_page", result), Is.Null);
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

        Assert.That(PageWrites.Written("write_page", result), Is.Null);
    }

    [Test]
    public void ThePathIsReturnedInItsCanonicalSpelling() {
        const string result = """{"ok":true,"data":{"path":"/Blog//Hello"},"error":null}""";

        Assert.That(PageWrites.Written("edit_page", result)?.Key, Is.EqualTo("/blog/hello"));
    }
}
