// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageContentTypesConfigurationTests {

    [Test]
    public void TheShippedSectionMapsEveryExtension() {
        PageContentTypes contentTypes = ReadShipped();

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
    public void AMissingFallbackTakesTheSdkDefault() {
        PageContentTypes contentTypes = Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.js"] = "text/javascript",
        });

        Assert.That(contentTypes.Fallback, Is.EqualTo(PageResponse.Default.ContentType));
        Assert.That(contentTypes.FallbackRule.ScriptPolicy, Is.EqualTo(PageScriptPolicy.None));
        Assert.That(contentTypes.ForPath("/notes"), Is.EqualTo(PageResponse.Default.ContentType));
    }

    [Test]
    public void AFallbackObjectCarriesItsPolicy() {
        PageContentTypes contentTypes = Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Fallback:ContentType"] = "text/plain; charset=utf-8",
            ["Pages:ContentTypes:Fallback:ScriptPolicy"] = "self",
        });

        Assert.That(contentTypes.Fallback, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(contentTypes.ScriptsForPath("/notes"), Is.EqualTo(PageScriptPolicy.Self));
    }

    [Test]
    public void ALegacyStringRowGetsTheNoScriptPolicy() {
        PageContentTypes contentTypes = Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.js"] = "text/javascript",
        });

        Assert.That(contentTypes.ForPath("/app.js"), Is.EqualTo("text/javascript"));
        Assert.That(contentTypes.ScriptsForPath("/app.js"), Is.EqualTo(PageScriptPolicy.None));
    }

    [Test]
    public void ARowObjectCarriesItsPolicy() {
        PageContentTypes contentTypes = Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.htmlx:ContentType"] = "text/html; charset=utf-8",
            ["Pages:ContentTypes:Extensions:.htmlx:ScriptPolicy"] = "self",
        });

        Assert.That(contentTypes.ForPath("/page.htmlx"), Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(contentTypes.ScriptsForPath("/page.htmlx"), Is.EqualTo(PageScriptPolicy.Self));
    }

    [Test]
    public void AnUnknownScriptPolicyIsRejected() {
        ArgumentException? failure = Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.html:ContentType"] = "text/html; charset=utf-8",
            ["Pages:ContentTypes:Extensions:.html:ScriptPolicy"] = "inline",
        }));

        Assert.That(failure?.Message, Does.Contain(".html"));
        Assert.That(failure?.Message, Does.Contain("inline"));
    }

    [TestCase("1")]
    [TestCase("2")]
    public void ANumericScriptPolicyIsRejected(string value) {
        Assert.That(
            () => Read(new Dictionary<string, string?> {
                ["Pages:ContentTypes:Extensions:.html:ContentType"] = "text/html; charset=utf-8",
                ["Pages:ContentTypes:Extensions:.html:ScriptPolicy"] = value,
            }),
            Throws.ArgumentException);
    }

    [Test]
    public void ABlankFallbackIsRejected() {
        Assert.That(
            () => Read(new Dictionary<string, string?> { ["Pages:ContentTypes:Fallback"] = "" }),
            Throws.ArgumentException);
    }

    [Test]
    public void AnInvalidRowNamesItsKey() {
        ArgumentException? failure = Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.js"] = "not-a-media-type",
        }));

        Assert.That(failure?.Message, Does.Contain(".js"));
    }

    [Test]
    public void TwoRowsDifferingOnlyInCaseAreRejected() {
        Assert.That(
            () => Read(new Dictionary<string, string?> {
                ["Pages:ContentTypes:Extensions:.js"] = "text/javascript",
                ["Pages:ContentTypes:Extensions:.JS"] = "text/javascript",
            }),
            Throws.ArgumentException);
    }

    [Test]
    public void ARowMappedToTheFallbackBehavesLikeNoRow() {
        PageContentTypes withRow = Read(new Dictionary<string, string?> {
            ["Pages:ContentTypes:Extensions:.html"] = "text/html; charset=utf-8",
        });
        PageContentTypes withoutRow = Read([]);

        Assert.That(withRow.ForPath("/notes"), Is.EqualTo(withoutRow.ForPath("/notes")));
        Assert.That(withRow.ForPath("/notes.html"), Is.EqualTo(withoutRow.ForPath("/notes.html")));
    }

    [Test]
    public void TheReaderLogsTheFallbackThePolicyAndTheRowCount() {
        RecordingLoggerFactory logs = new();

        _ = PageContentTypesConfiguration.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Pages:ContentTypes:Extensions:.js"] = "text/javascript",
            }).Build(),
            logs.CreateLogger("Pages.ContentTypes"));

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Message, Does.Contain("text/html; charset=utf-8"));
        Assert.That(logs.Entries[0].Message, Does.Contain("None"));
        Assert.That(logs.Entries[0].Message, Does.Contain("1"));
    }

    private static PageContentTypes ReadShipped() {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath(), optional: false)
            .Build();

        return Read(configuration);
    }

    private static PageContentTypes Read(Dictionary<string, string?> values) {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return Read(configuration);
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
