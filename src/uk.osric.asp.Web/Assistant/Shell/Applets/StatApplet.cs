// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;

/// <summary>Prints a record for each key: its permission, size, revision and compile state.</summary>
internal sealed class StatApplet : ShellApplet {

    public override string Name => "stat";

    public override string Usage => "stat <key> [key ...]";

    public override string Description => "Print each page's key, permission, size, revision hash, saved time and compile state.";

    public override ShellSyntax Syntax => new(
        [],
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

        foreach (ShellFile file in files) {
            context.Output.Raw(file.Page is null
                ? $"{file.Path.Key.Key}\n{file.Path.Mode}\nshell file\n{ShellText.Split(file.Text).Length} lines, {file.Text.Length} chars"
                : $"{file.Path.Key.Key}\n{file.Path.Mode}\n{file.Page.Template.Length} chars\n{file.Page.Hash}\n{file.Page.SavedAt:O}\n{file.Status}");
        }

        return ShellResult.Ok($"stat {string.Join(' ', context.Paths.Select(path => path.Key.Key))}");
    }
}
