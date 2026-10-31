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

        IReadOnlyList<string> diagnostics = await pages.SaveAsync(Key("/hello"), "Hello.");

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

        IReadOnlyList<string> diagnostics = await pages.SaveAsync(Key("/broken"), "<% int value = ; %>");

        Assert.That(diagnostics, Is.Not.Empty);
        Assert.That(diagnostics, Has.Some.Contains("error CS"));

        StoredPage saved = Stored(storage, "/broken");
        Assert.That(saved.Content, Is.EqualTo("<% int value = ; %>"));
        Assert.That(saved.Compilation?.Image, Is.Null);
        Assert.That(saved.Compilation?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
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
        PageCompilation failed = new(ContentFingerprint.Of("<% int value = ; %>"), _compiler.RuntimeId, null, ["error CS1525: broken"]);
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
    public void TemplateExceptionsPropagate() {
        FakeStorage storage = new();
        storage["/throw"] = new StoredPage(Key("/throw"), "<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        PageService pages = CreateService(storage);

        InvalidOperationException? thrown = Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(pages, "/throw"));

        Assert.That(thrown?.Message, Is.EqualTo("Boom."));
    }

    private PageService CreateService(FakeStorage storage) {
        return new PageService(storage, _compiler, _metrics, NullLogger<PageService>.Instance);
    }

    private static async Task<(RenderResult Result, string Output)> RenderAsync(PageService pages, string path) {
        PagePath key = Key(path);
        StringWriter output = new();
        IContext context = new SimpleContext(NullLogger.Instance, TestHttp.Request(key.Path), CancellationToken.None, output);

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
}
