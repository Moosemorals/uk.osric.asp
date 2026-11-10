// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;

/// <summary>The commands, and one command's syntax, read from the applets themselves.</summary>
internal sealed class HelpApplet : ShellApplet {

    private const string Preface =
        "A read-only shell over the page store, which is a tree of keys: /notes is a page, and /vim\n"
        + "is a folder when a key lives beneath it. A key is a key, never a host path.\n"
        + "One command per line: there are no pipes, redirects, separators, globbing or expansion.\n"
        + "Quote a word with ' ' or \" \" to keep its spaces and punctuation literal, or escape one\n"
        + "character with a backslash: grep 'a|b' is a query, grep a|b is refused.";

    public override string Name => "help";

    public override string Usage => "help [command]";

    public override string Description => "Print the commands, or one command's usage and syntax.";

    public override ShellSyntax Syntax => new(
        [],
        [],
        [new ShellOperand("command", ShellOperandKind.Text, 0, 1)]);

    public override ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        if (line.Arguments.Count == 0) {
            context.Output.Raw(Preface);
            context.Output.Line(string.Empty);
            foreach (ShellApplet applet in context.Registry.All) {
                context.Output.Line($"{applet.Usage}: {applet.Description}");
            }

            return ValueTask.FromResult(ShellResult.Ok("Shell help"));
        }

        string word = line.Arguments[0].Word.Text;
        ShellApplet? named = context.Registry.Find(word);
        if (named is null) {
            return ValueTask.FromResult(ShellResult.Refused(
                $"The command {MessageText.Sanitize(word)} is not supported. Run help for the commands it answers.",
                "Shell help: not a command"));
        }

        context.Output.Line($"{named.Usage}: {named.Description}");
        if (named.Notes is { } notes) {
            context.Output.Raw(notes);
        }

        return ValueTask.FromResult(ShellResult.Ok($"Shell help: {MessageText.Sanitize(word)}"));
    }
}
