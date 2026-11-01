// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageServiceTests {

    // A fresh fixture instance per test keeps the compile count and the meter belonging to one test.
    private readonly RecordingCompiler _compiler = new(new TemplateCompiler());
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [Test]
    public async Task SaveCompilesOnceAndStoresTheSourceWithTheImageAsync() {
        FakeStorage storage = new();
        PageService pages = CreateService(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(Key("/hello"), "Hello.");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(_compiler.CompileCount, Is.EqualTo(1));

        StoredPage saved = Stored(storage, "/hello");
        Assert.That(saved.Content, Is.EqualTo("Hello."));
        Assert.That(saved.Compilation?.Image, Is.Not.Null.And.Not.Empty);
        Assert.That(saved.Compilation?.ContentHash, Is.EqualTo(ContentFingerprint.Of("Hello.")));
        Assert.That(saved.Compilation?.RuntimeId, Is.EqualTo(_compiler.RuntimeId));
        Assert.That(saved.Compilation?.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task SaveKeepsTheSourceAndStoresDiagnosticsWhenThePageDoesNotCompileAsync() {
        FakeStorage storage = new();
        PageService pages = CreateService(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(Key("/broken"), "<% int value = ; %>");

        Assert.That(diagnostics, Is.Not.Empty);
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS", StringComparison.Ordinal)
                && diagnostic.Severity == PageDiagnosticSeverity.Error));

        StoredPage saved = Stored(storage, "/broken");
        Assert.That(saved.Content, Is.EqualTo("<% int value = ; %>"));
        Assert.That(saved.Compilation?.Image, Is.Null);
        Assert.That(saved.Compilation?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
    }

    [Test]
    public async Task SaveStoresAParseErrorWithoutCallingTheCompilerAsync() {
        FakeStorage storage = new();
        PageService pages = CreateService(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(Key("/unterminated"), "before<% if (true) {");

        Assert.That(diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
        Assert.That(_compiler.CompileCount, Is.Zero, "a template that does not parse never reaches the compiler");

        StoredPage saved = Stored(storage, "/unterminated");
        Assert.That(saved.Content, Is.EqualTo("before<% if (true) {"));
        Assert.That(saved.Compilation?.Image, Is.Null);
        Assert.That(saved.Compilation?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
    }

    [Test]
    public async Task SaveReportsAWarningWithoutFailingThePageAsync() {
        FakeStorage storage = new();
        PageService pages = CreateService(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(Key("/warned"), "<% int unused = 1; %>");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));

        StoredPage saved = Stored(storage, "/warned");
        Assert.That(saved.Compilation?.Image, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task RenderPropagatesACompilerFaultAsync() {
        FakeStorage storage = new();
        storage["/fault"] = new StoredPage(Key("/fault"), "Hello.");
        PageService pages = new(storage, new FaultingCompiler(), _metrics, NullLogger<PageService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(pages, "/fault"));
    }

    [Test]
    public async Task SavePreservesTheSourceWhenTheCompilerFaultsAsync() {
        FakeStorage storage = new();
        PageService pages = new(storage, new FaultingCompiler(), _metrics, NullLogger<PageService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pages.SaveAsync(Key("/fault"), "Hello."));

        StoredPage saved = Stored(storage, "/fault");
        Assert.That(saved.Content, Is.EqualTo("Hello."));
        Assert.That(saved.Compilation, Is.Null);
    }

    [Test]
    public async Task RenderOfACompiledPageDoesNotCompileAsync() {
        FakeStorage storage = new();
        PageService pages = CreateService(storage);
        await pages.SaveAsync(Key("/hello"), "Hello.");
        int compiles = _compiler.CompileCount;

        (RenderResult result, string output) = await RenderAsync(pages, "/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.CompilationWriteCount, Is.Zero);
    }

    [Test]
    public async Task RenderCompilesAPageThatWasStoredWithoutOneAsync() {
        FakeStorage storage = new();
        storage["/seeded"] = new StoredPage(Key("/seeded"), "Seeded.");
        PageService pages = CreateService(storage);

        (RenderResult result, string output) = await RenderAsync(pages, "/seeded");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Seeded."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(1));
        Assert.That(storage.CompilationWriteCount, Is.EqualTo(1));
        Assert.That(Stored(storage, "/seeded").Compilation, Is.Not.Null);
    }

    [Test]
    public async Task RenderRecompilesWhenTheContentChangedBehindTheCacheAsync() {
        FakeStorage storage = new();
        PageCompilation stale = new(ContentFingerprint.Of("Old."), _compiler.RuntimeId, [1, 2, 3], []);
        storage["/edited"] = new StoredPage(Key("/edited"), "New.", stale);
        PageService pages = CreateService(storage);

        (RenderResult result, string output) = await RenderAsync(pages, "/edited");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("New."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(1));
        Assert.That(Stored(storage, "/edited").Compilation?.ContentHash, Is.EqualTo(ContentFingerprint.Of("New.")));
    }

    [Test]
    public async Task RenderRecompilesWhenTheBuildChangedAsync() {
        FakeStorage storage = new();
        PageCompilation previousBuild = new(ContentFingerprint.Of("Hello."), "0:an-old-build", [1, 2, 3], []);
        storage["/hello"] = new StoredPage(Key("/hello"), "Hello.", previousBuild);
        PageService pages = CreateService(storage);

        (RenderResult result, string output) = await RenderAsync(pages, "/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(1));
        Assert.That(Stored(storage, "/hello").Compilation?.RuntimeId, Is.EqualTo(_compiler.RuntimeId));
    }

    [Test]
    public async Task AStoredCompileErrorIsServedWithoutCompilingAgainAsync() {
        FakeStorage storage = new();
        PageCompilation failed = new(ContentFingerprint.Of("<% int value = ; %>"), _compiler.RuntimeId, null, [
            new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: broken"),
        ]);
        storage["/broken"] = new StoredPage(Key("/broken"), "<% int value = ; %>", failed);
        PageService pages = CreateService(storage);

        (RenderResult result, string _) = await RenderAsync(pages, "/broken");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.CompileFailed));
        Assert.That(result.Diagnostics, Is.EqualTo(failed.Diagnostics).AsCollection);
        Assert.That(_compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task MissingPageIsNotFoundAsync() {
        PageService pages = CreateService(new FakeStorage());

        (RenderResult result, string _) = await RenderAsync(pages, "/missing");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.NotFound));
    }

    [Test]
    public async Task RenderKeepsTheNewerPageWhenItChangesWhileCompilingAsync() {
        FakeStorage storage = new();
        storage["/race"] = new StoredPage(Key("/race"), "first");
        storage.ReplaceOnCompilationWrite = (path, _) => new StoredPage(path, "second");
        PageService pages = CreateService(storage);

        (RenderResult result, string output) = await RenderAsync(pages, "/race");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("second"));
        Assert.That(_compiler.CompileCount, Is.EqualTo(2));
        Assert.That(Stored(storage, "/race").Content, Is.EqualTo("second"));
    }

    [Test]
    public async Task TemplateExceptionsPropagateAsync() {
        FakeStorage storage = new();
        storage["/throw"] = new StoredPage(Key("/throw"), "<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        PageService pages = CreateService(storage);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(pages, "/throw"));

        Assert.That(thrown?.Message, Is.EqualTo("Boom."));
    }

    private PageService CreateService(FakeStorage storage) {
        return new PageService(storage, _compiler, _metrics, NullLogger<PageService>.Instance);
    }

    private static async Task<(RenderResult Result, string Output)> RenderAsync(PageService pages, string path) {
        PagePath key = Key(path);
        StringWriter output = new();
        IContext context = new SimpleContext(NullLoggerFactory.Instance, TestHttp.Request(key.Path), CancellationToken.None, output);

        RenderResult result = await pages.RenderAsync(key, context, CancellationToken.None);

        return (result, output.ToString());
    }

    private static StoredPage Stored(FakeStorage storage, string path) {
        StoredPage? page = storage[path];
        Assert.That(page, Is.Not.Null, $"{path} was not stored");

        // The assertion above proves the page exists.
        return page!;
    }

    private static PagePath Key(string path) {
        return PagePath.Normalize(path);
    }

    private sealed class FaultingCompiler : ITemplateCompiler {

        public string RuntimeId => "faulting";

        public CompileResult Compile(string source) {
            throw new InvalidOperationException("The generated page source did not compile.");
        }

        public CompiledTemplate Load(byte[] image) {
            throw new NotSupportedException("A faulting compiler never produces an image.");
        }
    }
}
