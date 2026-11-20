// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Reflection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;

/// <summary>
/// The table is the only list of tools, so these tests hold the invariants the two switches used to
/// carry: a tool cannot be declared twice, cannot be left out of the model's list, and cannot claim
/// a fact about itself that the rest of the wiring contradicts.
/// </summary>
[TestFixture]
public sealed class ToolSpecsTests {

    private static IEnumerable<ToolSpec> Specs => ToolRegistry.Default.All.Select(tool => tool.Spec);

    [Test]
    public void EveryToolHasAUniqueName() {
        string[] names = [.. Specs.Select(tool => tool.Name)];

        Assert.That(names, Is.Unique);
    }

    [Test]
    public void TheModelIsOfferedEveryToolInDeclarationOrder() {
        string[] declared = [.. Specs.Select(tool => tool.Name)];
        string[] offered = [.. PageToolExecutor.Definitions.Select(tool => tool.Name)];

        Assert.That(offered, Is.EqualTo(declared));
    }

    [Test]
    public void EveryToolThatWritesIsApprovalGated() {
        ToolSpec[] ungated = [.. Specs.Where(tool => tool.Writes && !tool.NeedsApproval)];

        Assert.That(ungated, Is.Empty);
    }

    [Test]
    public void EveryPathArgumentIsOneTheToolDeclares() {
        IEnumerable<string> undeclared = Specs
            .Where(tool => tool.PathArgument is not null)
            .Where(tool => !tool.Arguments.Any(argument => argument.Name == tool.PathArgument))
            .Select(tool => tool.Name);

        Assert.That(undeclared, Is.Empty);
    }

    [Test]
    public void OnlyAnApprovalGatedToolRefusesAPathBeforeThePrompt() {
        IEnumerable<string> ungated = Specs
            .Where(tool => tool.PathArgument is not null && !tool.NeedsApproval)
            .Select(tool => tool.Name);

        Assert.That(ungated, Is.Empty);
    }

    [Test]
    public void EveryToolCarriesTheVerbItsRefusalsUse() {
        string[] withoutVerb = [.. Specs.Where(tool => tool.Verb.Length == 0).Select(tool => tool.Name)];

        Assert.That(withoutVerb, Is.Empty);
    }

    [Test]
    public void TheRegistryHoldsEveryTool() {
        Assert.That(ToolRegistry.Default.All, Has.Count.EqualTo(20));
    }

    [Test]
    public void EveryToolTypeIsConcreteAndOverridesItsRun() {
        MethodInfo run = typeof(AssistantTool).GetMethod(nameof(AssistantTool.RunAsync))!;

        IEnumerable<string> unimplemented = ToolRegistry.Default.All
            .Where(tool => tool.GetType().IsAbstract || tool.GetType().GetMethod(nameof(AssistantTool.RunAsync))!.DeclaringType == run.DeclaringType)
            .Select(tool => tool.GetType().Name);

        Assert.That(unimplemented, Is.Empty);
    }

    [Test]
    public void EveryToolTypeIsRegistered() {
        HashSet<Type> registered = [.. ToolRegistry.Default.All.Select(tool => tool.GetType())];

        IEnumerable<string?> unregistered = typeof(AssistantTool).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(AssistantTool)) && !type.IsAbstract && !registered.Contains(type))
            .Select(type => type.FullName);

        Assert.That(unregistered, Is.Empty);
    }


    [Test]
    public void OnlyTheQuestionToolWaitsForTheOwner() {
        string[] waiting = [.. Specs.Where(tool => tool.WaitsForOwner).Select(tool => tool.Name)];

        Assert.That(waiting, Is.EqualTo(["ask_user"]));
    }

    [Test]
    public void AWaitingToolIsNotApprovalGated() {
        IEnumerable<string> gated = Specs
            .Where(tool => tool.WaitsForOwner && tool.NeedsApproval)
            .Select(tool => tool.Name);

        Assert.That(gated, Is.Empty);
    }

    [Test]
    public void EveryArgumentOfAWaitingToolIsDescribed() {
        IEnumerable<string> undescribed = Specs
            .Where(tool => tool.WaitsForOwner)
            .SelectMany(tool => Undescribed(tool.Arguments, tool.Name));

        Assert.That(undescribed, Is.Empty);
    }

    // A tool that describes no argument and names no closed set sends the schema it always sent, so
    // the provider's cached prefix does not move for the tools this change leaves alone.
    [Test]
    public void AToolWithNoDescriptionsOrValuesIsUnchangedOnTheWire() {
        ToolDefinition listing = PageToolExecutor.Definitions.Single(tool => tool.Name == "list_revisions");

        Assert.That(listing.ParametersJson, Is.EqualTo(
            """{"type":"object","properties":{"path":{"type":"string"},"limit":{"type":"integer"},"before":{"type":"string"}},"required":["path"],"additionalProperties":false}"""));
    }

    // The declaration is the one source: the schema renders whatever set the tool names, and a tool
    // that names none carries no `enum` at all.
    [Test]
    public void EveryClosedSetRendersFromItsDeclaration() {
        List<string> wrong = [];
        foreach (ToolSpec spec in Specs) {
            using JsonDocument parameters = JsonDocument.Parse(
                PageToolExecutor.Definitions.Single(tool => tool.Name == spec.Name).ParametersJson);
            foreach (ToolArgumentSchema argument in spec.Arguments) {
                JsonElement property = parameters.RootElement.GetProperty("properties").GetProperty(argument.Name);
                bool carries = property.TryGetProperty("enum", out JsonElement values);
                if (argument.Values is { Count: > 0 } declared) {
                    if (!carries || !values.EnumerateArray().Select(item => item.GetString()).SequenceEqual(declared)) {
                        wrong.Add($"{spec.Name}.{argument.Name}");
                    }
                } else if (carries) {
                    wrong.Add($"{spec.Name}.{argument.Name}");
                }
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    [Test]
    public void AClosedSetIsValidatedBeforeTheToolRuns() {
        IReadOnlyList<ToolArgumentSchema> check = ToolRegistry.Default.Find("check")!.Spec.Arguments;

        ToolArgumentsResult refused = ToolArguments.Parse(
            """{"language":"css","content":"body { color: red; }"}""", check, maximumChars: 4_096);
        ToolArgumentsResult accepted = ToolArguments.Parse(
            """{"language":"javascript","content":"export const answer = 42;"}""", check, maximumChars: 4_096);

        Assert.Multiple(() => {
            Assert.That(refused.Error, Does.Contain("must be one of template, javascript"));
            Assert.That(accepted.IsSuccess, Is.True, accepted.Error);
        });
    }

    private static IEnumerable<string> Undescribed(IEnumerable<ToolArgumentSchema> arguments, string prefix) {
        foreach (ToolArgumentSchema argument in arguments) {
            string name = $"{prefix}.{argument.Name}";
            if (argument.Description is null) {
                yield return name;
            }
            foreach (string nested in Undescribed(argument.Items ?? [], name)) {
                yield return nested;
            }
        }
    }

    [TestCase("write_page", ExpectedResult = true)]
    [TestCase("edit_page", ExpectedResult = true)]
    [TestCase("restore_revision", ExpectedResult = true)]
    [TestCase("list_revisions", ExpectedResult = false)]
    [TestCase("delete_page", ExpectedResult = false)]
    [TestCase("read_page", ExpectedResult = false)]
    public bool OnlyThePageWritingToolsChangeAPage(string toolName) =>
        PageToolExecutor.ChangesPages(toolName);
}
