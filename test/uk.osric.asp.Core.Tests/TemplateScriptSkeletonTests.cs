// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class TemplateScriptSkeletonTests {

    [Test]
    public void TextWithoutTemplateConstructsIsReturnedUnchanged() {
        const string template = "import { x } from \"./x.js\";\n\nexport function run() {\n    return x;\n}\n";

        ScriptSkeletonResult result = TemplateScriptSkeleton.Create(template);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Source, Is.EqualTo(template));
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void CodeAndDeclarationBlocksAreBlankedWithoutMovingTheLinesBelow() {
        const string template = "<% var x = 1; %>\n<%! class Amount { } %>\nconst answer = x;";

        ScriptSkeletonResult result = TemplateScriptSkeleton.Create(template);

        string[] templateLines = Lines(template);
        string[] lines = Lines(result.Source);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Is.EqualTo(new string(' ', templateLines[0].Length)));
            Assert.That(lines[1], Is.EqualTo(new string(' ', templateLines[1].Length)));
            Assert.That(lines[2], Is.EqualTo("const answer = x;"));
        });
    }

    [Test]
    public void AWriteBlockBecomesAnExpressionInPlace() {
        const string template = "const label = \"<%= ctx.Path %>\";";

        string source = TemplateScriptSkeleton.Create(template).Source;

        Assert.Multiple(() => {
            Assert.That(source, Is.EqualTo("const label = \"" + "0" + new string(' ', "<%= ctx.Path %>".Length - 1) + "\";"));
            Assert.That(source, Has.Length.EqualTo(template.Length));
        });
    }

    [Test]
    public void AWriteBlockSpanningLinesKeepsTheLineBreaks() {
        const string template = "const label = <%= \n    \"a\" +\n    \"b\"\n%>;";

        string source = TemplateScriptSkeleton.Create(template).Source;

        Assert.Multiple(() => {
            Assert.That(source.Split('\n'), Has.Length.EqualTo(template.Split('\n').Length));
            Assert.That(source, Does.StartWith("const label = 0"));
            Assert.That(source, Does.EndWith("  ;"));
        });
    }

    [TestCase("\r\n")]
    [TestCase("\n")]
    [TestCase("\r")]
    public void EveryLineBreakSurvivesTheSkeleton(string lineBreak) {
        string template = string.Join(lineBreak, "<% one %>", "<%= two %>", "three");

        string source = TemplateScriptSkeleton.Create(template).Source;

        Assert.Multiple(() => {
            Assert.That(Lines(source), Is.EqualTo<string[]>(["         ", "0         ", "three"]));
            Assert.That(source, Has.Length.EqualTo(template.Length));
        });
    }

    [Test]
    public void AMalformedTemplateReturnsTheTokenizerErrorAndNoSource() {
        ScriptSkeletonResult result = TemplateScriptSkeleton.Create("const a = 1;\n<% if (true) {");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Source, Is.Empty);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Message, Does.StartWith("TEMPLATE001:"));
            Assert.That(result.Diagnostics[0].Position, Is.EqualTo(new TemplatePosition(2, 1)));
        });
    }

    [Test]
    public void TextAfterSeveralTemplateBlocksKeepsItsPagePosition() {
        string template = "<%= a %>\r\n<% b %>\r\n    <%! c %>\r\nconst here = 1;";

        ScriptSkeletonResult result = TemplateScriptSkeleton.Create(template);

        int offset = result.Source.IndexOf("const here", StringComparison.Ordinal);
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(TemplateLines.PositionOf(result.Source, offset), Is.EqualTo(new TemplatePosition(4, 1)));
            Assert.That(result.Source[offset..], Is.EqualTo("const here = 1;"));
        });
    }

    private static string[] Lines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
}
