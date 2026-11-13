// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// The editor's own browser behaviour: the line gutter, the caret across a save and a form that
/// works without the module. None of it is visible to the suites that see only markup.
/// </summary>
[TestFixture]
internal sealed class EditorBrowserTests : BrowserTest {

    [Test]
    public async Task ATypedLineAddsItsNumberAndADeletedOneTakesItAwayAsync() {
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(UniquePath("gutter"))}");
        await Assertions.Expect(Page.Locator("#gutter > span")).ToHaveCountAsync(1);

        await Page.Locator("#template").FillAsync("one\ntwo\nthree");
        await Assertions.Expect(Page.Locator("#gutter > span")).ToHaveCountAsync(3);

        await Page.Locator("#template").FillAsync("one\ntwo");
        await Assertions.Expect(Page.Locator("#gutter > span")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task TheNumbersStayRightAlignedWhenTheColumnWidensAsync() {
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(UniquePath("aligned-numbers"))}");
        await Page.Locator("#template").FillAsync(string.Join('\n', Enumerable.Range(1, 10).Select(line => $"line {line}")));

        // A range around the text, not the span's box: every span is as wide as the column, so only
        // the glyphs' edges say whether the numbers are right-aligned.
        double[] edges = await Page.EvaluateAsync<double[]>("""
            () => {
                const gutter = document.getElementById('gutter');
                const text = element => {
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const box = range.getBoundingClientRect();
                    return [box.right, box.top];
                };
                return [...text(gutter.children[8]), ...text(gutter.children[9])];
            }
            """);

        Assert.Multiple(() => {
            Assert.That(edges[0], Is.EqualTo(edges[2]).Within(1), "9 and 10 do not end at the same edge");
            Assert.That(edges[3], Is.GreaterThan(edges[1]), "10 is not below 9");
        });
    }

    [Test]
    public async Task EveryNumberSitsOnTheRowItCountsAsync() {
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(UniquePath("aligned-rows"))}");
        await Page.Locator("#template").FillAsync(
            string.Join('\n', Enumerable.Range(1, 50).Select(line => $"line {line}")));
        // A row well down the page, so the numbers have to follow the text rather than sit still.
        await Page.EvaluateAsync("""
            () => { document.getElementById('template').scrollTop = 300; }
            """);
        await Page.WaitForFunctionAsync("""
            () => {
                const gutter = document.getElementById('gutter');
                return Math.abs(gutter.scrollTop - document.getElementById('template').scrollTop) <= 1;
            }
            """);

        double[] rows = await Page.EvaluateAsync<double[]>("""
            () => {
                const gutter = document.getElementById('gutter');
                const textarea = document.getElementById('template');
                const top = element => element.getBoundingClientRect().top;
                return [
                    top(gutter.children[0]),
                    top(gutter.children[1]) - top(gutter.children[0]),
                    top(gutter.children[39]) - top(gutter.children[0]),
                    top(textarea) + parseFloat(getComputedStyle(textarea).paddingTop) - textarea.scrollTop,
                    parseFloat(getComputedStyle(textarea).lineHeight),
                ];
            }
            """);

