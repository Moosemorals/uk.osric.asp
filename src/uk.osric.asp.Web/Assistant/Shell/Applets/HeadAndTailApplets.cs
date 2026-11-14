// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;

/// <summary>Prints a counted number of lines from one end of each key.</summary>
/// <remarks>
/// The two commands are one option, one arity and one loop; only which end of the file they take
/// differs, so that is the part each one writes.
/// </remarks>
internal abstract class HeadOrTailApplet : ShellApplet {

    public override string Usage => $"{Name} [-n <count>] [key ...]";

    public override string? Notes =>
        "-n <count> may be written -n5, and the site's configured default is used otherwise. With more than one key each one is\n"
        + $"named in a '==> key <==' line, as a shell's {Name} does.";

    public override ShellSyntax Syntax => new(
        [],
        [new ShellValueOption('n', "a number")],
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

        int count = line.Number('n') ?? context.Options.ShellDefaultLines;
        List<string> rows = [];
        foreach (PageRead file in files) {
            if (files.Count > 1) {
                if (rows.Count > 0) {
                    rows.Add(string.Empty);
                }
                rows.Add($"==> {file.Path.Key} <==");
            }
            rows.AddRange(Take(ShellText.Split(file.Text), count));
        }

        foreach (string row in rows) {
            context.Output.Line(row);
        }

        return ShellResult.Ok($"{Name} {string.Join(' ', context.Paths.Select(path => path.Path.Key))}");
    }

    /// <summary>The lines this command shows from a page's own lines.</summary>
    /// <param name="lines">Every line of the page.</param>
    /// <param name="count">How many to take.</param>
    protected abstract IEnumerable<string> Take(string[] lines, int count);
}

/// <summary>Prints the first lines of each key.</summary>
internal sealed class HeadApplet : HeadOrTailApplet {

    public override string Name => "head";

    public override string Description => "Print the first count lines of each key, ten lines when no count is given.";

    protected override IEnumerable<string> Take(string[] lines, int count) => lines.Take(count);
}

/// <summary>Prints the last lines of each key.</summary>
internal sealed class TailApplet : HeadOrTailApplet {

    public override string Name => "tail";

    public override string Description => "Print the last count lines of each key, ten lines when no count is given.";

    protected override IEnumerable<string> Take(string[] lines, int count) => lines.TakeLast(count);
}
