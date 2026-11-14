// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the file the site ships with is one the site could start from.</summary>
[TestFixture]
public sealed class ShippedConfigurationTests {

    [Test]
    public void EveryFactoryReadsTheShippedFile() {
        IConfiguration configuration = ShippedConfiguration.Read();

        Assert.Multiple(() => {
            Assert.That(() => RenderOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => CompileOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageStorageOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageSessionOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageFetchOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageReadOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => uk.osric.asp.Web.Admin.ArchiveOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => uk.osric.asp.Web.Admin.AdminOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageEditorOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => PageLayoutConfiguration.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => uk.osric.asp.Web.Assistant.AssistantOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => uk.osric.asp.Web.Authentication.AuthenticationOptions.FromConfiguration(configuration), Throws.Nothing);
            Assert.That(() => uk.osric.asp.Web.Metrics.MetricsOptions.FromConfiguration(configuration), Throws.Nothing);
        });
    }

    [Test]
    public void TheShippedReservedKeysAreTheOnesTheShellRouteTableDescribes() {
        Assert.That(TestOptions.ReservedKeys(), Is.EquivalentTo(ReservedPaths.Default));
    }

    [Test]
    public void TheShippedEveryExtensionIsMapped() {
        Assert.That(TestOptions.ContentTypes.ExtensionCount, Is.EqualTo(7));
    }

    [Test]
    public void TheShippedLayoutIsTheSeededOne() {
        Assert.That(TestOptions.Layout.Default, Is.EqualTo("/_layout"));
    }

    [Test]
    public void TheShippedEditorIsTheSeededPage() {
        Assert.That(TestOptions.Editor.Key, Is.EqualTo("/pages"));
    }
}
