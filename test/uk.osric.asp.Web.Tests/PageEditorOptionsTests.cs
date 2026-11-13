// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageEditorOptionsTests {

    [Test]
    public void TheEditorIsPagesWhenNothingIsConfigured() {
        PageEditorOptions options = PageEditorOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.That(options.Key, Is.EqualTo("/pages"));
    }

    [Test]
    public void AConfiguredKeyIsCanonicalised() {
        PageEditorOptions options = PageEditorOptions.FromConfiguration(
            Configuration(new() { ["Pages:Editor:Key"] = "/Desk//Edit" }));

        Assert.That(options.Key, Is.EqualTo("/desk/edit"));
    }

    [Test]
    public void AKeyNoRequestCouldReachFallsBackToTheDefault() {
        PageEditorOptions options = PageEditorOptions.FromConfiguration(
            Configuration(new() { ["Pages:Editor:Key"] = "/pages?edit=/about" }));

        Assert.That(options.Key, Is.EqualTo(PageEditorOptions.DefaultKey));
    }

    [Test]
    public void ABlankKeyFallsBackToTheDefault() {
        PageEditorOptions options = PageEditorOptions.FromConfiguration(
            Configuration(new() { ["Pages:Editor:Key"] = "  " }));

        Assert.That(options.Key, Is.EqualTo(PageEditorOptions.DefaultKey));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
