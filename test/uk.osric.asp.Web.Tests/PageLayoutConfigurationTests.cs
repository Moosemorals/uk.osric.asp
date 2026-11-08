// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Pages;

/// <summary>How <c>Pages:Layout</c> and <c>Pages:Render:MaxLayoutDepth</c> are read.</summary>
[TestFixture]
public sealed class PageLayoutConfigurationTests {

    [TestCase("/_layout", "/_layout")]
    [TestCase("/Layout", "/layout")]
    [TestCase("_layout", "/_layout")]
    [TestCase("/_layout/", "/_layout")]
    [TestCase("/a/../_layout", "/_layout")]
    public void TheDefaultLayoutComesFromConfigurationAndNormalises(string configured, string expected) {
        Assert.That(Configuration(configured).Default, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankDefaultMeansNoLayout(string? configured) {
        Assert.That(Configuration(configured).Default, Is.Null);
    }

    [TestCase(null, 4)]
    [TestCase(1, 1)]
    [TestCase(16, 16)]
    [TestCase(0, 1)]
    [TestCase(-3, 1)]
    [TestCase(400, 16)]
    public void TheLayoutDepthComesFromConfigurationAndIsClamped(int? configured, int expected) {
        RenderOptions options = RenderOptions.FromConfiguration(Settings(
            ("Pages:Render:MaxLayoutDepth", configured?.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        Assert.That(options.MaxLayoutDepth, Is.EqualTo(expected));
    }

    private static PageLayoutConfiguration Configuration(string? layout) =>
        PageLayoutConfiguration.FromConfiguration(Settings(("Pages:Layout", layout)));

    private static IConfiguration Settings(params (string Key, string? Value)[] values) {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
    }
}
