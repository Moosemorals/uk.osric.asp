// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using ReferenceCorpus;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Documentation;

/// <summary>The C# renderer's rewrite of the corpus the build emits.</summary>
[TestFixture]
public sealed class ReferenceHtmlTests {

    [Test]
    public void ThePageContractRendersItsIndexAndSections() {
        ReferenceLibrary library = new(AppContext.BaseDirectory, NullLogger<ReferenceLibrary>.Instance);
        ReferenceNamespace space = library.Find("sdk")!.Corpus.Namespaces.Single();
        string html = ReferenceHtml.Namespace(space);

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<nav class=\"type-index\" aria-label=\"Types in this namespace\">"));
            Assert.That(html, Does.Contain("<details class=\"type\" id=\"t-IContext\">"));
            Assert.That(html, Does.Contain("id=\"m-IContext.Request\""));
            Assert.That(html, Does.Contain("<h3 class=\"member-group\">Properties</h3>"), "a member group");
            Assert.That(html, Does.Contain("<h3 class=\"member-group\">Methods</h3>"), "a member group");
            Assert.That(html, Does.Contain("<h4 id=\"m-IContext.Request\""), "a member heading");
            Assert.That(html, Does.Not.Contain("<span class=\"kind\">"), "no per-member kind tag");
            Assert.That(html, Does.Contain("tok-keyword"), "a coloured declaration");
            Assert.That(html, Does.Contain("<a href=\"/admin/reference/sdk#t-PageRequest\">"), "a return type links to its type");
            Assert.That(html, Does.Contain("id=\"m-IContext.Log-string-param-message\""), "a parameter has an anchor");
            Assert.That(html, Does.Contain("href=\"#m-IContext.Log-string-param-message\""), "a parameter name links to it");
            Assert.That(html, Does.Contain("id=\"m-IContext.Log-string-param-message-ref\""), "the signature name is the back-link target");
            Assert.That(html, Does.Contain("href=\"#m-IContext.Log-string-param-message-ref\""), "the row links back to the signature");
            Assert.That(html, Does.Contain("aria-label=\"Back to the signature\""), "the back link says what it does");
            Assert.That(html, Does.Contain(ReferenceDisplay.LearnBase), "a Learn link for a Task cref");
            Assert.That(html, Does.Not.Contain("<style"));
            Assert.That(html, Does.Not.Contain(" style="));
        });
    }

    [Test]
    public void ADeclarationCarriesItsAccessibilityReturnTypeAndKind() {
        ReferenceLibrary library = new(AppContext.BaseDirectory, NullLogger<ReferenceLibrary>.Instance);
        ReferenceNamespace space = library.Find("sdk")!.Corpus.Namespaces.Single();
        ReferenceType context = space.Types.Single(type => type.Name == "IContext");
        ReferenceMember flush = context.Members.Single(member =>
            member.Declaration.Text.Contains("FlushAsync", StringComparison.Ordinal));
        ReferenceMember log = context.Members.Single(member =>
            member.Declaration.Text.Contains("Log(string message)", StringComparison.Ordinal));

        ReferenceType request = space.Types.Single(type => type.Name == "HttpFetchRequest");
        ReferenceMember url = request.Members.Single(member =>
            member.Declaration.Text.StartsWith("public string Url", StringComparison.Ordinal));

        Assert.Multiple(() => {
            Assert.That(context.Declaration.Text, Is.EqualTo("public interface IContext"));
            Assert.That(flush.Declaration.Text, Is.EqualTo("public Task FlushAsync()"));
            Assert.That(flush.Declaration.Tokens.Single(token => token.Text == "Task").Href,
                Does.StartWith(ReferenceDisplay.LearnBase), "a BCL return type links to Learn");
            Assert.That(log.Declaration.Tokens.Single(token => token.Kind == "param" && token.Text == "message").Href,
                Is.EqualTo("#m-IContext.Log-string-param-message"), "a parameter name links to its entry");
            Assert.That(url.Declaration.Text, Is.EqualTo("public string Url { get; init; }"),
                "a class member does not repeat the access word it already carries");
        });
    }

    [Test]
    public void AnInlineElementBecomesMarkupAndTextIsEscaped() {
        ReferenceDoc summary = new("text", [
            new ReferenceNode("text", Text: "Uses "),
            new ReferenceNode("link", Href: "/admin/reference/test#m-Type.Value",
                Children: [new ReferenceNode("text", Text: "Value")]),
            new ReferenceNode("text", Text: " and "),
            new ReferenceNode("code", Children: [new ReferenceNode("text", Text: "null")]),
            new ReferenceNode("para", Children: [new ReferenceNode("text", Text: "One < two.")]),
            new ReferenceNode("list", Ordered: true, Children: [
                new ReferenceNode("item", Children: [
                    new ReferenceNode("description", Children: [new ReferenceNode("text", Text: "first")]),
                ]),
            ]),
        ]);

        string html = ReferenceHtml.Namespace(Space(Type(summary: summary)));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("""<a href="/admin/reference/test#m-Type.Value">Value</a>"""));
            Assert.That(html, Does.Contain("<code>null</code>"));
            Assert.That(html, Does.Contain("One &lt; two."));
            Assert.That(html, Does.Contain("<ol><li>first</li></ol>"));
        });
    }

    [Test]
    public void AnInheritedEntryNamesItsSourceAndLinksToIt() {
        ReferenceMember member = Member(
            summary: ReferenceDoc.Empty,
            inherited: new ReferenceInherited(
                "M:Test.Base.Value",
                "Base.Value",
                "/admin/reference/test#m-Base.Value",
                Unknown: false),
            exceptions: [
                new ReferenceExceptionDoc(
                    "InvalidOperationException",
                    "https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception",
                    new ReferenceDoc("When the value is wrong.", [new ReferenceNode("text", Text: "When the value is wrong.")])),
            ]);

        string html = ReferenceHtml.Namespace(Space(Type(members: [member])));

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("""<p class="inherited">Inherited from <a href="/admin/reference/test#m-Base.Value"><code>Base.Value</code></a>.</p>"""));
            Assert.That(html, Does.Contain("""<dt><a href="https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception"><code>InvalidOperationException</code></a></dt>"""));
            Assert.That(html, Does.Contain("When the value is wrong."));
        });
    }

    [Test]
    public void AnEntryWithNoSourceSaysItInheritsItsDocumentation() {
        ReferenceInherited unknown = new(null, null, null, Unknown: true);
        string html = ReferenceHtml.Namespace(Space(Type(inherited: unknown, members: [Member(ReferenceDoc.Empty, unknown)])));

        Assert.That(html.Split("Inherits its documentation."), Has.Length.EqualTo(3), "once for the type and once for the member");
    }

    private static ReferenceNamespace Space(params ReferenceType[] types) =>
        new("Test", types.Length, types);

    private static ReferenceType Type(
        ReferenceDoc? summary = null,
        ReferenceInherited? inherited = null,
        IReadOnlyList<ReferenceMember>? members = null) =>
        new(
            "T:Test.Type",
            "t-Type",
            "Type",
            "Type",
            "class",
            new ReferenceDeclaration("public class Type", [
                new ReferenceToken("keyword", "public"),
                new ReferenceToken("text", " "),
                new ReferenceToken("keyword", "class"),
                new ReferenceToken("text", " "),
                new ReferenceToken("type", "Type"),
            ]),
            summary ?? ReferenceDoc.Empty,
            ReferenceDoc.Empty,
            [],
            inherited,
            members ?? []);

    private static ReferenceMember Member(
        ReferenceDoc summary,
        ReferenceInherited? inherited = null,
        IReadOnlyList<ReferenceExceptionDoc>? exceptions = null) =>
        new(
            "P:Test.Type.Value",
            "m-Type.Value",
            "Property",
            "Value",
            new ReferenceDeclaration("public string Value { get; }", [
                new ReferenceToken("keyword", "public"),
                new ReferenceToken("text", " "),
                new ReferenceToken("keyword", "string"),
                new ReferenceToken("text", " "),
                new ReferenceToken("member", "Value"),
            ]),
            summary,
            ReferenceDoc.Empty,
            [],
            [],
            ReferenceDoc.Empty,
            ReferenceDoc.Empty,
            exceptions ?? [],
            inherited);
}
