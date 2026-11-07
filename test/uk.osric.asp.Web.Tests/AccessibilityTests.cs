// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Collections.Concurrent;

/// <summary>The accessibility rules every document the shell renders has to keep.</summary>
/// <remarks>
/// The rules are applied to the markup the endpoints actually write, read by
/// <see cref="MarkupReader"/>. What a string cannot show - the accessibility tree, tab order, colour
/// contrast, reflow - stays a manual pass and is recorded in the plan.
/// </remarks>
[TestFixture]
public sealed class AccessibilityTests {

    private static readonly ConcurrentDictionary<string, Task<ShellView>> Rendered = new();

    private static readonly Dictionary<string, string> ImplicitRoles = new(StringComparer.Ordinal) {
        ["a"] = "link",
        ["article"] = "article",
        ["aside"] = "complementary",
        ["button"] = "button",
        ["footer"] = "contentinfo",
        ["form"] = "form",
        ["h1"] = "heading",
        ["h2"] = "heading",
        ["h3"] = "heading",
        ["h4"] = "heading",
        ["h5"] = "heading",
        ["h6"] = "heading",
        ["header"] = "banner",
        ["hr"] = "separator",
        ["img"] = "img",
        ["li"] = "listitem",
        ["main"] = "main",
        ["nav"] = "navigation",
        ["ol"] = "list",
        ["p"] = "paragraph",
        ["section"] = "region",
        ["table"] = "table",
        ["tbody"] = "rowgroup",
        ["td"] = "cell",
        ["th"] = "columnheader",
        ["thead"] = "rowgroup",
        ["tr"] = "row",
        ["ul"] = "list",
    };

    [Test]
    public async Task EachDocumentHasOneHtmlHeadBodyMainAndH1Async() {
        await ForEachViewAsync((view, document) => {
            MarkupElement html = document.Elements("html").Single();
            Assert.That(html.Attribute("lang"), Is.Not.Null.And.Not.Empty, $"{view.Name}: html has no lang");
            Assert.That(document.Elements("head"), Has.Count.EqualTo(1), $"{view.Name}: head");
            Assert.That(document.Elements("body"), Has.Count.EqualTo(1), $"{view.Name}: body");
            Assert.That(document.Elements("main"), Has.Count.EqualTo(1), $"{view.Name}: main");
            Assert.That(document.Elements("h1"), Has.Count.EqualTo(1), $"{view.Name}: h1");
        });
    }

    [Test]
    public async Task HeadingsNeverSkipALevelAsync() {
        await ForEachViewAsync((view, document) => {
            int previous = 0;
            foreach (MarkupElement heading in document.Elements("h1")
                .Concat(document.Elements("h2"))
                .Concat(document.Elements("h3"))
                .Concat(document.Elements("h4"))
                .Concat(document.Elements("h5"))
                .Concat(document.Elements("h6"))
                .OrderBy(heading => DocumentOrder(document, heading))) {

                int level = heading.Name[1] - '0';
                Assert.That(level, Is.LessThanOrEqualTo(previous + 1), $"{view.Name}: {heading.Name} follows h{previous}");
                previous = level;
            }
        });
    }

    [Test]
    public async Task EveryNavigationLandmarkIsNamedAndNoTwoShareANameAsync() {
        await ForEachViewAsync((view, document) => {
            string?[] names = [.. document.Elements("nav").Select(nav => NameOf(document, nav))];

            Assert.That(names.Where(string.IsNullOrEmpty), Is.Empty, $"{view.Name}: an unnamed nav");
            Assert.That(names.Distinct(StringComparer.Ordinal), Is.EquivalentTo(names), $"{view.Name}: two navs share a name");
        });
    }

