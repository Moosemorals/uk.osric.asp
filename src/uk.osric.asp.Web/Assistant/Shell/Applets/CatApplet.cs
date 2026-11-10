// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;

/// <summary>Prints each page's source, one after another.</summary>
internal sealed class CatApplet : ShellApplet {

    public override string Name => "cat";

    public override string Usage => "cat [-n] <key> [key ...]";

    public override string Description => "Print each page's source, one after another.";

    public override string? Notes =>
        "'-n' puts each line's number and a tab before it, counting each page from one, because a line\n"
        + "number names a line of a page: it can be pasted into read_page or edit_page.";

    public override ShellSyntax Syntax => new(
        ['n'],
        [],
        [new ShellOperand("key", ShellOperandKind.Path, 1, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        foreach (ShellPath path in context.Paths) {
            if (!path.IsPage) {
                return ShellResult.Refused(
                    $"{path.Key.Key} is a folder, not a page. ls {path.Key.Key} lists what is in it.",
                    $"cat {path.Key.Key}: a folder");
            }
        }

        foreach (ShellPath path in context.Paths) {
            ShellFile? file = await context.Store.ReadAsync(path, cancellationToken);
            if (file is null) {
                return ShellResult.Refused($"Page {path.Key.Key} was not found.", $"cat {path.Key.Key}: not found");
            }

            context.Output.Raw(line.Has('n') ? ShellText.Number(file.Text) : file.Text);
        }

        return ShellResult.Ok($"cat {string.Join(' ', context.Paths.Select(path => path.Key.Key))}");
    }
}
