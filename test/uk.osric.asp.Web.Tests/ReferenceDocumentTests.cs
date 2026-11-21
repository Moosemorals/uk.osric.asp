// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>The normalised document and the corpus built from it.</summary>
[TestFixture]
public sealed class ReferenceDocumentTests {

    private static readonly ReferenceAssemblyInfo Info = new("test", "Test.Assembly", "The test", "For tests.");

    private static readonly string[] ExpectedNamespaces = ["", "Widgets"];
    private static readonly string[] ExpectedTypes = ["Orphan", "Other", "Outer", "Outer.Inner", "Button"];

    [Test]
    public void ANestedTypesMembersBelongToTheNestedType() {
        ReferenceDocument document = Build();

        XElement outer = Type(document, "Outer");
        XElement inner = Type(document, "Outer.Inner");

        Assert.Multiple(() => {
            Assert.That(outer.Elements("member").Select(member => (string?)member.Attribute("id")),
                Is.EquivalentTo(["P:Test.Assembly.Outer.Name"]));
            Assert.That(inner.Elements("member").Select(member => (string?)member.Attribute("id")),
                Is.EquivalentTo(["M:Test.Assembly.Outer.Inner.Run", "M:Test.Assembly.Outer.Inner.Inherited"]));
            Assert.That((string?)inner.Attribute("name"), Is.EqualTo("Outer.Inner"));
            Assert.That((string?)inner.Attribute("local"), Is.EqualTo("Inner"));
        });
    }

    [Test]
    public void AMemberOfAnUndocumentedTypeGetsASection() {
        ReferenceDocument document = Build();

        XElement orphan = Type(document, "Orphan");

        Assert.Multiple(() => {
            Assert.That((string?)orphan.Attribute("id"), Is.EqualTo("T:Test.Assembly.Orphan"));
            Assert.That(orphan.Elements("member").Count(), Is.EqualTo(1));
            Assert.That(document.TypeCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void TypesAreGroupedTheGlobalNamespaceFirstAndThenOrdinal() {
        ReferenceDocument document = Build();

        string[] namespaces = [.. document.Content.Root!.Elements("namespace").Select(element => (string?)element.Attribute("name") ?? "")];
        string[] types = [.. document.Content.Root!.Elements("namespace")
            .SelectMany(element => element.Elements("type"))
            .Select(element => (string?)element.Attribute("name") ?? "")];

        Assert.Multiple(() => {
            Assert.That(namespaces, Is.EqualTo(ExpectedNamespaces));
            Assert.That(types, Is.EqualTo(ExpectedTypes));
        });
    }

    [Test]
    public void ATypeEntryIsTheTypeDocumentationAndNotAMember() {
        ReferenceDocument document = Build();
        XElement outer = Type(document, "Outer");

        Assert.Multiple(() => {
            Assert.That(outer.Element("summary")?.Value, Is.EqualTo("Outer."));
            Assert.That(outer.Elements("member").Any(member => ((string?)member.Attribute("id"))?.StartsWith("T:", StringComparison.Ordinal) == true),
                Is.False);
            Assert.That((string?)outer.Attribute("display"), Is.EqualTo("Outer"));
        });
    }

    [Test]
    public void AnInheritOnlyEntryIsNotedWhenNoSourceCanBeFound() {
        ReferenceDocument document = Build();
        XElement member = document.Content.Descendants("member")
            .Single(item => (string?)item.Attribute("id") == "M:Test.Assembly.Outer.Inner.Inherited");

        ReferenceInheritance.Resolve([document], NullLogger<ReferenceLibrary>.Instance);

        Assert.Multiple(() => {
            Assert.That((string?)member.Attribute("inherits"), Is.EqualTo("true"));
            Assert.That(member.Elements("inheritdoc"), Is.Empty);
        });
    }

    [Test]
    public void TheRealCorpusResolvesInheritedTextAndNamesTheSource() {
        ReferenceLibrary library = TestReference.Library;
        ReferenceAssembly runtime = library.Assemblies.Single(assembly => assembly.Info.AssemblyName == "uk.osric.asp.Runtime");
        ReferenceAssembly web = library.Assemblies.Single(assembly => assembly.Info.AssemblyName == "uk.osric.asp.Web");

        XElement copied = runtime.Document.Descendants("member")
            .Single(item => item.Attribute("id")?.Value.StartsWith("M:uk.osric.asp.Runtime.DiffRenderer.ToHtml", StringComparison.Ordinal) == true);
        XElement elsewhere = web.Document.Descendants("member")
            .First(item => item.Attribute("inherited-href")?.Value.StartsWith(ReferenceDisplay.LearnBase, StringComparison.Ordinal) == true);

        Assert.Multiple(() => {
            Assert.That((string?)copied.Attribute("inherited-from"),
                Is.EqualTo("M:uk.osric.asp.Sdk.IDiffRenderer.ToHtml(System.String,System.String)"));
            Assert.That(copied.Element("summary")?.Value, Is.Not.Empty);
            Assert.That((string?)copied.Attribute("inherited-href"), Does.StartWith("/admin/reference/sdk#"));
            Assert.That((string?)elsewhere.Attribute("inherited-from"), Is.Not.Null);
        });
    }

    private static ReferenceDocument Build() {
        XDocument source = XDocument.Parse("""
            <doc>
              <assembly><name>Test.Assembly</name></assembly>
              <members>
                <member name="M:Test.Assembly.Orphan.Missing"><summary>Missing.</summary></member>
                <member name="P:Test.Assembly.Outer.Name"><summary>A name.</summary></member>
                <member name="T:Test.Assembly.Outer.Inner"><summary>Inner.</summary></member>
                <member name="M:Test.Assembly.Outer.Inner.Run"><summary>Runs.</summary></member>
                <member name="M:Test.Assembly.Outer.Inner.Inherited"><inheritdoc/></member>
                <member name="T:Test.Assembly.Outer"><summary>Outer.</summary></member>
                <member name="P:Test.Assembly.Other.Value"><summary>A value.</summary></member>
                <member name="T:Test.Assembly.Widgets.Button"><summary>A button.</summary></member>
                <member name="T:Test.Assembly.Other"><summary>Other.</summary></member>
              </members>
            </doc>
            """);

        return ReferenceDocument.Build(Info, source);
    }

    private static XElement Type(ReferenceDocument document, string name) =>
        document.Content.Descendants("type").Single(type => (string?)type.Attribute("name") == name);
}
