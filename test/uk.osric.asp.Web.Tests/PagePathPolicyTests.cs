// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PagePathPolicyTests {

    [Test]
    public void ReservedKeysAreReadableAndNeverWritable() {
        PagePathPolicy paths = CreatePaths();
        PagePath login = PagePath.Normalize("/login");

        Assert.Multiple(() => {
            Assert.That(paths.Validate("/login", write: false).IsSuccess, Is.True, "a shell file is readable");
            Assert.That(paths.Validate("/BLOG/../login", write: true).Error,
                Does.Contain("Not permitted").And.Contain("shell file"), "normalised before the check");
            Assert.That(paths.IsReserved(login), Is.True);
            Assert.That(paths.CanWrite(login), Is.False);
            Assert.That(paths.Validate("/edit/anything", write: true).Error, Does.Contain("/edit"),
                "a key beneath a route names the route that owns it");
        });
    }

    [Test]
    public void RefusesUnaddressableKeys() {
        Assert.That(CreatePaths().Validate("/page?query", write: false).Error, Does.Contain("addressable"));
    }

    [Test]
    public void WritablePrefixAllowsOnlyItsPathAndDescendants() {
        PagePathPolicy paths = CreatePaths("/blog");

        Assert.Multiple(() => {
            Assert.That(paths.Validate("/blog", write: true).IsSuccess, Is.True);
            Assert.That(paths.Validate("/blog/post", write: true).IsSuccess, Is.True);
            Assert.That(paths.Validate("/blogger/post", write: true).Error, Does.Contain("WritablePathPrefix"));
            Assert.That(paths.Validate("/public", write: true).IsSuccess, Is.False);
            Assert.That(paths.Validate("/public", write: false).IsSuccess, Is.True);
            Assert.That(paths.CanWrite(PagePath.Normalize("/blog/post")), Is.True);
            Assert.That(paths.CanWrite(PagePath.Normalize("/public")), Is.False,
                "the permission column says what the prefix decides");
        });
    }

    private static PagePathPolicy CreatePaths(string writablePrefix = "") => new(
        new ReservedPaths(ReservedPaths.Default),
        new AssistantOptions { WritablePathPrefix = writablePrefix });
}
