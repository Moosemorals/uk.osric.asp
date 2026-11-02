// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class SvgTextTests {

    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("plain", "plain")]
    [TestCase("<b>", "&lt;b&gt;")]
    [TestCase("a & b", "a &amp; b")]
    [TestCase("\"quoted\"", "&quot;quoted&quot;")]
    public void TextIsEncodedForMarkup(string? value, string expected) {
        Assert.That(SvgText.Escape(value), Is.EqualTo(expected));
    }
}
