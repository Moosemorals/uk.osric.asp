// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Documentation;
using uk.osric.asp.Web.Pages;

/// <summary>The help page's links into the reference stay real as the documentation moves.</summary>
[TestFixture]
public sealed class HelpReferenceLinksTests {

    [Test]
    public async Task EveryAnchorTheHelpPageLinksToExists() {
        string source = TestOptions.SeedSource(PagePath.Normalize("/help"))
            ?? throw new InvalidOperationException("The help seed is missing.");
        ReferenceLibrary library = TestReference.Library;
        List<string> missing = [];

        foreach ((string slug, string anchor) in Links(source)) {
            if (!library.HasAnchor(slug, anchor)) {
                missing.Add($"/admin/reference/{slug}#{anchor}");
            }
        }

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public async Task TheHelpPageLinksTheReferenceAndTheContract() {
        string source = TestOptions.SeedSource(PagePath.Normalize("/help"))
            ?? throw new InvalidOperationException("The help seed is missing.");

        Assert.Multiple(() => {
            Assert.That(source, Does.Contain("href=\"/admin/reference\""), "the index");
            Assert.That(source, Does.Contain("href=\"/admin/reference/sdk\""), "the contract");
        });
    }

    private static List<(string Slug, string Anchor)> Links(string source) {
        const string prefix = "/admin/reference/";
        List<(string Slug, string Anchor)> links = [];
        int index = 0;
        while ((index = source.IndexOf(prefix, index, StringComparison.Ordinal)) >= 0) {
            int start = index + prefix.Length;
            int end = source.IndexOf('"', start);
            if (end < 0) {
                break;
            }

            string target = source[start..end];
            int hash = target.IndexOf('#', StringComparison.Ordinal);
            if (hash > 0) {
                links.Add((target[..hash], target[(hash + 1)..]));
            }

            index = end;
        }

        return links;
    }
}
