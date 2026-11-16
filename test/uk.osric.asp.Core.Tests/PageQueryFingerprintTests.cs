// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

/// <summary>How one render of a page is told from another with the same key.</summary>
[TestFixture]
public sealed class PageQueryFingerprintTests {

    [Test]
    public void TheSameQueryIsTheSameFingerprint() {
        string first = Of(("a", "1"), ("b", "2"));
        string second = Of(("a", "1"), ("b", "2"));

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void AnEmptyQueryHasAnEmptyFingerprint() {
        Assert.That(PageQueryFingerprint.Of([]), Is.Empty);
    }

    [Test]
    public void TwoOrdersOfNamesDoNotCollide() {
        Assert.That(Of(("a", "1"), ("b", "2")), Is.Not.EqualTo(Of(("b", "2"), ("a", "1"))));
    }

    [Test]
    public void ARepeatedNameKeepsTheOrderOfItsValues() {
        Assert.That(Of(("tag", "a"), ("tag", "b")), Is.Not.EqualTo(Of(("tag", "b"), ("tag", "a"))));
    }

    [Test]
    public void AValueIsNotConfusedWithTheSeparators() {
        // Length-prefixed, so no name or value can be read as the boundary between two of them.
        Assert.That(Of(("a", "bc")), Is.Not.EqualTo(Of(("ab", "c"))));
        Assert.That(Of(("a", "b;c")), Is.Not.EqualTo(Of(("a", "b"), ("c", ""))));
    }

    [Test]
    public void ANullQueryIsRefused() {
        Assert.Throws<ArgumentNullException>(() => PageQueryFingerprint.Of(null!));
    }

    private static string Of(params (string Name, string Value)[] query) {
        Dictionary<string, List<string>> grouped = [with(StringComparer.Ordinal)];
        List<KeyValuePair<string, IReadOnlyList<string>>> ordered = [];

        foreach ((string name, string value) in query) {
            if (!grouped.TryGetValue(name, out List<string>? values)) {
                values = [];
                grouped.Add(name, values);
                ordered.Add(new(name, values));
            }

            values.Add(value);
        }

        return PageQueryFingerprint.Of(ordered);
    }
}
