// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Moves a page, its history and its state to another key.</summary>
/// <remarks>
/// The only applet that writes. It reads the head first, because a move is conditional on the head
/// the caller saw, and the shell is the caller.
/// </remarks>
internal sealed class MvApplet : ShellApplet {

    public override string Name => "mv";

    public override string Usage => "mv <key> <new-key>";

    public override string Description => "Move a page, its revision history and its state to another key. The old key stops answering.";

    public override string? Notes =>
        "A library cannot be moved, and neither can a key with pages below it: move those first.\n"
        + "Nothing is left at the old key, so a seed this release still ships there will be installed there again.";

    public override ShellSyntax Syntax => new(
        [],
        [],
        [
            new ShellOperand("key", ShellOperandKind.Path),
            new ShellOperand("new-key", ShellOperandKind.Destination),
        ]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        if (context.Line.Arguments[0].Word.HasWildcard) {
            // A pattern would expand to several pages and leave the destination at the end of them,
            // so `mv` takes one key: a folder is a batch of moves, not one command.
            return ShellResult.Refused(
                "mv takes one key, not a pattern: name the page to move.",
                "mv: a pattern is not a key");
        }

        PagePath from = context.Paths[0].Path;
        PagePath to = context.Paths[1].Path;

        PageRead? read = await context.Store.ReadAsync(from, cancellationToken);
        if (read?.Page is null) {
            return ShellResult.Refused(
                $"{from.Key} is not a live page. ls {PageTree.FolderOf(from.Key) ?? "/"} lists what is there.",
                $"mv {from.Key}: not a page");
        }

        PageMoveResult moved;
        try {
            moved = PageMoveAnswer.From(
                await context.Pages.MoveAsync(from, to, read.Page.Hash, cancellationToken), from);
        } catch (InvalidOperationException exception) {
            // The capability's page-bug rules — a library and a page moving itself — are refusals a
            // shell command reports, not a fault that ends the run.
            return ShellResult.Refused(exception.Message, $"mv {from.Key}: refused");
        }

        switch (moved.Outcome) {
            case PageMoveOutcome.Moved:
                context.Output.Raw(from.Key + " -> " + to.Key);
                return ShellResult.Ok($"mv {from.Key} {to.Key}") with { Changed = to };

            case PageMoveOutcome.Unchanged:
                return ShellResult.Refused(
                    $"{from.Key} and {to.Key} are the same key, so nothing was moved.",
                    $"mv {from.Key}: unchanged");

            case PageMoveOutcome.Stale:
                return ShellResult.Refused(
                    $"{from.Key} changed since it was read, so nothing was moved. Read it again and retry.",
                    $"mv {from.Key}: stale");

            case PageMoveOutcome.SourceNotFound:
                return ShellResult.Refused(
                    $"{from.Key} has no live page, so nothing was moved.",
                    $"mv {from.Key}: not found");

            case PageMoveOutcome.TargetExists:
                return ShellResult.Refused(
                    $"{to.Key} already has a page, so nothing was moved.",
                    $"mv {from.Key}: target exists");

            case PageMoveOutcome.Busy:
                return ShellResult.Refused(
                    "Another writer held the page store, so nothing was moved. Try again.",
                    $"mv {from.Key}: busy");

            default:
                return ShellResult.Refused(
                    $"{from.Key} names a folder: {string.Join(", ", moved.Below)} live below it. Move or scrub those first.",
                    $"mv {from.Key}: a folder");
        }
    }
}
