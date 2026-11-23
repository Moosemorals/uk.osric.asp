// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using ReferenceCorpus;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Documentation;

/// <summary>The string rules over Roslyn's member ids.</summary>
[TestFixture]
public sealed class ReferenceDisplayTests {

    private const string Sdk = "uk.osric.asp.Sdk";
    private const string Web = "uk.osric.asp.Web";

    [Test]
    public void AnOwnersIdIsTheTypeTheMemberNames() {
        Assert.Multiple(() => {
            Assert.That(ReferenceDisplay.Owner("M:uk.osric.asp.Sdk.IContext.Write(System.String)"),
                Is.EqualTo("T:uk.osric.asp.Sdk.IContext"));
            Assert.That(ReferenceDisplay.Owner("M:Ns.Outer.Inner.Method(System.String)"),
                Is.EqualTo("T:Ns.Outer.Inner"));
            Assert.That(ReferenceDisplay.Owner("M:Ns.Type.Ns#IFoo#Bar"),
                Is.EqualTo("T:Ns.Type"));
            Assert.That(ReferenceDisplay.Owner("T:Ns.Type"), Is.Null);
        });
    }

    [TestCase("T:uk.osric.asp.Sdk.IContext", "IContext", "IContext")]
    [TestCase("T:uk.osric.asp.Web.Pages.PageSeedInstaller.SeedResult", "Pages.PageSeedInstaller.SeedResult",
        "Pages.PageSeedInstaller.SeedResult")]
    [TestCase("T:uk.osric.asp.Sdk.PageValue`1", "PageValue", "PageValue<T>")]
    public void ATypeIsItsNameWithinItsNamespace(string id, string name, string display) {
        IReadOnlyList<string> typeParameters = display.Contains('<') ? ["T"] : [];

        Assert.Multiple(() => {
            Assert.That(ReferenceDisplay.Relative(id, id.Contains(Sdk, StringComparison.Ordinal) ? Sdk : Web),
                Is.EqualTo(name).Or.EqualTo(name + "`1"));
            Assert.That(ReferenceDisplay.TypeDisplay(id,
                id.Contains(Sdk, StringComparison.Ordinal) ? Sdk : Web, typeParameters), Is.EqualTo(display));
        });
    }

    [TestCase("P:uk.osric.asp.Sdk.IContext.Request", "Request")]
    [TestCase("F:uk.osric.asp.Sdk.HttpFetchFailure.None", "None")]
    [TestCase("M:uk.osric.asp.Sdk.IContext.FlushAsync", "FlushAsync()")]
    [TestCase("M:uk.osric.asp.Web.Logging.LogScope.System#Collections#IEnumerable#GetEnumerator",
        "System.Collections.IEnumerable.GetEnumerator()")]
    public void AMemberWithoutParametersKeepsItsName(string id, string display) {
        Assert.That(ReferenceDisplay.MemberDisplay(id,
            id.Contains(Sdk, StringComparison.Ordinal) ? Sdk : Web, [], [], []), Is.EqualTo(display));
    }

