// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageEditorOptionsTests {

    [Test]
    public void TheShippedKeyIsTheSeededEditor() {
        Assert.That(TestOptions.Editor.Key, Is.EqualTo("/pages"));
    }

    [Test]
    public void AConfiguredKeyIsCanonicalised() {
        PageEditorOptions options = PageEditorOptions.FromConfiguration(
            Configuration(new() { ["Pages:Editor:Key"] = "/Desk//Edit" }));

        Assert.That(options.Key, Is.EqualTo("/desk/edit"));
    }

    [Test]
    public void AMissingKeyStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(
            () => PageEditorOptions.FromConfiguration(new ConfigurationBuilder().Build()))!;

        Assert.That(error.Message, Does.Contain("Pages:Editor:Key is not set."));
    }

    [Test]
    public void AKeyNoRequestCouldReachStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(
            () => PageEditorOptions.FromConfiguration(
                Configuration(new() { ["Pages:Editor:Key"] = "/pages?edit=/about" })))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:Editor:Key"));
            Assert.That(error.Message, Does.Contain("not an addressable page key"));
        });
    }

    [Test]
    public void ABlankKeyStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(
            () => PageEditorOptions.FromConfiguration(
                Configuration(new() { ["Pages:Editor:Key"] = "  " })))!;

        Assert.That(error.Message, Does.Contain("Pages:Editor:Key is blank."));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
