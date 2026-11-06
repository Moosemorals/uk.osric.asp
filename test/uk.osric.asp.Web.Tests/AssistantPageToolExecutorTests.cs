// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Text.Json;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AssistantPageToolExecutorTests {

    [Test]
    public async Task EditPageReplacesOnlyTheSelectedLinesAndReturnsDiagnosticsAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/notes"), "first\r\nsecond\r\nthird");
        storage["/notes"] = original;
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);
        string arguments = JsonSerializer.Serialize(new {
            path = "/notes",
            startLine = 2,
            endLine = 2,
            expectedRevisionHash = original.Hash,
            replacement = "changed",
        });

        AssistantToolExecutionResult result = await tools.ExecuteAsync("edit_page", arguments, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Content);
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("first\r\nchanged\r\nthird"));
        using JsonDocument document = JsonDocument.Parse(result.Content);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("revisionHash").GetString(), Is.Not.EqualTo(original.Hash));
    }

    [Test]
    public async Task StaleEditReturnsConflictWithoutWritingAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "original");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);

        AssistantToolExecutionResult result = await tools.ExecuteAsync(
            "edit_page",
            "{\"path\":\"/notes\",\"startLine\":1,\"endLine\":1,\"expectedRevisionHash\":\"stale\",\"replacement\":\"changed\"}",
            CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Content, Does.Contain("changed since read_page"));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/notes"]?.Template, Is.EqualTo("original"));
    }

    [Test]
    public async Task ProtectedPathIsRejectedBeforeReadingStorageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);

        AssistantToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/editor.js\"}", CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task WriteReturnsCompileDiagnosticsAndARevisionHashAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);

        AssistantToolExecutionResult result = await tools.ExecuteAsync(
            "write_page", "{\"path\":\"/new\",\"content\":\"<% int value = ; %>\"}", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(storage["/new"]?.Template, Is.EqualTo("<% int value = ; %>"));
        using JsonDocument document = JsonDocument.Parse(result.Content);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("revisionHash").GetString(), Is.Not.Empty);
        Assert.That(document.RootElement.GetProperty("data").GetProperty("diagnostics").GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task DeleteCreatesATombstoneAndKeepsTheRevisionAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "source");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);

        AssistantToolExecutionResult result = await tools.ExecuteAsync(
            "delete_page", "{\"path\":\"/notes\"}", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(await storage.GetPageAsync(PageHarness.Key("/notes")), Is.Null);
        Assert.That((await storage.ListRevisionsAsync(PageHarness.Key("/notes"))).Count, Is.EqualTo(1));
        Assert.That((await storage.GetHeadAsync(PageHarness.Key("/notes")))?.IsDeleted, Is.True);
    }

    [Test]
    public async Task ReadPageLineNumbersReferToTheStoredTemplateSourceAsync() {
        FakePageStore storage = new();
        storage["/template"] = new PageTemplate(PageHarness.Key("/template"),
            "<h1>Title</h1>\r\n<%\r\nif (true) {\r\n%>\r\n<%= \"value\" %>\r\n<% } %>");
        using PageHarness pages = new(storage);
        using ServiceProvider services = Services(pages.Editor);
        AssistantPageToolExecutor tools = CreateTools(services);

        AssistantToolExecutionResult result = await tools.ExecuteAsync(
            "read_page", "{\"path\":\"/template\",\"startLine\":5,\"endLine\":5}", CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(result.Content);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.That(data.GetProperty("startLine").GetInt32(), Is.EqualTo(5));
        Assert.That(data.GetProperty("source").GetString(), Is.EqualTo("<%= \"value\" %>"));
    }

    private static ServiceProvider Services(PageEditor editor) =>
        new ServiceCollection().AddScoped(_ => editor).BuildServiceProvider();

    private static AssistantPageToolExecutor CreateTools(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        new AssistantPaths(new ReservedPaths(ReservedPaths.Default), new AssistantOptions { ProtectedPaths = ["/editor.js"] }),
        new AssistantOptions { MaxToolArgumentChars = 4096, MaxToolResultChars = 4096, MaxPageChars = 4096 });
}
