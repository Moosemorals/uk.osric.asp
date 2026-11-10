// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant.Shell.Applets;
using uk.osric.asp.Web.Editor;

/// <summary>The kernel: it lexes a line, finds the applet, parses, expands, runs it and wraps the answer.</summary>
/// <remarks>
/// The whole of the shell's own behaviour is here, so an applet never parses its own command line and
/// never builds the tool result. It also owns the one rule a line has to obey that a POSIX shell would
/// not impose: no operators, yet. That rule lives here rather than in the lexer because it is
/// temporary — a pipeline is a kernel stage, and the lexer already hands it the tokens.
/// </remarks>
internal static class ShellRunner {

    private static readonly ShellRegistry s_registry = new([
        new CatApplet(),
        new DiffApplet(),
        new FindApplet(),
        new GrepApplet(),
        new HeadApplet(),
        new HelpApplet(),
        new LsApplet(),
        new StatApplet(),
        new TailApplet(),
        new WcApplet(),
    ]);

    /// <summary>The table of applets, which <c>help</c> reads.</summary>
    public static ShellRegistry Registry => s_registry;

    /// <summary>Runs a line. Every command the shell answers is an applet, so this always answers.</summary>
    /// <param name="text">The command text.</param>
    /// <param name="pages">The editor behind the store.</param>
    /// <param name="paths">The path policy.</param>
    /// <param name="options">The assistant's options, for the result budget.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public static async Task<ShellOutcome> RunAsync(
        string text,
        PageEditor pages,
        PagePathPolicy paths,
        AssistantOptions options,
        CancellationToken cancellationToken) {
        ShellLexResult lexed = ShellLine.Lex(text);
        if (lexed.Error is not null) {
            return ShellOutcome.Refused(lexed.Error, "Shell command refused");
        }
        if (lexed.Words.Count == 0) {
            return ShellOutcome.Refused(
                "There is no command to run. Run help for the commands the shell answers.",
                "Shell command refused");
        }

        ShellApplet? applet = s_registry.Find(lexed.Words[0].Text);
        if (applet is null) {
            return ShellOutcome.Refused(
                $"The shell command {MessageText.Sanitize(lexed.Words[0].Text)} is not supported. Run help for the commands it answers.",
                "Shell command refused");
        }

        if (lexed.Operator is { } unquoted) {
            return ShellOutcome.Refused(OperatorRefusal(unquoted.Character), "Shell command refused");
        }

        ShellMatchResult matched = applet.Syntax.Match([.. lexed.Words.Skip(1)], applet.Usage);
        if (matched.Line is null) {
            return ShellOutcome.Refused(matched.Error!, $"{applet.Name}: refused");
        }

        PageStoreView store = new(pages, paths);
        (IReadOnlyList<ShellPath> expanded, ShellResult? refusal) =
            await new ShellExpansion(paths).ExpandAsync(matched.Line.Arguments, store, cancellationToken);
        if (refusal is not null) {
            return ShellOutcome.Refused(refusal.Error!, refusal.Summary);
        }

        ShellOutput output = new();
        ShellContext context = new(matched.Line, expanded, store, output, s_registry);
        ShellResult result = await applet.RunAsync(matched.Line, context, cancellationToken);
        if (!result.IsSuccess) {
            return ShellOutcome.Refused(result.Error!, result.Summary);
        }

        string answer = output.Text;
        bool truncated = output.Truncated;

        int budget = options.ToolResultBudget;
        if (answer.Length > budget) {
            answer = answer[..budget] + $"\n[the answer was cut at {budget} characters; ask for less]";
            truncated = true;
        }

        return ShellOutcome.Answered(answer, truncated, result.Summary);
    }

    /// <summary>Why an unquoted operator is refused for now.</summary>
    /// <param name="character">The operator the line carried.</param>
    /// <remarks>It says "yet" on purpose: pipelines and redirections are the next thing this grows.</remarks>
    public static string OperatorRefusal(char character) =>
        $"There are no pipes, redirects, separators or background jobs yet, so {Display(character)} is refused. Run help for the accepted form.";

    private static string Display(char character) => character is '\n' or '\r' ? "a newline" : $"'{character}'";
}