        Assert.Multiple(() => {
            Assert.That(rows[0], Is.EqualTo(rows[3]).Within(1), "the first number has not followed the text's first row");
            Assert.That(rows[1], Is.EqualTo(rows[4]).Within(1), "the numbers do not step by the text's line height");
            Assert.That(rows[2], Is.EqualTo(39 * rows[1]).Within(1), "the fortieth number has not kept the rhythm the rows set");
        });
    }

    [Test]
    public async Task ClickingADiagnosticRevealsItsRowAsync() {
        string path = UniquePath("diagnostic-line");
        string[] lines = [.. Enumerable.Range(1, 50).Select(line => $"<p>line {line}</p>")];
        lines[39] = "<p><%= this is not C# %></p>";

        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(path)}");
        await Page.Locator("#template").FillAsync(string.Join('\n', lines));
        await Page.ClickAsync("button[name=cmd][value=save]");
        // The one broken line can raise more than one diagnostic, so the first link to it is enough.
        ILocator diagnostic = Page.Locator(".diagnostics a[href='#L40']").First;
        await Assertions.Expect(diagnostic).ToBeVisibleAsync();
        // A short box on the reloaded page, so the row to reveal is far enough down to need scrolling.
        await Page.EvaluateAsync("""
            () => { document.getElementById('template').style.height = '200px'; }
            """);
        await diagnostic.ClickAsync();
        // A fragment is a same-document navigation, so the hash is polled rather than awaited as a load.
        await Page.WaitForFunctionAsync("() => location.hash === '#L40'");
        await Assertions.Expect(Page.Locator("#L40:target")).ToHaveCountAsync(1);
        await Page.WaitForFunctionAsync("() => document.getElementById('template').scrollTop > 0");
        double[] scrolled = await Page.EvaluateAsync<double[]>("""
            () => {
                const gutter = document.getElementById('gutter');
                const textarea = document.getElementById('template');
                return [
                    textarea.scrollTop,
                    gutter.children[1].getBoundingClientRect().top - gutter.children[0].getBoundingClientRect().top,
                ];
            }
            """);

        Assert.That(scrolled[0], Is.EqualTo(39 * scrolled[1]).Within(scrolled[1]),
            "the textarea did not stop with the diagnostic's row at the top");
    }

    [Test]
    public async Task ASavePutsTheCaretBackWhereItWasAsync() {
        string path = UniquePath("caret");
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(path)}");
        await Page.Locator("#template").FillAsync("alpha\nbravo\ncharlie");
        await Page.EvaluateAsync("""
            () => {
                const textarea = document.getElementById('template');
                textarea.focus();
                textarea.setSelectionRange(7, 12);
            }
            """);

        await Page.ClickAsync("button[name=cmd][value=save]");
        await ExpectMessageAsync("Saved as revision");
        await Page.WaitForFunctionAsync("() => document.activeElement?.id === 'template'");

        double[] selection = await Page.EvaluateAsync<double[]>("""
            () => {
                const textarea = document.getElementById('template');
                return [textarea.selectionStart, textarea.selectionEnd];
            }
            """);

        Assert.Multiple(() => {
            Assert.That(selection[0], Is.EqualTo(7), "the selection start moved");
            Assert.That(selection[1], Is.EqualTo(12), "the selection end moved");
        });
    }

    [Test]
    public async Task TheGutterIsClippedToTheRowsTheTextareaShowsAsync() {
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(UniquePath("shortened"))}");
        await Page.Locator("#template").FillAsync(
            string.Join(' ', Enumerable.Repeat("a long line that will not wrap", 20)) + "\n"
            + string.Join('\n', Enumerable.Range(2, 60).Select(line => $"line {line}")));
        // The scrollbar takes its height out of the box the numbers are clipped to, and the module
        // hears about it through the observer rather than during the keystroke.
        await Page.WaitForFunctionAsync("""
            () => {
                const textarea = document.getElementById('template');
                const gutter = document.getElementById('gutter');
                return textarea.scrollWidth > textarea.clientWidth
                    && Math.abs(parseFloat(getComputedStyle(gutter).maxHeight) - textarea.clientHeight) <= 1;
            }
            """);

        double[] boxes = await Page.EvaluateAsync<double[]>("""
            () => {
                const textarea = document.getElementById('template');
                const gutter = document.getElementById('gutter');
                return [
                    textarea.scrollWidth > textarea.clientWidth ? 1 : 0,
                    gutter.getBoundingClientRect().height,
                    textarea.clientHeight,
                    parseFloat(getComputedStyle(gutter).maxHeight),
                ];
            }
            """);

        Assert.Multiple(() => {
            Assert.That(boxes[0], Is.EqualTo(1), "the long line did not bring up the horizontal scrollbar");
            Assert.That(boxes[1], Is.EqualTo(boxes[2]).Within(1), "the gutter runs on past the rows the textarea shows");
            Assert.That(boxes[3], Is.EqualTo(boxes[2]).Within(1), "the gutter was not clipped to the textarea's content box");
        });
    }

    [Test]
    public async Task TheEditorStillSavesWithScriptsBlockedAsync() {
        string path = UniquePath("without-scripts");

        await using IBrowserContext withoutScripts = await BrowserSuite.Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = Host.BaseAddress.ToString(),
            StorageStatePath = BrowserSuite.StorageStatePath,
            JavaScriptEnabled = false,
        });
        IPage page = await withoutScripts.NewPageAsync();

        await page.GotoAsync($"pages?edit={Uri.EscapeDataString(path)}");
        // No module runs, so the gutter is the one the server rendered for the empty template.
        await Assertions.Expect(page.Locator("#gutter > span")).ToHaveCountAsync(1);
        await page.FillAsync("#template", "first\nsecond\nthird");
        await page.ClickAsync("button[name=cmd][value=save]");

        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Saved as revision");
        await Assertions.Expect(page.Locator("#gutter > span")).ToHaveCountAsync(3);
    }
}
