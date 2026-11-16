// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class TemplateCommentsTests {

    [TestCase("")]
    [TestCase("<p>No constructs at all.</p>")]
    [TestCase("<% int value = 1; %>text")]
    public void ATemplateWithNoCommentIsReturnedAsItIs(string template) {
        Assert.That(TemplateComments.Remove(template), Is.EqualTo(template));
    }

    [TestCase("<p>One</p><%# a note %><p>Two</p>", "<p>One</p><p>Two</p>")]
    [TestCase("before<%# note %>after", "beforeafter")]
    [TestCase("<%# multi\nline\n%>text", "text")]
    public void ACommentIsRemovedWithItsDelimiters(string template, string expected) {
        Assert.That(TemplateComments.Remove(template), Is.EqualTo(expected));
    }

    [Test]
    public void EveryCommentIsRemoved() {
        Assert.That(TemplateComments.Remove("<%# one %>a<%# two %>b"), Is.EqualTo("ab"));
    }

    [Test]
    public void ACommentMarkerInsideCodeIsNotAComment() {
        const string Template = "<% ctx.Response.Write(\"<%#\"); %>";

        Assert.That(TemplateComments.Remove(Template), Is.EqualTo(Template));
    }

    [Test]
    public void AnUnterminatedCommentLeavesTheTemplateWhole() {
        const string Template = "<p>text</p><%# a note";

        Assert.That(TemplateComments.Remove(Template), Is.EqualTo(Template));
    }
}
