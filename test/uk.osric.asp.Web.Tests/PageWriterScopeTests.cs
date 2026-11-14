// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using CoreDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using CoreSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;
// The page's own diagnostic is the one these tests read; the compiler's is only the input.
using PageDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Sdk.PageDiagnosticSeverity;

/// <summary>The capability a page saves itself through.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageWriterScopeTests {

    [Test]
    public async Task ASignedInVisitorSavesItsOwnSourceAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("<% ctx.Write(\"Hello.\"); %>");

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(result.Hash, Is.EqualTo(PageHarness.Stored(storage, "/self").Hash));
        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Write(\"Hello.\"); %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task TheSaveIsBoundToThePagesOwnKeyAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        await Scope(pages, SignedIn(), path: "/blog/self").SaveAsync("Hello.");

        Assert.That(PageHarness.Stored(storage, "/blog/self").Template, Is.EqualTo("Hello."));
        Assert.That(pages.Revisions.Last?.Path, Is.EqualTo(PageHarness.Key("/blog/self")));
    }

    [Test]
    public async Task AnAnonymousSaveIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());

        UserFacingException? refused = await Assert.ThrowsAsync<UserFacingException>(
            () => Scope(pages, user: null).SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("Sign in"));
    }

    [Test]
    public async Task AnAnonymousSaveStoresNothingAndCompilesNothingAsync() {
        using PageHarness pages = new(new FakePageStore());

        await Assert.ThrowsAsync<UserFacingException>(() => Scope(pages, user: null).SaveAsync("Hello."));

        Assert.That(pages.Revisions.SaveCount, Is.Zero);
        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task ASaveOnHeadIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scope(pages, SignedIn(), writable: false).SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("HEAD"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task AnAbandonedRenderCannotSaveAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWriterScope writer = Scope(pages, SignedIn());

        writer.Abandon();

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("abandoned"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task ASaveAfterTheDeadlineIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWriterScope writer = Scope(pages, SignedIn());
        using CancellationTokenSource deadline = new();
        writer.AttachDeadline(deadline.Token);
        await deadline.CancelAsync();

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("out of time"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task ASecondSaveInOneRenderIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWriterScope writer = Scope(pages, SignedIn());
        await writer.SaveAsync("First.");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.SaveAsync("Second."));

        Assert.That(refused?.Message, Does.Contain("already saved"));
        Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AWarningIsAnsweredAndThePageIsStillStoredAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("<% int unused = 1; %>");

        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Not.Null.And.Not.Empty, "a warning never stops a page");
    }

    [Test]
    public async Task AnErrorIsAnsweredWithItsPositionAndStoredWithoutAnImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("<% int value = ; %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(error.Line, Is.EqualTo(1));
        Assert.That(error.Column, Is.GreaterThan(0));
        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% int value = ; %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Null, "an error is stored and reported");
    }

    [Test]
    public async Task ADiagnosticCarriesItsCodeAndItsTextApartAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("before<% if (true) {");

        PageDiagnostic error = result.Diagnostics.Single();
        Assert.That(error.Code, Is.EqualTo("TEMPLATE001"));
        Assert.That(error.Message, Is.EqualTo("unterminated code block; the '<%' has no matching '%>'."));
        Assert.That(error.Line, Is.EqualTo(1));
        Assert.That(error.Column, Is.EqualTo(7));
    }

    [Test]
    public async Task ADiagnosticWithNoPositionHasNoLineOrColumnAsync() {
        using PageHarness pages = new(new FakePageStore(), new PositionlessCompiler());

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("<% ctx.Write(\"Hello.\"); %>");

        PageDiagnostic error = result.Diagnostics.Single();
        Assert.That(error.Line, Is.Null);
        Assert.That(error.Column, Is.Null);
        Assert.That(error.Code, Is.EqualTo("PAGE003"));
    }

    [Test]
    public async Task TheDiagnosticsAreOrderedForDisplayAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageSaveResult result = await Scope(pages, SignedIn()).SaveAsync("<% int first = ; %>\n<p>fine</p>\n<% int second = ; %>");

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Line), Is.Ordered);
    }

    [Test]
    public async Task ACompilerFaultStoresTheSourceAndThrowsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scope(pages, SignedIn()).SaveAsync("<% ctx.Write(\"Hello.\"); %>"));

        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Write(\"Hello.\"); %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image, Is.Null);
    }

    [Test]
    public async Task TheSavedRevisionIsWhatTheNextRequestServesAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/self", "<h1>Before</h1>");

        PageWriterScope writer = Scope(pages, SignedIn());
        await writer.SaveAsync("<h1>After</h1>");

        // The running image is the one loaded before the save; the store holds the new revision.
        (RenderResult result, string output) = await pages.RenderAsync("/self");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("After"));
    }

    [Test]
    public async Task APageSavesItselfDuringARenderAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/saving", """
            <% PageSaveResult saved = await ctx.Capabilities.GetRequired<IPageWriter>().SaveAsync("<h1>Saved</h1>"); %>
            <h1>Saved</h1><p id="hash"><%= saved.Hash %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/saving", user: SignedIn());

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("<h1>Saved</h1>"));
        Assert.That(PageHarness.Stored(storage, "/saving").Template, Is.EqualTo("<h1>Saved</h1>"));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1), "the saved template has no code, so it is stored without compiling");
    }

    private static PageUser SignedIn() => new("owner");

    private static PageWriterScope Scope(
        PageHarness pages,
        PageUser? user,
        bool writable = true,
        string path = "/self") =>
        new(PageHarness.Key(path), pages.Saver, user, writable);

    /// <summary>Reports one diagnostic the way a compile timeout does: with no position at all.</summary>
    private sealed class PositionlessCompiler : IPageCompiler {

        public string RuntimeId => "test:positionless";

        public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) =>
            new(null, null, [
                new CoreDiagnostic(CoreSeverity.Error, null,
                    "PAGE003: the page waited longer than 10 s for a compile slot."),
            ]);

        public CompileResult CompileLibrary(GeneratedSource source, CancellationToken cancellationToken = default) =>
            Compile(source, cancellationToken);

        public LoadedPage Load(byte[] image, byte[]? symbols = null) =>
            throw new NotSupportedException("this compiler never produces an image");
    }
}
