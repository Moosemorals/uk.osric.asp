// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;

/// <summary>Prints the first lines of each key.</summary>
internal sealed class HeadApplet : ShellApplet {

    private const int DefaultLines = 10;

    public override string Name => "head";

    public override string Usage => "head [-n <count>] [key ...]";

    public override string Description => "Print the first count lines of each key, ten lines when no count is given.";

    public override string? Notes =>
        "-n <count> may be written -n5, and ten is the default. With more than one key each one is\n"
        + "named in a '==> key <==' line, as a shell's head does.";

    public override ShellSyntax Syntax => new(
        [],
        [new ShellValueOption('n', "a number")],
        [new ShellOperand("key", ShellOperandKind.Path, 1, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        int count = line.Number('n') ?? DefaultLines;
        List<string> rows = [];
        foreach (ShellPath path in context.Paths) {
            ShellFile? file = await context.Store.ReadAsync(path, cancellationToken);
            if (file is null) {
                return ShellResult.Refused(
                    $"{path.Key.Key} is a folder, not a page. ls {path.Key.Key} lists what is in it.",
                    $"head {path.Key.Key}: a folder");
            }

            if (context.Paths.Count > 1) {
                if (rows.Count > 0) {
                    rows.Add(string.Empty);
                }
                rows.Add($"==> {path.Key.Key} <==");
            }
            rows.AddRange(ShellText.Split(file.Text).Take(count));
        }

        foreach (string row in rows) {
            context.Output.Line(row);
        }

        return ShellResult.Ok($"head {string.Join(' ', context.Paths.Select(path => path.Key.Key))}");
    }
}

/// <summary>Prints the last lines of each key.</summary>
internal sealed class TailApplet : ShellApplet {

    private const int DefaultLines = 10;

    public override string Name => "tail";

    public override string Usage => "tail [-n <count>] [key ...]";

    public override string Description => "Print the last count lines of each key, ten lines when no count is given.";

    public override string? Notes =>
        "-n <count> may be written -n5, and ten is the default. With more than one key each one is\n"
        + "named in a '==> key <==' line, as a shell's tail does.";

    public override ShellSyntax Syntax => new(
        [],
        [new ShellValueOption('n', "a number")],
        [new ShellOperand("key", ShellOperandKind.Path, 1, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        int count = line.Number('n') ?? DefaultLines;
        List<string> rows = [];
        foreach (ShellPath path in context.Paths) {
            ShellFile? file = await context.Store.ReadAsync(path, cancellationToken);
            if (file is null) {
                return ShellResult.Refused(
                    $"{path.Key.Key} is a folder, not a page. ls {path.Key.Key} lists what is in it.",
                    $"tail {path.Key.Key}: a folder");
            }

            if (context.Paths.Count > 1) {
                if (rows.Count > 0) {
                    rows.Add(string.Empty);
                }
                rows.Add($"==> {path.Key.Key} <==");
            }
            rows.AddRange(ShellText.Split(file.Text).TakeLast(count));
        }

        foreach (string row in rows) {
            context.Output.Line(row);
        }

        return ShellResult.Ok($"tail {string.Join(' ', context.Paths.Select(path => path.Key.Key))}");
    }
}
