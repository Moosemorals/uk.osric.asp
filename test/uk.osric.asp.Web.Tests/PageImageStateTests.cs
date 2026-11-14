// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>The words and the classes a stored image's state is described with.</summary>
[TestFixture]
public sealed class PageImageStateTests {

    [Test]
    public void AnImageWithDiagnosticsSaysCompiledWithWarnings() {
        PageImageState state = PageImageStatus.Describe("1:build", hasImage: true, hasDiagnostics: true);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.CompiledWithWarnings));
            Assert.That(state.Describe(), Is.EqualTo("Compiled with warnings"));
        });
    }

    [Test]
    public void AnImageWithNoDiagnosticsSaysCompiled() {
        PageImageState state = PageImageStatus.Describe("1:build", hasImage: true, hasDiagnostics: false);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.Compiled));
            Assert.That(state.Describe(), Is.EqualTo("Compiled"));
        });
    }

    [Test]
    public void ARecordWithNoImageSaysCompileFailed() {
        PageImageState state = PageImageStatus.Describe("1:build", hasImage: false, hasDiagnostics: true);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.CompileFailed));
            Assert.That(state.Describe(), Is.EqualTo("Compile failed"));
        });
    }

    [Test]
    public void APageWithNoRecordSaysNotCompiled() {
        PageImageState state = PageImageStatus.Describe(null, hasImage: false, hasDiagnostics: false);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.NotCompiled));
            Assert.That(state.Describe(), Is.EqualTo("Not compiled"));
        });
    }

    [Test]
    public void AnImageFromAnotherBuildSaysStale() {
        PageImageState state = PageImageStatus.Describe(
            "1:build", hasImage: true, hasDiagnostics: true, currentRuntimeId: "2:build");

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.Stale));
            Assert.That(state.Describe(), Is.EqualTo("Stale"));
        });
    }

    [Test]
    public void AnImageIsNotStaleWhenNoCurrentBuildIsGiven() {
        PageImageState state = PageImageStatus.Describe(
            "1:build", hasImage: true, hasDiagnostics: false, currentRuntimeId: null);

        Assert.That(state, Is.EqualTo(PageImageState.Compiled), "a retained revision is history, not stale");
    }

    [Test]
    public void ARevisionWithNoCodeSaysStoredAsSource() {
        PageImageState state = PageImageStatus.Describe(
            runtimeId: null, hasImage: false, hasDiagnostics: false, kind: PageKind.Text);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.StoredAsSource));
            Assert.That(state.Describe(), Is.EqualTo("Stored as source"));
        });
    }

    [Test]
    public void ALibrarySaysItIsALibrary() {
        PageImageState state = PageImageStatus.Describe(
            runtimeId: "1:build", hasImage: false, hasDiagnostics: false, kind: PageKind.Library);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.Library));
            Assert.That(state.Describe(), Is.EqualTo("Library"));
            Assert.That(state.Class(), Is.EqualTo("state-library"));
        });
    }

    [Test]
    public void ALibraryWhoseCheckReportedSomethingSaysSo() {
        PageImageState state = PageImageStatus.Describe(
            runtimeId: "1:build", hasImage: false, hasDiagnostics: true, kind: PageKind.Library);

        Assert.Multiple(() => {
            Assert.That(state, Is.EqualTo(PageImageState.LibraryWithIssues));
            Assert.That(state.Describe(), Is.EqualTo("Library with issues"));
        });
    }

    [TestCase("StoredAsSource", "state-source")]
    [TestCase("NotCompiled", "state-not-compiled")]
    [TestCase("Stale", "state-stale")]
    [TestCase("CompileFailed", "state-compile-failed")]
    [TestCase("CompiledWithWarnings", "state-compiled-with-warnings")]
    [TestCase("Compiled", "state-compiled")]
    public void EveryStateHasItsOwnClass(string state, string expected) {
        // An internal type cannot be a public test method's parameter, so the case names the state.
        Assert.That(Enum.Parse<PageImageState>(state).Class(), Is.EqualTo(expected));
    }
}