    [Test]
    public void AParameterListIsShortenedAndNamedByPosition() {
        Assert.Multiple(() => {
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.IPageStorage.TryGetValueAsync``1(System.String)", Sdk, [], ["T"], ["key"]),
                Is.EqualTo("TryGetValueAsync<T>(string key)"));
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.IPageCapabilities.TryGet``1(``0@)", Sdk, [], [], ["capability"]),
                Is.EqualTo("TryGet<T>(ref T capability)"));
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.HttpFetchRequest.#ctor(System.String)", Sdk, [], [], ["Url"]),
                Is.EqualTo("HttpFetchRequest(string Url)"));
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.IContext.Redirect(System.String,System.Int32)", Sdk, [], [], []),
                Is.EqualTo("Redirect(string, int)"));
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.IPageStorage.SetAsync``1(System.String,``0)", Sdk, [], ["T"], ["key", "value"]),
                Is.EqualTo("SetAsync<T>(string key, T value)"));
        });
    }

    [Test]
    public void ANullableAnArrayAndAGenericArgumentKeepTheirShape() {
        Assert.Multiple(() => {
            Assert.That(ReferenceDisplay.MemberDisplay(
                "M:uk.osric.asp.Sdk.Test(System.Nullable{System.Int32},System.String[],System.Collections.Generic.IReadOnlyList{System.String})",
                Sdk, [], [], []),
                Is.EqualTo("Test(int?, string[], IReadOnlyList<string>)"));
        });
    }

    [TestCase("T:uk.osric.asp.Sdk.IContext", "t-IContext")]
    [TestCase("T:uk.osric.asp.Sdk.PageValue`1", "t-PageValue-1")]
    [TestCase("T:uk.osric.asp.Web.Pages.PageSeedInstaller.SeedResult", "t-Pages.PageSeedInstaller.SeedResult")]
    [TestCase("P:uk.osric.asp.Sdk.IContext.Request", "m-IContext.Request")]
    [TestCase("M:uk.osric.asp.Sdk.IContext.Redirect(System.String,System.Int32)", "m-IContext.Redirect-string-int")]
    [TestCase("M:uk.osric.asp.Sdk.IPageStorage.TryGetValueAsync``1(System.String)", "m-IPageStorage.TryGetValueAsync-g1-string")]
    [TestCase("M:uk.osric.asp.Sdk.IPageCapabilities.TryGet``1(``0@)", "m-IPageCapabilities.TryGet-g1-ref-T")]
    [TestCase("F:uk.osric.asp.Sdk.HttpFetchFailure.None", "m-HttpFetchFailure.None")]
    [TestCase("M:uk.osric.asp.Web.Logging.LogScope.System#Collections#IEnumerable#GetEnumerator",
        "m-Logging.LogScope.System.Collections.IEnumerable.GetEnumerator")]
    public void AnAnchorIsDerivedFromTheId(string id, string anchor) {
        Assert.That(ReferenceDisplay.Anchor(id,
            id.Contains(Sdk, StringComparison.Ordinal) ? Sdk : Web, [], []), Is.EqualTo(anchor));
    }

    [TestCase("T:System.Threading.Tasks.Task", "system.threading.tasks.task")]
    [TestCase("T:System.Collections.Generic.IReadOnlyList`1", "system.collections.generic.ireadonlylist-1")]
    [TestCase("P:System.Uri.IdnHost", "system.uri.idnhost")]
    [TestCase("M:System.IO.StringReader.ReadLine", "system.io.stringreader.readline")]
    [TestCase("M:System.String.Join`1(System.String,System.Collections.Generic.IEnumerable{`0})", "system.string.join")]
    [TestCase("F:System.String.Empty", "system.string")]
    [TestCase("M:System.String.#ctor(System.String)", "system.string")]
    [TestCase("T:Microsoft.AspNetCore.Http.IResult", "microsoft.aspnetcore.http.iresult")]
    public void APlatformTargetGetsALearnUrl(string id, string path) {
        Assert.That(ReferenceDisplay.LearnUrl(id), Is.EqualTo(ReferenceDisplay.LearnBase + path));
    }

    [TestCase("T:Markdig.Renderers.HtmlRenderer")]
    [TestCase("M:uk.osric.asp.Web.Logging.LogScope.System#Collections#IEnumerable#GetEnumerator")]
    [TestCase("T:uk.osric.asp.Sdk.IContext")]
    public void ATargetThatIsNotPlatformOrOursHasNoLearnUrl(string id) {
        Assert.That(ReferenceDisplay.LearnUrl(id), Is.Null);
    }

    [TestCase("F:System.String.Empty", "string.Empty")]
    [TestCase("T:System.Threading.Tasks.Task", "Task")]
    [TestCase("M:System.IO.StringReader.ReadLine", "StringReader.ReadLine")]
    [TestCase("T:System.Collections.Generic.IReadOnlyList`1", "IReadOnlyList<T>")]
    public void AnUnresolvedCrefShortensToItsName(string id, string text) {
        Assert.That(ReferenceDisplay.CrefText(id), Is.EqualTo(text));
    }

    [TestCase("uk.osric.asp.Sdk")]
    [TestCase("uk.osric.asp.Core")]
    [TestCase("uk.osric.asp.Runtime")]
    [TestCase("uk.osric.asp.Web")]
    public void TheRealDocumentHasUniqueAnchors(string assemblyName) {
        ReferenceAssembly assembly = TestReference.Library.Assemblies.Single(carried => carried.Info.AssemblyName == assemblyName);

        string[] anchors = [.. assembly.Corpus.Namespaces
            .SelectMany(space => space.Types)
            .SelectMany(type => type.Members.Select(member => member.Anchor).Prepend(type.Anchor))];

        Assert.Multiple(() => {
            Assert.That(anchors, Is.Unique);
            Assert.That(assembly.TypeCount, Is.GreaterThan(0));
            Assert.That(assembly.EntryCount, Is.GreaterThan(0));
        });
    }
}
