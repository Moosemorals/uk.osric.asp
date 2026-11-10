// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using System.Globalization;
using uk.osric.asp.Core.Shell;

/// <summary>Counts the lines of each key, as a shell's wc counts them.</summary>
internal sealed class WcApplet : ShellApplet {

    public override string Name => "wc";

    public override string Usage => "wc [-l] <key> [key ...]";

    public override string Description => "Print the number of lines in each key, and a total when there is more than one.";

    public override string? Notes =>
        "-l counts lines, which is the only thing this wc counts; it is accepted so that the letter a\n"
        + "shell's wc is given works here too.";

    public override ShellSyntax Syntax => new(
        ['l'],
        [],
        [new ShellOperand("key", ShellOperandKind.Path, 1, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        (List<ShellFile> files, ShellResult? refusal) =
            await ShellReading.PagesAsync(context.Paths, context.Store, Name, cancellationToken);
        if (refusal is not null) {
            return refusal;
        }

        int total = 0;
        List<string> rows = [];
        foreach (ShellFile file in files) {
            int lines = ShellText.Split(file.Text).Length;
            total += lines;
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{lines} {file.Path.Key.Key}"));
        }

        if (files.Count > 1) {
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{total} total"));
        }

        foreach (string row in rows) {
            context.Output.Line(row);
        }

        return ShellResult.Ok($"wc {string.Join(' ', context.Paths.Select(path => path.Key.Key))}");
    }
}
