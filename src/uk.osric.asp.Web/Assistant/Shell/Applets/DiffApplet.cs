// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Diff;
using uk.osric.asp.Core.Shell;

/// <summary>Compares two keys and writes what POSIX diff writes.</summary>
internal sealed class DiffApplet : ShellApplet {

    public override string Name => "diff";

    public override string Usage => "diff [-b] [-u | -U <count>] <key> <key>";

    public override string Description => "Compare two pages line by line, in the normal format unless -u or -U asks for a unified one.";

    public override string? Notes =>
        "-u is a unified diff with three lines of context and -U <count> gives another count, which may be\n"
        + "written -U3. -b ignores a change in the amount of white space, trailing white space included.\n"
        + "Two keys only, for now. Identical pages write nothing and the call still succeeds, as a\n"
        + "shell's diff does.";

    public override ShellSyntax Syntax => new(
        ['b', 'u'],
        [new ShellValueOption('U', "a number")],
        [new ShellOperand("left", ShellOperandKind.Path), new ShellOperand("right", ShellOperandKind.Path)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(context);

        int contextLines = line.Number('U') ?? LineDiff.ContextLines;
        bool unified = line.Has('u') || line.Number('U') is not null;

        List<ShellFile> files = [];
        foreach (ShellPath path in context.Paths) {
            ShellFile? file = await context.Store.ReadAsync(path, cancellationToken);
            if (file is null) {
                return ShellResult.Refused(
                    $"{path.Key.Key} is a folder, not a page. ls {path.Key.Key} lists what is in it.",
                    $"diff {path.Key.Key}: a folder");
            }
            files.Add(file);
        }

        DiffDocument diff = LineDiff.Compare(files[0].Text, files[1].Text, contextLines, line.Has('b'));
        if (!diff.IsEmpty) {
            if (unified) {
                context.Output.Line($"--- {files[0].Path.Key.Key}");
                context.Output.Line($"+++ {files[1].Path.Key.Key}");
                context.Output.Raw(UnifiedDiff.Format(diff));
            } else {
                context.Output.Raw(NormalDiff.Format(diff));
            }
        }
        if (diff.IsCoarse) {
            context.Output.Note("the pages differ too much to align line by line: this is one removed block and one added block");
        }

        return ShellResult.Ok($"diff {files[0].Path.Key.Key} {files[1].Path.Key.Key}");
    }
}