    [Test]
    public async Task EveryTableHasAHeadAndEveryHeaderSaysWhatItCoversAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement table in document.Elements("table")) {
                Assert.That(table.Descendants().Any(element => element.Name == "thead"), Is.True,
                    $"{view.Name}: a table without a head row");
            }

            foreach (MarkupElement header in document.Elements("th")) {
                Assert.That(header.Attribute("scope"), Is.AnyOf("col", "row", "colgroup", "rowgroup"),
                    $"{view.Name}: a th without a scope");
            }
        });
    }

    [Test]
    public async Task EveryFormHasASubmitControlAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement form in document.Elements("form")) {
                bool hasSubmit = form.Descendants().Any(control => control.Name switch {
                    "button" => control.Attribute("type") is null or "submit",
                    "input" => control.Attribute("type") is "submit" or "image",
                    _ => false,
                });

                Assert.That(hasSubmit, Is.True, $"{view.Name}: a form with no way to submit it");
            }
        });
    }

    [Test]
    public async Task EveryControlIsLabelledAsync() {
        await ForEachViewAsync((view, document) => {
            HashSet<string> labelled = new(
                document.Elements("label").Select(label => label.Attribute("for")).OfType<string>(),
                StringComparer.Ordinal);

            foreach (MarkupElement control in document.Elements("input")
                .Concat(document.Elements("select"))
                .Concat(document.Elements("textarea"))
                .Where(control => control.Name != "input" || control.Attribute("type") != "hidden")) {

                bool isLabelled = (control.Attribute("id") is { } id && labelled.Contains(id))
                    || control.IsInside("label")
                    || !string.IsNullOrEmpty(NameOf(document, control));

                Assert.That(isLabelled, Is.True,
                    $"{view.Name}: {control.Name} '{control.Attribute("name")}' has no label");
            }
        });
    }

    [Test]
    public async Task FormFieldsUseTheLabelAndControlColumnsAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement form in document.Elements("form").Where(form => form.HasClass("form"))) {
                foreach (MarkupElement control in form.Descendants().Where(IsDataControl)) {
                    bool isTextarea = control.Name == "textarea";
                    Assert.That(
                        isTextarea ? control.IsInside("fieldset") : control.IsInside("dd"),
                        Is.True,
                        $"{view.Name}: {control.Name} '{control.Attribute("name")}' is not in {(isTextarea ? "a fieldset" : "a dd")}");
                }

                foreach (MarkupElement label in form.Descendants().Where(element => element.Name == "label")) {
                    Assert.That(label.IsInside("dt") || label.IsInside("legend"), Is.True,
                        $"{view.Name}: a form label is outside a dt or a legend");
                }
            }
        });
    }

    private static bool IsDataControl(MarkupElement element) =>
        (element.Name == "input" && element.Attribute("type") != "hidden")
        || element.Name is "select" or "textarea";

    [Test]
    public async Task NoRoleRepeatsTheElementsOwnRoleAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement element in document.Root.Descendants()) {
                if (element.Attribute("role") is not { } role || !ImplicitRoles.TryGetValue(element.Name, out string? implicitRole)) {
                    continue;
                }

                Assert.That(role != implicitRole || KeepsItsSemanticsDespiteTheSheet(element), Is.True,
                    $"{view.Name}: {element.Name} repeats its own role '{role}'");
            }
        });
    }

    [Test]
    public async Task NothingHiddenFromAssistiveTechnologyCanTakeFocusAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement hidden in document.Root.Descendants().Where(element => element.Attribute("aria-hidden") == "true")) {
                MarkupElement? focusable = hidden.SelfAndDescendants().FirstOrDefault(IsFocusable);

                Assert.That(focusable, Is.Null, $"{view.Name}: {focusable?.Name} is focusable inside aria-hidden");
            }
        });
    }

    [Test]
    public async Task NoStyleIsWrittenAnywhereAsync() {
        await ForEachViewAsync((view, document) => {
            Assert.That(document.Root.Descendants().Where(element => element.HasAttribute("style")), Is.Empty,
                $"{view.Name}: a style attribute");
            Assert.That(document.Elements("style"), Is.Empty, $"{view.Name}: a style element");
        });
    }

    [Test]
    public async Task EveryImageAndNamedDrawingSaysWhatItShowsAsync() {
        await ForEachViewAsync((view, document) => {
            foreach (MarkupElement image in document.Elements("img")) {
                Assert.That(image.HasAttribute("alt"), Is.True, $"{view.Name}: an img without alt");
            }

            foreach (MarkupElement drawing in document.Elements("svg").Where(svg => svg.Attribute("role") == "img")) {
                Assert.That(
                    drawing.Children.Any(child => child.Name == "title" && !string.IsNullOrWhiteSpace(child.Text)),
                    Is.True,
                    $"{view.Name}: an svg role=img without a title");
            }
        });
    }

    [Test]
    public async Task TheEditorsFieldIsLabelledAsync() {
        MarkupReader editor = MarkupReader.Parse((await ViewAsync("editor")).Html);

        Assert.That(editor.Elements("label").Any(label =>
            label.Attribute("for") == "template" && !string.IsNullOrWhiteSpace(label.Text)), Is.True);
    }

    [Test]
    public async Task EveryListingActionNamesItsPageAsync() {
        MarkupReader listing = MarkupReader.Parse((await ViewAsync("listing")).Html);
        MarkupElement[] lists = [.. listing.Root.Descendants().Where(element => element.HasClass("pages"))];

        Assert.That(lists, Is.Not.Empty);
        foreach (MarkupElement link in lists.SelectMany(list => list.Descendants()).Where(element => element.Name == "a")) {
            bool isThePageItself = link.Parent is { Name: "li" } row && row.Children[0] == link;

            Assert.That(isThePageItself || NameOf(listing, link)?.Contains('/') == true, Is.True,
                $"a listing action that does not name its page: {link.Attribute("href")}");
        }
    }

    [Test]
    public async Task ASignInFailureIsAnnouncedAndDescribesBothFieldsAsync() {
        MarkupReader failed = MarkupReader.Parse((await ViewAsync("login-failed")).Html);
        MarkupElement alert = failed.Root.Descendants().Single(element => element.Attribute("role") == "alert");

        Assert.That(alert.Text.Trim(), Is.Not.Empty);
        foreach (MarkupElement field in failed.Elements("input").Where(field => field.Attribute("aria-describedby") is not null)) {
            Assert.That(field.Attribute("aria-describedby"), Is.EqualTo(alert.Attribute("id")));
            Assert.That(field.Attribute("aria-invalid"), Is.EqualTo("true"));
        }

        Assert.That(failed.Elements("input").Count(field => field.Attribute("aria-describedby") is not null), Is.EqualTo(2));
    }

    [Test]
    public async Task ASourceBlocksCanTakeFocusAsync() {
        string[] names = ["revision"];
        foreach (string name in names) {
            MarkupReader document = MarkupReader.Parse((await ViewAsync(name)).Html);

            Assert.That(document.Root.Descendants()
                .Where(element => element.HasClass("source"))
                .All(element => element.Attribute("tabindex") == "0"), Is.True, $"{name}: a source block a keyboard cannot scroll");
        }
    }

    private static async Task ForEachViewAsync(Action<ShellView, MarkupReader> assert) {
        foreach (string name in ShellViews.Names) {
            ShellView view = await ViewAsync(name);
            assert(view, MarkupReader.Parse(view.Html));
        }
    }

    // Rendered once per view per run: every rule re-renders the same documents otherwise.
    private static Task<ShellView> ViewAsync(string name) => Rendered.GetOrAdd(name, ShellViews.RenderAsync);

    private static string? NameOf(MarkupReader document, MarkupElement element) {
        if (element.Attribute("aria-label") is { Length: > 0 } label) {
            return label;
        }

        string[] references = (element.Attribute("aria-labelledby") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string naming = string.Join(' ', references
            .Select(id => document.Root.Descendants().FirstOrDefault(candidate => candidate.Attribute("id") == id)?.Text.Trim())
            .OfType<string>()
            .Where(text => text.Length > 0));

        return naming.Length > 0 ? naming : null;
    }

    // A list whose markers a sheet removes loses its semantics in some readers, so the role is what
    // keeps them: the one redundancy the rules allow, and only where a rule below takes the markers.
    private static bool KeepsItsSemanticsDespiteTheSheet(MarkupElement element) =>
        element.Name == "ul"
        && element.Attribute("role") == "list"
        && (element.HasClass("diagnostics") || element.HasClass("chart-legend")
            || element.HasClass("pages") || element.HasClass("admin-pages"));

    private static bool IsFocusable(MarkupElement element) => element.Name switch {
        "a" => element.HasAttribute("href"),
        "button" => true,
        "select" => true,
        "textarea" => true,
        "input" => element.Attribute("type") != "hidden",
        _ => element.Attribute("tabindex") is { } tabindex && tabindex != "-1",
    };

    private static int DocumentOrder(MarkupReader document, MarkupElement element) {
        int index = 0;
        foreach (MarkupElement candidate in document.Root.Descendants()) {
            if (ReferenceEquals(candidate, element)) {
                return index;
            }

            index++;
        }

        return -1;
    }
}
