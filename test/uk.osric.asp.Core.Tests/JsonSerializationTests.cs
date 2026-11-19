// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Text;

[TestFixture]
public sealed class JsonSerializationTests {

    [Test]
    public void MarkupAndNonAsciiAreWrittenAsWritten() {
        string json = JsonSerializer.Serialize(new { text = "<p class='x'>&amp; + café</p>" }, JsonSerialization.Options);

        Assert.That(json, Is.EqualTo("""{"text":"<p class='x'>&amp; + café</p>"}"""));
    }

    [Test]
    public void QuotesBackslashesAndControlCharactersAreStillEscaped() {
        string json = JsonSerializer.Serialize(new { text = "a\"b\\c\nd\te" }, JsonSerialization.Options);

        Assert.That(json, Is.EqualTo("""{"text":"a\"b\\c\nd\te"}"""));
    }
}
