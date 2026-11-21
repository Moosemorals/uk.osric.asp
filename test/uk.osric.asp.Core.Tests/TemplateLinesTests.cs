// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class TemplateLinesTests {

    [TestCase("", 1)]
    [TestCase("a", 1)]
    [TestCase("a\n", 2)]
    [TestCase("a\nb", 2)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\rb", 2)]
    [TestCase("\n\n", 3)]
    [TestCase("a\r\n\r\n", 3)]
    public void CountMatchesTheBreaksTheTokenizerSees(string input, int expected) {
        Assert.That(TemplateLines.Count(input), Is.EqualTo(expected));
    }

    [Test]
    public void AMapsEveryOffsetTheWayAWalkFromTheStartDoes() {
        const string input = "a\r\nbb\rccc\n\nd\r\n";

        TemplateLineMap map = TemplateLines.Map(input);

        for (int offset = 0; offset <= input.Length; offset++) {
            Assert.That(map.PositionOf(offset), Is.EqualTo(PlaceFromTheStart(input, offset)), $"offset {offset}");
        }
    }

    [Test]
    public void CountRefusesAMissingTemplate() {
        Assert.That(() => TemplateLines.Count(null!), Throws.ArgumentNullException);
    }

    // The rule the map has to keep, written the slow way: before the map, this walk was the answer.
    private static TemplatePosition PlaceFromTheStart(string input, int offset) {
        int line = 1;
        int lineStart = 0;

        for (int index = 0; index < offset; index++) {
            if (input[index] == '\r' && index + 1 < offset && input[index + 1] == '\n') {
                index++;
            }

            if (input[index] is '\r' or '\n') {
                line++;
                lineStart = index + 1;
            }
        }

        return new TemplatePosition(line, offset - lineStart + 1);
    }
}
