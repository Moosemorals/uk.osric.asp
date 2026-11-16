// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

/// <summary>The copy-and-change helpers a composed page uses to shape a request.</summary>
[TestFixture]
public sealed class PageRequestQueryTests {

    [Test]
    public void WithQueryReplacesEveryValueOfOneName() {
        PageRequest request = Request(("tag", "a"), ("tag", "b"), ("page", "2"));

        PageRequest changed = request.WithQuery("tag", "c");

        Assert.Multiple(() => {
            Assert.That(changed.QueryValues["tag"], Is.EqualTo(["c"]));
            Assert.That(changed.Query["tag"], Is.EqualTo("c"));
            Assert.That(changed.Query["page"], Is.EqualTo("2"));
        });
    }

    [Test]
    public void WithQueryAddsANameThatWasNotThere() {
        PageRequest changed = Request(("page", "2")).WithQuery("tag", "a");

        Assert.That(changed.QueryValues["tag"], Is.EqualTo(["a"]));
    }

    [Test]
    public void WithQueryKeepsThePositionTheNameHad() {
        PageRequest changed = Request(("first", "1"), ("second", "2"), ("third", "3")).WithQuery("second", "x");

        Assert.That(changed.QueryValues.Keys, Is.EqualTo(["first", "second", "third"]));
    }

    [Test]
    public void WithQueryReplacesTheWholeQueryInTheOrderGiven() {
        PageRequest changed = Request(("a", "1")).WithQuery([new("b", "2"), new("a", "9")]);

        Assert.Multiple(() => {
            Assert.That(changed.QueryValues.Keys, Is.EqualTo(["b", "a"]));
            Assert.That(changed.QueryValues["a"], Is.EqualTo(["9"]));
            Assert.That(changed.Query["b"], Is.EqualTo("2"));
        });
    }

    [Test]
    public void WithAnotherQueryAddsAValueBesideTheOnesANameHas() {
        PageRequest changed = Request(("tag", "a")).WithAnotherQuery("tag", "b");

        Assert.Multiple(() => {
            Assert.That(changed.QueryValues["tag"], Is.EqualTo(["a", "b"]));
            Assert.That(changed.Query["tag"], Is.EqualTo("a"), "the collapsed view keeps the first value");
        });
    }

    [Test]
    public void WithoutQueryDropsOneName() {
        PageRequest changed = Request(("tag", "a"), ("page", "2")).WithoutQuery("tag");

        Assert.That(changed.QueryValues.Keys, Is.EqualTo(["page"]));
        Assert.That(changed.Query.ContainsKey("tag"), Is.False);
    }

    [Test]
    public void WithoutQueryDropsTheWholeQuery() {
        PageRequest changed = Request(("tag", "a"), ("page", "2")).WithoutQuery();

        Assert.Multiple(() => {
            Assert.That(changed.Query, Is.Empty);
            Assert.That(changed.QueryValues, Is.Empty);
        });
    }

    [Test]
    public void TheRestOfTheRequestIsCarriedAcross() {
        PageRequest request = Request(("tag", "a")) with {
            Method = "POST",
            Form = new Dictionary<string, string> { ["field"] = "value" },
            Headers = new Dictionary<string, string> { ["X-Test"] = "1" },
            Cookies = new Dictionary<string, string> { ["c"] = "v" },
        };

        PageRequest changed = request.WithQuery("tag", "b");

        Assert.Multiple(() => {
            Assert.That(changed.Method, Is.EqualTo("POST"));
            Assert.That(changed.Path, Is.EqualTo("/page"));
            Assert.That(changed.Form["field"], Is.EqualTo("value"));
            Assert.That(changed.Headers["X-Test"], Is.EqualTo("1"));
            Assert.That(changed.Cookies["c"], Is.EqualTo("v"));
        });
    }

    [Test]
    public void ANullRequestIsRefused() {
        PageRequest nothing = null!;

        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => nothing.WithQuery("a", "b"));
            Assert.Throws<ArgumentNullException>(() => nothing.WithAnotherQuery("a", "b"));
            Assert.Throws<ArgumentNullException>(() => nothing.WithoutQuery("a"));
            Assert.Throws<ArgumentNullException>(() => nothing.WithoutQuery());
            Assert.Throws<ArgumentNullException>(() => nothing.WithQuery([]));
        });
    }

    [Test]
    public void ABlankNameIsRefused() {
        PageRequest request = Request();

        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => request.WithQuery("", "value"));
            Assert.Throws<ArgumentException>(() => request.WithAnotherQuery(" ", "value"));
            Assert.Throws<ArgumentException>(() => request.WithoutQuery(""));
        });
    }

    [Test]
    public void ANullValueIsRefused() {
        PageRequest request = Request();

        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => request.WithQuery("a", null!));
            Assert.Throws<ArgumentNullException>(() => request.WithAnotherQuery("a", null!));
        });
    }

    private static PageRequest Request(params (string Name, string Value)[] query) {
        Dictionary<string, string> collapsed = [with(StringComparer.Ordinal)];
        Dictionary<string, List<string>> grouped = [with(StringComparer.Ordinal)];
        Dictionary<string, IReadOnlyList<string>> every = [with(StringComparer.Ordinal)];

        foreach ((string name, string value) in query) {
            _ = collapsed.TryAdd(name, value);
            if (!grouped.TryGetValue(name, out List<string>? values)) {
                values = [];
                grouped.Add(name, values);
            }

            values.Add(value);
            every[name] = values;
        }

        return new PageRequest(
            "GET",
            "/page",
            collapsed,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            every,
            new Dictionary<string, IReadOnlyList<string>>());
    }
}
