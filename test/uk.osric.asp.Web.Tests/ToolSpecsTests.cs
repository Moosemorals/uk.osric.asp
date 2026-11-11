// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;

/// <summary>
/// The table is the only list of tools, so these tests hold the invariants the two switches used to
/// carry: a tool cannot be declared twice, cannot be left out of the model's list, and cannot claim
/// a fact about itself that the rest of the wiring contradicts.
/// </summary>
[TestFixture]
public sealed class ToolSpecsTests {

    [Test]
    public void EveryToolHasAUniqueName() {
        string[] names = [.. PageToolExecutor.Specs.Select(tool => tool.Name)];

        Assert.That(names, Is.Unique);
    }

    [Test]
    public void TheModelIsOfferedEveryToolInDeclarationOrder() {
        string[] declared = [.. PageToolExecutor.Specs.Select(tool => tool.Name)];
        string[] offered = [.. PageToolExecutor.Definitions.Select(tool => tool.Name)];

        Assert.That(offered, Is.EqualTo(declared));
    }

    [Test]
    public void EveryToolThatWritesIsApprovalGated() {
        ToolSpec[] ungated = [.. PageToolExecutor.Specs.Where(tool => tool.Writes && !tool.NeedsApproval)];

        Assert.That(ungated, Is.Empty);
    }

    [Test]
    public void EveryPathArgumentIsOneTheToolDeclares() {
        IEnumerable<string> undeclared = PageToolExecutor.Specs
            .Where(tool => tool.PathArgument is not null)
            .Where(tool => !tool.Arguments.Any(argument => argument.Name == tool.PathArgument))
            .Select(tool => tool.Name);

        Assert.That(undeclared, Is.Empty);
    }

    [Test]
    public void OnlyAnApprovalGatedToolRefusesAPathBeforeThePrompt() {
        IEnumerable<string> ungated = PageToolExecutor.Specs
            .Where(tool => tool.PathArgument is not null && !tool.NeedsApproval)
            .Select(tool => tool.Name);

        Assert.That(ungated, Is.Empty);
    }

    [Test]
    public void EveryToolCarriesTheVerbItsRefusalsUse() {
        string[] withoutVerb = [.. PageToolExecutor.Specs.Where(tool => tool.Verb.Length == 0).Select(tool => tool.Name)];

        Assert.That(withoutVerb, Is.Empty);
    }

    [TestCase("write_page", ExpectedResult = true)]
    [TestCase("edit_page", ExpectedResult = true)]
    [TestCase("delete_page", ExpectedResult = false)]
    [TestCase("read_page", ExpectedResult = false)]
    public bool OnlyThePageWritingToolsChangeAPage(string toolName) =>
        PageToolExecutor.ChangesPages(toolName);
}
