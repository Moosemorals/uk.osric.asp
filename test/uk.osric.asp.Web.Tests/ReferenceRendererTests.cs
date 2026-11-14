// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>The embedded stylesheet's rewrite of a normalised document.</summary>
[TestFixture]
public sealed class ReferenceRendererTests {

    [Test]
    public void ThePageContractRendersItsIndexAndSections() {
        ReferenceLibrary library = new(AppContext.BaseDirectory, NullLogger<ReferenceLibrary>.Instance);
        string html = library.Render("sdk")!;

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("<nav class=\"type-index\" aria-label=\"Types in this assembly\">"));
            Assert.That(html, Does.Contain("<section class=\"type\">"));
            Assert.That(html, Does.Contain("id=\"t-IContext\""));
            Assert.That(html, Does.Contain("id=\"m-IContext.Request\""));
            Assert.That(html, Does.Contain("<h3"), "a member heading");
            Assert.That(html, Does.Contain(ReferenceDisplay.LearnBase), "a Learn link for a Task cref");
            Assert.That(html, Does.Not.Contain("<style"));
            Assert.That(html, Does.Not.Contain(" style="));
        });
    }

    [Test]
    public void AnInlineElementBecomesMarkupAndTextIsEscaped() {
        string html = ReferenceRenderer.Render(Fixture());

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("""<a href="/admin/reference/test#m-Type.Value">Value</a>"""));
            Assert.That(html, Does.Contain("<code>null</code>"));
            Assert.That(html, Does.Contain("<code>value</code>"));
            Assert.That(html, Does.Contain("One &lt; two."));
            Assert.That(html, Does.Contain("<ol><li>first</li></ol>"));
            Assert.That(html, Does.Contain("An unknown element&#39;s text.").Or.Contain("An unknown element's text."));
        });
    }

    [Test]
    public void AnInheritedEntryNamesItsSourceAndLinksToIt() {
        string html = ReferenceRenderer.Render(Fixture());

        Assert.Multiple(() => {
            Assert.That(html, Does.Contain("""<p class="inherited">Inherited from <a href="/admin/reference/test#m-Base.Value"><code>Base.Value</code></a>.</p>"""));
            Assert.That(html, Does.Contain("""<dt><a href="https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception"><code>InvalidOperationException</code></a></dt>"""));
            Assert.That(html, Does.Contain("When the value is wrong."));
        });
    }

    [Test]
    public void AnEntryWithNoSourceSaysItInheritsItsDocumentation() {
        XDocument document = XDocument.Parse("""
            <reference slug="test" assembly="Test" title="Test">
              <namespace name="">
                <type id="T:Test.Thing" anchor="t-Thing" name="Thing" local="Thing" display="Thing" inherits="true">
                  <member id="M:Test.Thing.Run" anchor="m-Thing.Run" kind="Method" display="Run()" inherits="true" />
                </type>
              </namespace>
            </reference>
            """);

        string html = ReferenceRenderer.Render(document);

        Assert.That(html.Split("Inherits its documentation."), Has.Length.EqualTo(3), "once for the type and once for the member");
    }

    private static XDocument Fixture() => XDocument.Parse("""
        <reference slug="test" assembly="Test" title="Test">
          <namespace name="">
            <type id="T:Test.Type" anchor="t-Type" name="Type" local="Type" display="Type">
              <summary>Uses <see cref="P:Test.Type.Value" href="/admin/reference/test#m-Type.Value" text="Value" />,
                <see langword="null" /> and <paramref name="value" />.</summary>
              <remarks>
                <para>One &lt; two.</para>
                <list type="number"><item><description>first</description></item></list>
                <unknown>An unknown element's text.</unknown>
              </remarks>
              <member id="P:Test.Type.Value" anchor="m-Type.Value" kind="Property" display="Value"
                      inherited-from="M:Test.Base.Value" inherited-display="Base.Value"
                      inherited-href="/admin/reference/test#m-Base.Value">
                <summary>Inherited.</summary>
                <exception cref="T:System.InvalidOperationException" text="InvalidOperationException"
                           href="https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception">When the value is wrong.</exception>
              </member>
            </type>
          </namespace>
        </reference>
        """);
}
