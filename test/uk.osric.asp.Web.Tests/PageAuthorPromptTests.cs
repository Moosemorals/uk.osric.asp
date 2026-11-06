// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Agent;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageAuthorPromptTests {

    [Test]
    public void ContractSectionContainsEveryIContextMember() {
        string prompt = PageAuthorPrompt.Build();
        IEnumerable<string> members = typeof(IContext).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member is PropertyInfo || member is MethodInfo { IsSpecialName: false })
            .Select(member => member.Name);

        Assert.That(members.All(prompt.Contains), Is.True);
    }

    [Test]
    public void PolicyNamesThePageApiNamespacesThatMustStayUnavailable() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("System.IO"));
            Assert.That(prompt, Does.Contain("System.Net"));
            Assert.That(prompt, Does.Contain("System.Reflection"));
            Assert.That(prompt, Does.Contain("System.Text.RegularExpressions"));
            Assert.That(prompt, Does.Contain("Microsoft.*"));
        });
    }

    [Test]
    public void PromptExamplesCompileThroughTheRealPageCompiler() {
        using PageHarness pages = new(new FakePageStore());

        foreach ((string name, string source) in PageAuthorPrompt.Examples) {
            PageImage compilation = pages.Images.Build(
                PagePath.Normalize("/prompt-" + name), source, PageMetrics.ReasonSave, CancellationToken.None);

            Assert.That(compilation.Bytes, Is.Not.Null, $"{name}: {string.Join("; ", compilation.Diagnostics.Select(item => item.Message))}");
            Assert.That(compilation.Diagnostics.Where(item => item.Severity == PageDiagnosticSeverity.Error), Is.Empty, name);
        }
    }

    [Test]
    public void PromptVersionIsStableAndPresentInThePrompt() {
        Assert.That(PageAuthorPrompt.Build(), Does.Contain(PageAuthorPrompt.Version));
    }
}
