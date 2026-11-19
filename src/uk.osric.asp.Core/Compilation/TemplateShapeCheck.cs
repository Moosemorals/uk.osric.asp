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

    /// <summary>Checks a template for stray closers, unbalanced braces and blocks with nothing in them.</summary>
    /// <param name="template">The template as its author wrote it.</param>
    /// <returns>The warnings, ordered by template position; empty when the shape looks sound.</returns>
    public static IReadOnlyList<PageDiagnostic> Check(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        List<PageDiagnostic> diagnostics = [];

        // The text that follows a block cut short inside a literal continues that literal, so its
        // first closer is the one the author meant. Which runs those are is decided before the runs
        // are read, because that is the check that finds them.
        HashSet<int> closesABlock = [];
        if (tokenized.IsSuccess) {
            AddDeadBlocks(tokenized.Tokens, diagnostics);
            AddTruncatedBlocks(tokenized.Tokens, diagnostics, closesABlock);
            AddUnbalancedBraces(tokenized.Tokens, diagnostics);
        }

        for (int i = 0; i < tokenized.Tokens.Count; i++) {
            if (tokenized.Tokens[i].Type == TokenType.Text) {
                AddStrayClosers(tokenized.Tokens[i], closesABlock.Contains(i), diagnostics);
            }
        }

        return PageDiagnostic.SortedForDisplay(diagnostics);
    }

    private static void AddStrayClosers(Token token, bool firstCloserEndsABlock, List<PageDiagnostic> diagnostics) {
        int index = 0;
        while ((index = token.Value.IndexOf("%>", index, StringComparison.Ordinal)) >= 0) {
            if (firstCloserEndsABlock) {
                firstCloserEndsABlock = false;
                index += 2;
                continue;
            }

            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                token.Position.Advance(token.Value[..index]),
                "TEMPLATE003: '%>' has no matching '<%'; it is written as literal text."));
            index += 2;
        }
    }

    // A block that holds no code is decisive: it is what makes a template a page, or — as a
    // declaration — what makes it a library. Neither is visible in the template, so it is said out
    // loud. A comment gets its own warning, because the fix is different: `<%#` is the note that
    // leaves the template as it was.
    private static void AddDeadBlocks(IReadOnlyList<Token> tokens, List<PageDiagnostic> diagnostics) {
        for (int i = 0; i < tokens.Count; i++) {
            Token token = tokens[i];
            if (token.Type is not (TokenType.Code or TokenType.Declaration)) {
                continue;
            }

            if (!HoldsOnlyTrivia(token.Value, out bool hasComment)) {
                continue;
            }

            bool alone = IsTheOnlyCode(tokens, i);
            string consequence = alone
                ? token.Type is TokenType.Declaration
                    ? " It is what makes this template a library, which is never served."
                    : " It is what makes this template a page."
                : string.Empty;
            string message = hasComment
                ? "TEMPLATE007: this block holds only a comment; a note that should leave the template as it was is written as '<%# ... %>'."
                : "TEMPLATE006: this block holds no code.";

            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                token.OpenerPosition ?? token.Position,
                message + consequence));
        }
    }

    // Whitespace and comments are trivia lexically, which is all the kind rule ever asked about them.
    // A lone ';' is not: only the compiler can call a statement empty, so it stays code.
    private static bool HoldsOnlyTrivia(string value, out bool hasComment) {
        hasComment = false;
        int index = 0;
        while (index < value.Length) {
            char character = value[index];
            if (char.IsWhiteSpace(character)) {
                index++;
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '/') {
                hasComment = true;
                index = IndexAfterLine(value, index + 2);
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '*') {
                int end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0) {
                    return false;
                }

                hasComment = true;
                index = end + 2;
                continue;
            }

            return false;
        }

        return true;
    }

    // "Only" is about the kind rule, not about the author's intent: no other block decides what this
    // template is, so this one does.
    private static bool IsTheOnlyCode(IReadOnlyList<Token> tokens, int index) {
        for (int i = 0; i < tokens.Count; i++) {
            if (i != index && tokens[i].Type is not (TokenType.Text or TokenType.Comment)) {
                return false;
            }
        }

        return true;
    }

    // A block ends at the first '%>', so a literal or a comment still open when the block's end
    // arrives is a block cut short. Blaming the text that follows names the symptom; the author has
    // to be told where the cause is and what to write instead.
    private static void AddTruncatedBlocks(
        IReadOnlyList<Token> tokens,
        List<PageDiagnostic> diagnostics,
        HashSet<int> closesABlock) {
        for (int i = 0; i < tokens.Count; i++) {
            Token token = tokens[i];
            if (token.Type is not (TokenType.Code or TokenType.Declaration)) {
                continue;
            }

            if (!TryFindUnclosed(token.Value, out int offset, out bool inComment)) {
                continue;
            }

            string message = inComment
                ? "TEMPLATE008: the block ends inside a comment; close it with '*/'."
                : "TEMPLATE008: the block ends inside a literal; a block ends at the first '%>', so write the character as '\\u003e', or join '\"%\"' and '\">\"'.";
            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                token.Position.Advance(token.Value[..offset]),
                message));

            if (i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Text) {
                _ = closesABlock.Add(i + 1);
            }
        }
    }

    private static bool TryFindUnclosed(string value, out int offset, out bool inComment) {
        offset = 0;
        inComment = false;
        int index = 0;
        while (index < value.Length) {
            char character = value[index];
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '/') {
                index = IndexAfterLine(value, index + 2);
                continue;
            }
            if (character == '/' && index + 1 < value.Length && value[index + 1] == '*') {
                int end = value.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0) {
                    offset = index;
                    inComment = true;
                    return true;
                }

                index = end + 2;
                continue;
            }
            if (character == '"') {
                Skip skip = SkipString(value, index);
                if (!skip.Closed) {
                    offset = index;
                    return true;
                }

                index = skip.End;
                continue;
            }
            if (character == '\'') {
                Skip skip = SkipCharacter(value, index);
                if (!skip.Closed) {
                    offset = index;
                    return true;
                }

                index = skip.End;
                continue;
            }

            index++;
        }

        return false;
    }

    // Braces may open in one block and close in another: that is how an if or foreach wraps markup.
    // The stack therefore runs across the whole template, and only what is still open at the end is
    // unbalanced. A comment holds prose rather than code, so its braces are not counted either way.
    private static void AddUnbalancedBraces(IReadOnlyList<Token> tokens, List<PageDiagnostic> diagnostics) {
        Stack<TemplatePosition> openings = [];
        foreach (Token token in tokens) {
            if (token.Type is not (TokenType.Text or TokenType.Comment)) {
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
                index = SkipString(value, index).End;
                continue;
            }
            if (character == '\'') {
                index = SkipCharacter(value, index).End;
                continue;
            }
            if (character == '{') {
                openings.Push(token.Position.Advance(value[..index]));
            } else if (character == '}') {
                if (openings.Count > 0) {
                    _ = openings.Pop();
                } else {
                    diagnostics.Add(new PageDiagnostic(
                        PageDiagnosticSeverity.Warning,
                        token.Position.Advance(value[..index]),
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

    // A literal answers where it ended and whether it found its terminator: a block's end can arrive
    // inside one, and that is the difference between a closed string at the last character and a
    // string that never closed.
    private static Skip SkipString(string value, int start) {
        int quotes = 1;
        while (start + quotes < value.Length && value[start + quotes] == '"') {
            quotes++;
        }
        if (quotes >= 3) {
            string terminator = new('"', quotes);
            int end = value.IndexOf(terminator, start + quotes, StringComparison.Ordinal);
            return end < 0 ? new Skip(value.Length, Closed: false) : new Skip(end + quotes, Closed: true);
        }

        bool verbatim = (start > 0 && value[start - 1] == '@')
            || (start > 1 && value[start - 1] == '$' && value[start - 2] == '@');
        bool interpolated = start > 0 && (value[start - 1] == '$'
            || (start > 1 && value[start - 1] == '@' && value[start - 2] == '$'));

        return interpolated
            ? SkipInterpolatedString(value, start, verbatim)
            : SkipPlainString(value, start, verbatim);
    }

    private static Skip SkipPlainString(string value, int start, bool verbatim) {
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
                return new Skip(index + 1, Closed: true);
            }
            index++;
        }
        return new Skip(value.Length, Closed: false);
    }

    // A hole may hold a string, a character, a comment or a nested brace. Skipping the whole
    // interpolation keeps the braces in `$"{new Item { Name = "x" }}"` out of the template's count.
    private static Skip SkipInterpolatedString(string value, int start, bool verbatim) {
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
                    return new Skip(index + 1, Closed: true);
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
                index = SkipString(value, index).End;
                continue;
            }
            if (character == '\'') {
                index = SkipCharacter(value, index).End;
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
        return new Skip(value.Length, Closed: false);
    }

    private static Skip SkipCharacter(string value, int start) {
        int index = start + 1;
        while (index < value.Length) {
            if (value[index] == '\\') {
                index += 2;
                continue;
            }
            if (value[index] == '\'') {
                return new Skip(index + 1, Closed: true);
            }
            index++;
        }
        return new Skip(value.Length, Closed: false);
    }
}

// Where a literal or comment skip ended, and whether it found what it was looking for.
internal readonly record struct Skip(int End, bool Closed);
