// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageImageTests {

    private static readonly PageImage s_compilation = new(
        TemplateFingerprint.Of("Hello."),
        "3:1:build",
        [1, 2, 3],
        [4, 5, 6],
        []);

    [Test]
    public void IsCurrentWhenTheContentAndBuildMatch() {
        Assert.That(s_compilation.IsCurrent(TemplateFingerprint.Of("Hello."), "3:1:build"), Is.True);
    }

    [Test]
    public void IsNotCurrentWhenTheContentChanged() {
        Assert.That(s_compilation.IsCurrent(TemplateFingerprint.Of("Changed."), "3:1:build"), Is.False);
    }

    [Test]
    public void IsNotCurrentWhenTheBuildChanged() {
        Assert.That(s_compilation.IsCurrent(TemplateFingerprint.Of("Hello."), "4:1:build"), Is.False);
    }

    [Test]
    public void AnImageIsNeverStoredWithErrors() {
        using PageHarness pages = new(new FakePageStore());

        PageImage failure = pages.Images.Build(
            PagePath.Normalize("/broken"), "<% int value = ; %>", PageMetrics.ReasonSave, CancellationToken.None);
        PageImage warned = pages.Images.Build(
            PagePath.Normalize("/loud"), "<% int unused = 1; %>", PageMetrics.ReasonSave, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(failure.Bytes, Is.Null);
            Assert.That(failure.Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error), Is.True);
            Assert.That(warned.Bytes, Is.Not.Null);
            Assert.That(warned.Diagnostics.Select(diagnostic => diagnostic.Severity),
                Is.All.EqualTo(PageDiagnosticSeverity.Warning));
        });
    }

    [Test]
    public void IsNotCurrentWhenAnExecutableImageHasNoSymbols() {
        PageImage withoutSymbols = s_compilation with { Symbols = null };

        Assert.That(withoutSymbols.IsCurrent(TemplateFingerprint.Of("Hello."), "3:1:build"), Is.False);
    }
}
