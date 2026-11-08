// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Acornima;
using uk.osric.asp.Core.Compilation;

/// <summary>Parses JavaScript as an ECMAScript module and reports its syntax errors in page positions.</summary>
/// <remarks>
/// Syntax only: the grammar is checked and nothing is executed, imported or resolved. A page is
/// parsed as a module because that is the site's convention for a script page, so a top-level
/// <c>return</c> is an error and a duplicated top-level declaration is a redeclaration.
/// </remarks>
public static class JavaScriptValidator {

    // A botched edit usually leaves a brace or a bracket open; the parser's own error is then often
    // hundreds of lines away, at the next token it cannot use. The cap keeps a pathological page from
    // filling the tool result with hints, and the earliest hints are the ones nearest the edit.
    private const int HintLimit = 20;

    // A template literal's ${ and the } that closes it are punctuators too, so the substitution is
    // tracked as an opener of its own: without it a substitution's } would close a real brace outside
    // the literal and hide the unclosed opener the edit actually left behind.
    private const string TemplateSubstitution = "${";

    private static readonly string[] s_openers = ["{", "(", "["];

    /// <summary>Parses a script as an ECMAScript module and returns positioned syntax diagnostics.</summary>
    /// <param name="script">The JavaScript to parse.</param>
    /// <returns>The parser's error, if any, and up to twenty unclosed-opener hints, ordered for
    /// display. Empty when the script parses.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<PageDiagnostic> Validate(string script) {
        ArgumentNullException.ThrowIfNull(script);

        List<(string Opener, TemplatePosition Position)> open = [];
        ParserOptions options = new() {
            EcmaVersion = EcmaVersion.Latest,
            Tolerant = false,
            OnToken = (in Token token) => Track(token, open),
        };

        PageDiagnostic? error;
        try {
            _ = new Parser(options).ParseModule(script, "page.js");
            error = null;
        } catch (ParseErrorException exception) {
            error = SyntaxError(exception.Error);
        }

        if (error is null) {
            return [];
        }

        List<PageDiagnostic> diagnostics = [error];
        diagnostics.AddRange(open
            .Where(bracket => bracket.Opener != TemplateSubstitution)
            .Take(HintLimit)
            .Select(bracket => new PageDiagnostic(
                PageDiagnosticSeverity.Warning,
                bracket.Position,
                $"SCRIPT002: unclosed '{bracket.Opener}'.")));
        return PageDiagnostic.SortedForDisplay(diagnostics);
    }

    // Tracks the openers the parser has passed without closing them. A closer only pops the top of
    // the stack, so an unmatched closer cannot remove an opener it does not belong to.
    private static void Track(in Token token, List<(string Opener, TemplatePosition Position)> open) {
        if (token.Kind is not TokenKind.Punctuator || token.Value is not string punctuator) {
            return;
        }

        if (s_openers.Contains(punctuator, StringComparer.Ordinal) || punctuator == TemplateSubstitution) {
            Position start = token.Location.Start;
            open.Add((punctuator, new TemplatePosition(start.Line, start.Column + 1)));
            return;
        }

        // An unmatched closer is ignored: the parser reports it, and guessing would make the hint list
        // disagree with the error beside it.
        if (open.Count > 0 && Closer(open[^1].Opener) == punctuator) {
            open.RemoveAt(open.Count - 1);
        }
    }

    private static string Closer(string opener) => opener switch {
        "(" => ")",
        "[" => "]",
        _ => "}",
    };

    private static PageDiagnostic SyntaxError(ParseError error) {
        TemplatePosition? position = error.IsPositionDefined
            ? new TemplatePosition(error.LineNumber, error.Column + 1)
            : null;
        return new PageDiagnostic(
            PageDiagnosticSeverity.Error,
            position,
            $"SCRIPT001: {error.Code}: {error.Description}");
    }
}
