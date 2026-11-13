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
        (List<PageRead> files, ShellResult? refusal) =
            await ShellReading.PagesAsync(context.Paths, context.Store, Name, cancellationToken);
        if (refusal is not null) {
            return refusal;
        }

        foreach (PageRead file in files) {
            context.Output.Raw(line.Has('n') ? ShellText.Number(file.Text) : file.Text);
        }

        return ShellResult.Ok($"cat {string.Join(' ', context.Paths.Select(path => path.Path.Key))}");
    }
}
