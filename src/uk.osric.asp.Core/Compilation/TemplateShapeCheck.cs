// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>Finds template-shape problems that compile cleanly and quietly change the page.</summary>
/// <remarks>
/// The C# compiler sees generated code, so a missing <c>%&gt;</c> can leave a statement open and let
/// the handler's own closing brace close it; the page compiles and the rest of the template becomes
/// conditional or literal. These checks are about the template's shape rather than its C#, and they
/// are warnings: a page that compiles is still served.
/// </remarks>
public static class TemplateShapeCheck {

    /// <summary>Checks a template for stray closers and unbalanced braces in page code.</summary>
    /// <param name="template">The template as its author wrote it.</param>
    /// <returns>The warnings, ordered by template position; empty when the shape looks sound.</returns>
    public static IReadOnlyList<PageDiagnostic> Check(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        List<PageDiagnostic> diagnostics = [];
        foreach (Token token in tokenized.Tokens) {
            if (token.Type == TokenType.Text) {
                AddStrayClosers(token, diagnostics);
            }
        }

        // A template the tokenizer refused has already been reported at its opener; counting the
        // braces in the part it did read as well would bury that error in guesses.
        if (tokenized.IsSuccess) {
            AddUnbalancedBraces(tokenized.Tokens, diagnostics);
        }

        return PageDiagnostic.SortedForDisplay(diagnostics);
    }

    private static void AddStrayClosers(Token token, List<PageDiagnostic> diagnostics) {
        int index = 0;
        while ((index = token.Value.IndexOf("%>", index, StringComparison.Ordinal)) >= 0) {
            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                TemplateLines.Advance(token.Position, token.Value[..index]),
                "TEMPLATE003: '%>' has no matching '<%'; it is written as literal text."));
            index += 2;
        }
    }

    // Braces may open in one block and close in another: that is how an if or foreach wraps markup.
    // The stack therefore runs across the whole template, and only what is still open at the end is
    // unbalanced.
    private static void AddUnbalancedBraces(IReadOnlyList<Token> tokens, List<PageDiagnostic> diagnostics) {
        Stack<TemplatePosition> openings = [];
        foreach (Token token in tokens) {
            if (token.Type != TokenType.Text) {
                Scan(token, openings, diagnostics);
            }
        }

        foreach (TemplatePosition opening in openings) {
            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                opening,
                "TEMPLATE004: '{' is never closed in page code; the handler's own closing brace may close it, silently changing what the rest of the page does."));
        }
    }

    private static void Scan(Token token, Stack<TemplatePosition> openings, List<PageDiagnostic> diagnostics) {
        string value = token.Value;
        int index = 0;
        while (index < value.Length) {
            char character = value[index];
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '/') {
                index = IndexAfterLine(value, index + 2);
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '*') {
                int end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? value.Length : end + 2;
                continue;
            }
            if (character == '"') {
                index = SkipString(value, index);
                continue;
            }
            if (character == '\'') {
                index = SkipCharacter(value, index);
                continue;
            }
            if (character == '{') {
                openings.Push(TemplateLines.Advance(token.Position, value[..index]));
            } else if (character == '}') {
                if (openings.Count > 0) {
                    _ = openings.Pop();
                } else {
                    diagnostics.Add(new PageDiagnostic(
                        PageDiagnosticSeverity.Warning,
                        TemplateLines.Advance(token.Position, value[..index]),
                        "TEMPLATE005: '}' has no matching '{'."));
                }
            }
            index++;
        }
    }

    private static int IndexAfterLine(string value, int index) {
        while (index < value.Length && value[index] is not ('\r' or '\n')) {
            index++;
        }
        return index;
    }

    private static int SkipString(string value, int start) {
        int quotes = 1;
        while (start + quotes < value.Length && value[start + quotes] == '"') {
            quotes++;
        }
        if (quotes >= 3) {
            string terminator = new('"', quotes);
            int end = value.IndexOf(terminator, start + quotes, StringComparison.Ordinal);
            return end < 0 ? value.Length : end + quotes;
        }

        bool verbatim = (start > 0 && value[start - 1] == '@')
            || (start > 1 && value[start - 1] == '$' && value[start - 2] == '@');
        bool interpolated = start > 0 && (value[start - 1] == '$'
            || (start > 1 && value[start - 1] == '@' && value[start - 2] == '$'));

        return interpolated
            ? SkipInterpolatedString(value, start, verbatim)
            : SkipPlainString(value, start, verbatim);
    }

    private static int SkipPlainString(string value, int start, bool verbatim) {
        int index = start + 1;
        while (index < value.Length) {
            if (verbatim && value[index] == '"' && index + 1 < value.Length && value[index + 1] == '"') {
                index += 2;
                continue;
            }
            if (!verbatim && value[index] == '\\') {
                index += 2;
                continue;
            }
            if (value[index] == '"') {
                return index + 1;
            }
            index++;
        }
        return value.Length;
    }

    // A hole may hold a string, a character, a comment or a nested brace. Skipping the whole
    // interpolation keeps the braces in `$"{new Item { Name = "x" }}"` out of the template's count.
    private static int SkipInterpolatedString(string value, int start, bool verbatim) {
        int index = start + 1;
        int holes = 0;
        while (index < value.Length) {
            char character = value[index];
            if (holes == 0) {
                if (character == '"') {
                    if (verbatim && index + 1 < value.Length && value[index + 1] == '"') {
                        index += 2;
                        continue;
                    }
                    return index + 1;
                }
                if (character == '{') {
                    if (index + 1 < value.Length && value[index + 1] == '{') {
                        index += 2;
                        continue;
                    }
                    holes = 1;
                    index++;
                    continue;
                }
                if (character == '}' && index + 1 < value.Length && value[index + 1] == '}') {
                    index += 2;
                    continue;
                }
                if (character == '\\' && !verbatim) {
                    index += 2;
                    continue;
                }
                index++;
                continue;
            }

            if (character == '"') {
                index = SkipString(value, index);
                continue;
            }
            if (character == '\'') {
                index = SkipCharacter(value, index);
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '/') {
                index = IndexAfterLine(value, index + 2);
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '*') {
                int end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? value.Length : end + 2;
                continue;
            }
            if (character == '{') {
                holes++;
            } else if (character == '}') {
                holes--;
            }
            index++;
        }
        return value.Length;
    }

    private static int SkipCharacter(string value, int start) {
        int index = start + 1;
        while (index < value.Length) {
            if (value[index] == '\\') {
                index += 2;
                continue;
            }
            if (value[index] == '\'') {
                return index + 1;
            }
            index++;
        }
        return value.Length;
    }
}
