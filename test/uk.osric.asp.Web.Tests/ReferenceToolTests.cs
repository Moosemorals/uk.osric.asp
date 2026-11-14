// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Xml.Linq;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;
using uk.osric.asp.Web.Documentation;
using uk.osric.asp.Web.Pages;

/// <summary>The assistant's read-only view of the page contract.</summary>
[TestFixture]
public sealed class ReferenceToolTests {

    private static readonly AssistantOptions Options = TestOptions.Assistant with {
        MaxToolArgumentChars = 8_192,
        MaxToolResultChars = 131_072,
        DefaultListLimit = 50,
        MaxListLimit = 500,
    };

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    private ServiceProvider _services = null!;
    private PageToolExecutor _tools = null!;

    [SetUp]
    public void SetUp() {
        ServiceCollection services = new();
        services.AddSingleton(new ReferenceLibrary(AppContext.BaseDirectory, NullLogger<ReferenceLibrary>.Instance));
        services.AddSingleton(Options);
        services.AddSingleton(new AssistantMetrics());
        services.AddSingleton(TestOptions.Editor);
        _services = services.BuildServiceProvider();
        _tools = new PageToolExecutor(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), Options),
            Options,
            _services.GetRequiredService<AssistantMetrics>(),
            new FakeAssistantFetcher());
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public async Task ATypeAnswersItsOwnWordsAndMembers() {
        ToolExecutionResult result = await ExecuteAsync("""{"type":"IPageStorage"}""");
        JsonElement data = Data(result);

        string[] members = [.. data.GetProperty("type").GetProperty("members").EnumerateArray()
            .Select(member => member.GetProperty("display").GetString() ?? "")];

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("type").GetProperty("name").GetString(), Is.EqualTo("IPageStorage"));
            Assert.That(data.GetProperty("type").GetProperty("summary").GetString(), Is.Not.Empty);
            Assert.That(members, Has.Some.Contains("TryGetValueAsync<T>(string key)"));
            Assert.That(members, Has.Some.Contains("SetAsync<T>(string key, T value)"));
        });
    }

    [Test]
    public async Task ATypeCanBeNamedByItsRawIdOrItsGenericForm() {
        ToolExecutionResult raw = await ExecuteAsync("""{"type":"T:uk.osric.asp.Sdk.IContext"}""");
        ToolExecutionResult generic = await ExecuteAsync("""{"type":"PageValue<T>"}""");
        Assert.Multiple(() => {
            Assert.That(raw.IsSuccess, Is.True, "raw: " + raw.Content);
            Assert.That(generic.IsSuccess, Is.True, "generic: " + generic.Content);
            Assert.That(Data(raw).GetProperty("type").GetProperty("name").GetString(), Is.EqualTo("IContext"));
            Assert.That(Data(generic).GetProperty("type").GetProperty("name").GetString(), Is.EqualTo("PageValue<T>"));
        });
    }

    [Test]
    public async Task ATypeInAnotherAssemblyIsRefusedByAssembly() {
        ToolExecutionResult result = await ExecuteAsync("""{"type":"PagePath"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("uk.osric.asp.Core"));
            Assert.That(result.Content, Does.Contain("page contract"));
        });
    }

    [Test]
    public async Task AnUnknownTypePointsAtTheQueryForm() {
        ToolExecutionResult result = await ExecuteAsync("""{"type":"NotAType"}""");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("reference query="));
        });
    }

    [TestCase("""{}""")]
    [TestCase("""{"type":"IPageStorage","query":"storage"}""")]
    public async Task ExactlyOneFormIsRequired(string arguments) {
        ToolExecutionResult result = await ExecuteAsync(arguments);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("exactly one"));
            Assert.That(result.Content, Does.Contain("query"));
        });
    }

    [Test]
    public async Task AQueryAnswersTheTypeThatHoldsEachMatch() {
        ToolExecutionResult result = await ExecuteAsync("""{"query":"storage"}""");
        JsonElement data = Data(result);
        string[] types = [.. data.GetProperty("matches").EnumerateArray()
            .Select(match => match.GetProperty("type").GetString() ?? "")];

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(types, Has.Some.Contains("IPageStorage"));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task AQueryIsBoundedAndSaysThereIsMore() {
        ToolExecutionResult result = await ExecuteAsync("""{"query":"e","limit":2}""");
        JsonElement data = Data(result);

        Assert.Multiple(() => {
            Assert.That(data.GetProperty("matches").GetArrayLength(), Is.EqualTo(2));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void TheToolIsReadOnlyAndNeedsNoApproval() {
        ToolSpec spec = ToolRegistry.Default.Find("reference")!.Spec;

        Assert.Multiple(() => {
            Assert.That(spec.Writes, Is.False);
            Assert.That(spec.NeedsApproval, Is.False);
            Assert.That(spec.Verb, Is.EqualTo("Reference"));
        });
    }

    [Test]
    public void TheFlattenerTurnsParagraphsListsAndSeesIntoText() {
        XElement summary = XElement.Parse("""
            <summary><para>One.</para><para>Two <see cref="T:System.String" text="string" />.</para>
              <list type="bullet"><item><term>A</term><description>B</description></item></list></summary>
            """);

        Assert.That(ReferenceText.Flatten(summary), Is.EqualTo("One.\n\nTwo string.\n\n- A — B"));
    }

    private async Task<ToolExecutionResult> ExecuteAsync(string arguments) =>
        await _tools.ExecuteAsync("reference", arguments, Context, CancellationToken.None);

    private static JsonElement Data(ToolExecutionResult result) {
        using JsonDocument document = JsonDocument.Parse(result.Content);
        return document.RootElement.GetProperty("data").Clone();
    }
}
