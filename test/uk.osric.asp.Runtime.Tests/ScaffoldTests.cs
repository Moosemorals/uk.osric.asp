// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using System.Globalization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>The scaffold the generator writes compiles warning-free under nullable analysis.</summary>
/// <remarks>
/// <see cref="PageCompiler"/> drops a warning that has no template position, so a warning raised by
/// generated code would reach nobody. The scaffold is nullable-oblivious for exactly that reason,
/// and these tests are what keep it so: a Roslyn release that starts warning about something the
/// wrapper does fails the build here, rather than quietly taking a page's line with it.
/// </remarks>
[TestFixture]
public sealed class ScaffoldTests {

    private static readonly PageCompiler Compiler = new();

    // The shapes a template can have: only text, only a write, only code, and a declaration.
    [TestCase("literal text only")]
    [TestCase("<%= ctx.Path %>")]
    [TestCase("<% int value = 1; ctx.Write(value.ToString()); %>")]
    [TestCase("<%! sealed class Amount { public static int Value => 1; } %>")]
    [TestCase("<%! using System.Globalization; %><%= 1.5m.ToString(\"N2\", CultureInfo.InvariantCulture) %>")]
    public void TheScaffoldCompilesWithoutWarningsUnderNullableEnable(string template) {
        CompileResult result = Compiler.Compile(TestTemplates.Generate(template), []);

        Assert.Multiple(() => {
            Assert.That(result.Diagnostics, Is.Empty, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.That(result.Image, Is.Not.Null);
        });
    }

    [Test]
    public void EveryMappedRegionIsNullableEnabled() {
        const string template = "before<% int value = 1; %>middle<%= ctx.Path %>after<%! sealed class Amount { } %>";

        foreach ((string line, bool mapped, string nullable) in Walk(Generate(template), TemplateLines.Count(template))) {
            if (mapped && !IsDirective(line)) {
                Assert.That(nullable, Is.EqualTo("enable"), $"the mapped line '{line}' is not nullable-enabled");
            }
        }
    }

    [Test]
    public void EveryGeneratedLineIsNullableOblivious() {
        const string template = "before<% int value = 1; %>middle<%= ctx.Path %>after<%! sealed class Amount { } %>";

        foreach ((string line, bool mapped, string nullable) in Walk(Generate(template), TemplateLines.Count(template))) {
            if (!mapped && !IsDirective(line)) {
                Assert.That(nullable, Is.EqualTo("disable"), $"the generated line '{line}' is not nullable-oblivious");
            }
        }
    }

    private static string Generate(string template) {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate(template);

        Assert.That(result.Source, Is.Not.Null, string.Join(Environment.NewLine, result.Errors));
        return result.Source!.Text;
    }

    // Each line with the mapping and the nullable context in force at it. The postlude's `#line`
    // maps one line past the template so an unclosed block is positioned; it is generated code, and
    // the rule it follows is the generated one. A directive changes the context for what follows, so
    // it reports the context it sets.
    private static IEnumerable<(string Line, bool Mapped, string Nullable)> Walk(string source, int templateLines) {
        bool mapped = false;
        string nullable = "disable";

        foreach (string line in source.Split(Environment.NewLine)) {
            if (line.StartsWith("#nullable ", StringComparison.Ordinal)) {
                nullable = line["#nullable ".Length..];
            } else if (line.StartsWith("#line default", StringComparison.Ordinal)) {
                mapped = false;
            } else if (line.StartsWith("#line ", StringComparison.Ordinal)) {
                int number = int.Parse(line.Split(' ', 3)[1], CultureInfo.InvariantCulture);
                mapped = number <= templateLines;
            }

            yield return (line, mapped, nullable);
        }
    }

    private static bool IsDirective(string line) => line.StartsWith('#');
}
