// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ChatResponseTests {

    [Test]
    public void ContentAloneIsContent() {
        ChatResponse response = new("Hello.", "", [], null, null);

        Assert.That(response.HasContent, Is.True);
    }

    [Test]
    public void ReasoningAloneIsContent() {
        ChatResponse response = new("", "Thinking it over.", [], null, null);

        Assert.That(response.HasContent, Is.True);
    }

    [Test]
    public void AToolCallAloneIsContent() {
        ChatResponse response = new("", "", [new ToolCall(0, "id", "read_file", "{}")], null, null);

        Assert.That(response.HasContent, Is.True);
    }

    [Test]
    public void NothingAtAllIsNotContent() {
        ChatResponse response = new("", "", [], null, null);

        Assert.That(response.HasContent, Is.False);
    }
}
