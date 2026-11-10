// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant;

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
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        if (line.Arguments.Count == 0) {
            context.Output.Raw(Preface);
            context.Output.Line(string.Empty);
            foreach (string name in Names(context)) {
                context.Output.Line(Describe(name, context));
            }

            return ValueTask.FromResult(ShellResult.Ok("Shell help"));
        }

        string word = line.Arguments[0].Word.Text;
        ShellApplet? applet = context.Registry.Find(word);
        if (applet is not null) {
            context.Output.Line($"{applet.Usage}: {applet.Description}");
            if (applet.Notes is { } notes) {
                context.Output.Raw(notes);
            }

            return ValueTask.FromResult(ShellResult.Ok($"Shell help: {MessageText.Sanitize(word)}"));
        }

        // A command that has not moved to an applet yet is still described, by the table it left
        // behind, so help is never less than the whole shell.
        if (ShellHelp.TryGet(word, out ShellHelpEntry? entry) && entry is not null) {
            context.Output.Raw(ShellHelp.Text(entry));
            return ValueTask.FromResult(ShellResult.Ok($"Shell help: {MessageText.Sanitize(word)}"));
        }

        return ValueTask.FromResult(ShellResult.Refused(
            $"The command {MessageText.Sanitize(word)} is not supported. Run help for the commands it answers.",
            "Shell help: not a command"));
    }

    // Every command the shell answers, in ordinal order, whichever table describes it: the applet
    // registry for the commands that have moved, and the older table for the rest.
    private static IEnumerable<string> Names(ShellContext context) => VirtualShellParser.Commands
        .Union(context.Registry.All.Select(applet => applet.Name), StringComparer.Ordinal)
        .Order(StringComparer.Ordinal);

    private static string Describe(string name, ShellContext context) => context.Registry.Find(name) is { } applet
        ? $"{applet.Usage}: {applet.Description}"
        : ShellHelp.TryGet(name, out ShellHelpEntry? entry) && entry is not null
            ? $"{entry.Usage}: {entry.Description}"
            : name;
}
