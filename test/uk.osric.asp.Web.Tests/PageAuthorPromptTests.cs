// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

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
            Assert.That(prompt, Does.Contain("ctx.Features.GetRequired<IPageStorage>()"));
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
            Assert.That(prompt, Does.Contain("ctx.Features.GetRequired<IHttpFetch>()"));
            Assert.That(prompt, Does.Contain("GetAsync(url)"));
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
    public void TheAuthenticationSectionTeachesTheGuard() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Page authentication"));
            Assert.That(prompt, Does.Contain("ctx.User is not { } user"));
            Assert.That(prompt, Does.Contain("ctx.RedirectToLogin()"));
            Assert.That(prompt, Does.Contain("ctx.AntiforgeryField"));
            Assert.That(prompt, Does.Contain("public unless the page's own code checks"));
            Assert.That(prompt, Does.Contain("write a sign-in form"));
        });
    }

    [Test]
    public void TheScriptSectionTeachesNativeModules() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Scripts"));
            Assert.That(prompt, Does.Contain("native ES module"));
            Assert.That(prompt, Does.Contain("export {};"));
            Assert.That(prompt, Does.Contain("<script type=\"module\" src=\"/…\">"));
        });
    }

    [Test]
    public void TheScriptSectionTeachesTheExtensionDefaultAndTheCachePolicy() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("response type follows the extension in its key"));
            Assert.That(prompt, Does.Contain("`.js` and `.mjs`"));
            Assert.That(prompt, Does.Contain("only to override that default"));
            Assert.That(prompt, Does.Contain("Cache-Control: no-cache"));
            Assert.That(prompt, Does.Contain("do not assume a fixed list"), "the map belongs to the host");
        });
    }

    [Test]
    public void TheLayoutSectionTeachesAFragmentAndATitle() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("A page is a fragment, not a document"));
            Assert.That(prompt, Does.Contain("IPageLayout layout = ctx.Features.GetRequired<IPageLayout>()"));
            Assert.That(prompt, Does.Contain("must set `layout.Title`"));
            Assert.That(prompt, Does.Contain("layout.AddStylesheet"));
            Assert.That(prompt, Does.Contain("layout.AddHead"));
            Assert.That(prompt, Does.Contain("serves the page alone"));
        });
    }

    [Test]
    public void ThePromptIsVersionedForNullablePages() {
        Assert.Multiple(() => {
            Assert.That(PageAuthorPrompt.Version, Is.EqualTo("page-author-v12"));
            Assert.That(PageAuthorPrompt.Build(), Does.Contain("## Nullable page code"));
        });
    }

    [Test]
    public void TheShellFileSectionTeachesWhatAShellFileIs() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Shell files"));
            Assert.That(prompt, Does.Contain("`/edit`, `/login`"));
            Assert.That(prompt, Does.Contain("-r--r--r--"));
            Assert.That(prompt, Does.Contain("-rw-r--r--"));
            Assert.That(prompt, Does.Contain("shell file"));
            Assert.That(prompt, Does.Contain("not permitted"));
            Assert.That(prompt, Does.Contain("`/site.css`"),
                "the seeded assets are ordinary pages, not shell files");
            Assert.That(prompt, Does.Contain("Pages:ReservedPaths"));
        });
    }

    [Test]
    public void ThePromptSaysAWarningIsUnfinishedWork() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("runnable and not finished"));
            Assert.That(prompt, Does.Contain("fixing the warnings"));
            Assert.That(prompt, Does.Contain("does not stop the page being served"));
        });
    }

    [Test]
    public void ThePromptNamesTheNullableGuardIdiom() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("nullable analysis on"));
            Assert.That(prompt, Does.Contain("`ctx.User` is a `PageUser?`"));
            Assert.That(prompt, Does.Contain("ctx.User is not { } user"));
            Assert.That(prompt, Does.Contain("one check, then a local"));
        });
    }

    [Test]
    public void TheExamplesCompileWithoutWarningsUnderNullableAnalysis() {
        using PageHarness pages = new(new FakePageStore());

        foreach ((string name, string source) in PageAuthorPrompt.Examples) {
            PageImage compilation = pages.Images.Build(
                PagePath.Normalize("/prompt-" + name), source, PageMetrics.ReasonSave, CancellationToken.None);

            Assert.That(compilation.Diagnostics, Is.Empty,
                $"{name}: {string.Join("; ", compilation.Diagnostics.Select(item => item.Message))}");
        }
    }

    [Test]
    public void ThePageStoreReadSectionSaysItIsPublic() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Page store read"));
            Assert.That(prompt, Does.Contain("ctx.Features.GetRequired<IPageCatalog>()"));
            Assert.That(prompt, Does.Contain("ListAsync"));
            Assert.That(prompt, Does.Contain("ReadRevisionAsync"));
            Assert.That(prompt, Does.Contain("as public as the page that calls it"));
            Assert.That(prompt, Does.Contain("publishes the source it reads"));
        });
    }

    [Test]
    public void ThePrivatePageExampleIsPresent() {
        Assert.That(PageAuthorPrompt.Examples.Select(example => example.Name), Does.Contain("private-page"));
    }

    [Test]
    public void PromptVersionIsStableAndPresentInThePrompt() {
        Assert.That(PageAuthorPrompt.Build(), Does.Contain(PageAuthorPrompt.Version));
    }

    [Test]
    public void TheScriptSectionTeachesTheScriptChecks() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("check_script_text"));
            Assert.That(prompt, Does.Contain("check_script` with the page's path"));
            Assert.That(prompt, Does.Contain("valid: false"));
            Assert.That(prompt, Does.Contain("SCRIPT002"));
            Assert.That(PageAuthorPrompt.Version, Is.EqualTo("page-author-v12"));
        });
    }

    [Test]
    public void TheMarkdownSectionTeachesThePageCapabilityAndTheReplySet() {
        string prompt = PageAuthorPrompt.Build();

        Assert.Multiple(() => {
            Assert.That(prompt, Does.Contain("## Markdown"));
            Assert.That(prompt, Does.Contain("ctx.Features.GetRequired<IMarkdownRenderer>()"));
            Assert.That(prompt, Does.Contain("ToHtml"));
            Assert.That(prompt, Does.Contain("passes through"));
            Assert.That(prompt, Does.Contain("not a"));
            Assert.That(prompt, Does.Contain("sanitiser"));
            Assert.That(prompt, Does.Contain("pipe tables"), "the assistant may use tables");
            Assert.That(prompt, Does.Contain("images, raw HTML,"));
            Assert.That(prompt, Does.Contain("h3"));
        });
    }
}
