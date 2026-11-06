// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PagePathPolicyTests {

    [Test]
    public void NormalizesBeforeCheckingReservedAndProtectedPaths() {
        PagePathPolicy paths = CreatePaths();

        Assert.That(paths.Validate("/BLOG/../assistant/1", write: false).Error, Does.Contain("protected"));
        Assert.That(paths.Validate("/editor.js", write: false).Error, Does.Contain("protected"));
    }

    [Test]
    public void RefusesUnaddressableKeys() {
        Assert.That(CreatePaths().Validate("/page?query", write: false).Error, Does.Contain("addressable"));
    }

    [Test]
    public void WritablePrefixAllowsOnlyItsPathAndDescendants() {
        PagePathPolicy paths = CreatePaths("/blog");

        Assert.That(paths.Validate("/blog", write: true).IsSuccess, Is.True);
        Assert.That(paths.Validate("/blog/post", write: true).IsSuccess, Is.True);
        Assert.That(paths.Validate("/blogger/post", write: true).Error, Does.Contain("WritablePathPrefix"));
        Assert.That(paths.Validate("/public", write: true).IsSuccess, Is.False);
        Assert.That(paths.Validate("/public", write: false).IsSuccess, Is.True);
    }

    private static PagePathPolicy CreatePaths(string writablePrefix = "") => new(
        new ReservedPaths(ReservedPaths.Default),
        new AssistantOptions { ProtectedPaths = ["/editor.js"], WritablePathPrefix = writablePrefix });
}
