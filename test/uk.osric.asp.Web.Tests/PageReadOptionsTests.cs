// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageReadOptionsTests {

    [Test]
    public void TheDefaultsAreTheDocumentedOnes() {
        PageReadOptions options = PageReadOptions.FromConfiguration(Configuration([]));

        Assert.Multiple(() => {
            Assert.That(options.MaxRowsPerList, Is.EqualTo(500));
            Assert.That(options.MaxSourceCharsPerRender, Is.EqualTo(512 * 1024));
        });
    }

    [Test]
    public void AMissingSectionUsesTheDefaults() {
        PageReadOptions options = PageReadOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.That(options, Is.EqualTo(new PageReadOptions()));
    }

    [Test]
    public void ARowLimitBelowOneIsClamped() {
        PageReadOptions options = PageReadOptions.FromConfiguration(
            Configuration(new() { ["Pages:Read:MaxRowsPerList"] = "0" }));

        Assert.That(options.MaxRowsPerList, Is.EqualTo(1));
    }

    [Test]
    public void ARowLimitAboveTheCeilingIsClamped() {
        PageReadOptions options = PageReadOptions.FromConfiguration(
            Configuration(new() { ["Pages:Read:MaxRowsPerList"] = "100000" }));

        Assert.That(options.MaxRowsPerList, Is.EqualTo(PageReadOptions.MaximumRowsPerList));
    }

    [Test]
    public void ASourceBudgetBelowTheFloorIsClamped() {
        PageReadOptions options = PageReadOptions.FromConfiguration(
            Configuration(new() { ["Pages:Read:MaxSourceCharsPerRender"] = "1" }));

        Assert.That(options.MaxSourceCharsPerRender, Is.EqualTo(PageReadOptions.MinimumSourceCharsPerRender));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
