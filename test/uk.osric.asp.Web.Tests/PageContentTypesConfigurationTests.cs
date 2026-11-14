// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageContentTypesConfigurationTests {

    [Test]
    public void TheShippedSectionMapsEveryExtension() {
        PageContentTypes contentTypes = TestOptions.ContentTypes;

        Assert.Multiple(() => {
            Assert.That(contentTypes.Fallback, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(contentTypes.FallbackRule.ScriptPolicy, Is.EqualTo(PageScriptPolicy.None));
            Assert.That(contentTypes.ExtensionCount, Is.EqualTo(7));
            Assert.That(contentTypes.ForPath("/page.html"), Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(contentTypes.ScriptsForPath("/page.html"), Is.EqualTo(PageScriptPolicy.None));
            Assert.That(contentTypes.ForPath("/app.js"), Is.EqualTo("text/javascript; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/app.mjs"), Is.EqualTo("text/javascript; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/app.css"), Is.EqualTo("text/css; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/app.json"), Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/app.txt"), Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/shape.svg"), Is.EqualTo("image/svg+xml; charset=utf-8"));
            Assert.That(contentTypes.ForPath("/app"), Is.EqualTo("text/html; charset=utf-8"));
        });
    }

    [Test]
    public void TheRepositoryStillShipsEveryRow() {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath()));

        JsonElement section = document.RootElement.GetProperty("Pages").GetProperty("ContentTypes");
        JsonElement fallback = section.GetProperty("Fallback");

        Assert.That(fallback.GetProperty("ContentType").GetString(), Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(fallback.GetProperty("ScriptPolicy").GetString(), Is.EqualTo("none"));
        Assert.That(
            section.GetProperty("Extensions").EnumerateObject().Select(property => property.Name),
            Is.EquivalentTo([".html", ".js", ".mjs", ".css", ".json", ".svg", ".txt"]));
        Assert.That(
            section.GetProperty("Extensions").GetProperty(".html").GetProperty("ScriptPolicy").GetString(),
            Is.EqualTo("none"));
    }

    [Test]
    public void AMissingFallbackStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => Read(
            ShippedConfiguration.WithoutSection("Pages:ContentTypes:Fallback")))!;

        Assert.That(error.Message, Does.Contain("Pages:ContentTypes:Fallback is not set."));
    }

    [Test]
    public void AFallbackObjectCarriesItsPolicy() {
        PageContentTypes contentTypes = Read(ShippedConfiguration.Read(
            ("Pages:ContentTypes:Fallback:ContentType", "text/plain; charset=utf-8"),
            ("Pages:ContentTypes:Fallback:ScriptPolicy", "self")));

        Assert.Multiple(() => {
            Assert.That(contentTypes.Fallback, Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(contentTypes.ScriptsForPath("/notes"), Is.EqualTo(PageScriptPolicy.Self));
        });
    }

    [Test]
    public void AStringRowGetsTheNoScriptPolicy() {
        PageContentTypes contentTypes = Read(ShippedConfiguration.Read(
            ("Pages:ContentTypes:Extensions:.wasm", "application/wasm")));

        Assert.Multiple(() => {
            Assert.That(contentTypes.ForPath("/app.wasm"), Is.EqualTo("application/wasm"));
            Assert.That(contentTypes.ScriptsForPath("/app.wasm"), Is.EqualTo(PageScriptPolicy.None));
        });
    }

    [Test]
    public void ARowObjectCarriesItsPolicy() {
        PageContentTypes contentTypes = Read(ShippedConfiguration.Read(
            ("Pages:ContentTypes:Extensions:.htmlx:ContentType", "text/html; charset=utf-8"),
            ("Pages:ContentTypes:Extensions:.htmlx:ScriptPolicy", "self")));

        Assert.Multiple(() => {
            Assert.That(contentTypes.ForPath("/page.htmlx"), Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(contentTypes.ScriptsForPath("/page.htmlx"), Is.EqualTo(PageScriptPolicy.Self));
        });
    }

    [Test]
    public void AnUnknownScriptPolicyStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => Read(ShippedConfiguration.Read(
            ("Pages:ContentTypes:Extensions:.html:ContentType", "text/html; charset=utf-8"),
            ("Pages:ContentTypes:Extensions:.html:ScriptPolicy", "inline"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:ContentTypes:Extensions:.html:ScriptPolicy"));
            Assert.That(error.Message, Does.Contain("inline"));
        });
    }

    [TestCase("1")]
    [TestCase("2")]
    public void ANumericScriptPolicyStopsTheSite(string value) {
        Assert.That(
            () => Read(ShippedConfiguration.Read(
                ("Pages:ContentTypes:Extensions:.html:ContentType", "text/html; charset=utf-8"),
                ("Pages:ContentTypes:Extensions:.html:ScriptPolicy", value))),
            Throws.TypeOf<ConfigurationException>());
    }

    [Test]
    public void ABlankFallbackStopsTheSite() {
        Assert.That(
            () => Read(ShippedConfiguration.Read(("Pages:ContentTypes:Fallback", ""))),
            Throws.TypeOf<ConfigurationException>());
    }

    [Test]
    public void AnInvalidRowNamesItsKey() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => Read(
            ShippedConfiguration.Read(("Pages:ContentTypes:Extensions:.weird", "not-a-media-type"))))!;

        Assert.That(error.Message, Does.Contain(".weird"));
    }

    [Test]
    public void ARowMappedToTheFallbackBehavesLikeNoRow() {
        PageContentTypes withRow = Read(ShippedConfiguration.Read(
            ("Pages:ContentTypes:Extensions:.html", "text/html; charset=utf-8")));
        PageContentTypes withoutRow = TestOptions.ContentTypes;

        Assert.Multiple(() => {
            Assert.That(withRow.ForPath("/notes"), Is.EqualTo(withoutRow.ForPath("/notes")));
            Assert.That(withRow.ForPath("/notes.html"), Is.EqualTo(withoutRow.ForPath("/notes.html")));
        });
    }

    [Test]
    public void TheReaderLogsTheFallbackThePolicyAndTheRowCount() {
        RecordingLoggerFactory logs = new();

        _ = PageContentTypesConfiguration.FromConfiguration(
            ShippedConfiguration.Read(("Pages:ContentTypes:Extensions:.wasm", "application/wasm")),
            logs.CreateLogger("Pages.ContentTypes"));

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Message, Does.Contain("text/html; charset=utf-8"));
        Assert.That(logs.Entries[0].Message, Does.Contain("None"));
        Assert.That(logs.Entries[0].Message, Does.Contain("8"));
    }

    private static PageContentTypes Read(IConfiguration configuration) =>
        PageContentTypesConfiguration.FromConfiguration(configuration, NullLogger.Instance);

    private static string AppSettingsPath() => Path.Combine(
        typeof(PageContentTypesConfigurationTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepoRoot")
            .Value!,
        "src", "uk.osric.asp.Web", "appsettings.json");
}
