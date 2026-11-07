// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;
using NUnit.Framework;

/// <summary>The import page's drop zone, which is an enhancement over a working file input.</summary>
[TestFixture]
internal sealed class ArchiveBrowserTests : BrowserTest {

    [Test]
    public async Task ADroppedFileFillsTheUploadInputAsync() {
        await Page.GotoAsync("admin/import");

        // A drop is a DataTransfer the page's own handler moves into the input, so what is asserted
        // is the state the form will post, not that some listener fired.
        await Page.EvaluateAsync("""
            () => {
                const file = new File([new Uint8Array([1, 2, 3, 4])], 'pages.sqlite',
                    { type: 'application/vnd.sqlite3' });
                const transfer = new DataTransfer();
                transfer.items.add(file);
                document.getElementById('dropzone').dispatchEvent(
                    new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true }));
            }
            """);

        Assert.That(await Page.EvaluateAsync<int>("document.getElementById('archive').files.length"), Is.EqualTo(1));
        Assert.That(await Page.EvaluateAsync<string>("document.getElementById('archive').files[0].name"),
            Is.EqualTo("pages.sqlite"));
        await Assertions.Expect(Page.Locator("#drop-hint")).ToHaveTextAsync("pages.sqlite");
    }
}
