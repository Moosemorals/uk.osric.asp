// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Assistant;
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
    public void TheStorageSectionTeachesTheShapesThatWork() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Page storage"));
            Assert.That(prompt, Does.Contain("TryGetValueAsync<T>"));
            Assert.That(prompt, Does.Contain("SetAsync"));
            Assert.That(prompt, Does.Contain("PageValue<int>"));
            Assert.That(prompt, Does.Contain("unawaited"), "a dropped task is a page bug and must be said");
        });
    }

    [Test]
    public void TheFetchSectionSaysFailuresAreValues() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Outbound fetch"));
            Assert.That(prompt, Does.Contain("ctx.FetchAsync"));
            Assert.That(prompt, Does.Contain("allow-listed"));
            Assert.That(prompt, Does.Contain("Failures are values"));
        });
    }

    [Test]
    public void TheHandlerIsNoLongerDescribedAsSynchronous() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Not.Contain("contains synchronous C# statements"));
            Assert.That(prompt, Does.Not.Contain("handler is synchronous"));
            Assert.That(prompt, Does.Contain("await"));
        });
    }

    [Test]
    public void PromptVersionIsStableAndPresentInThePrompt() {
        Assert.That(PageAuthorPrompt.Build(), Does.Contain(PageAuthorPrompt.Version));
    }
}
