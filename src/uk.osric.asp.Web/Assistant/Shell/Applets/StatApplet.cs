// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Web.Pages;

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
        (List<PageRead> files, ShellResult? refusal) =
            await ShellReading.PagesAsync(context.Paths, context.Store, Name, cancellationToken);
        if (refusal is not null) {
            return refusal;
        }

        foreach (PageRead file in files) {
            string mode = PageMode.Describe(file.CanWrite);
            context.Output.Raw(file.Page is null
                ? $"{file.Path.Key}\n{mode}\nshell file\n{ShellText.Split(file.Text).Length} lines, {file.Text.Length} chars"
                : $"{file.Path.Key}\n{mode}\n{file.Page.Template.Length} chars\n{file.Page.Hash}\n{file.Page.SavedAt:O}\n{file.State!.Value.Describe()}");
        }

        return ShellResult.Ok($"stat {string.Join(' ', context.Paths.Select(path => path.Path.Key))}");
    }
}
