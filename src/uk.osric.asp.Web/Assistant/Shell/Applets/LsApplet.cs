// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Shell;

/// <summary>Lists what is at a path: the keys in it, or the path itself when it is a page.</summary>
internal sealed class LsApplet : ShellApplet {

    public override string Name => "ls";

    public override string Usage => "ls [-l] [-R] [-r] [path ...]";

    public override string Description => "List what is at a path: the keys in it, or the path itself when it is a page.";

    public override string? Notes =>
        "'-l' puts the permission in front of each key, '-R' lists everything below the paths, and '-r'\n"
        + "reverses the order. A path that names a folder lists what is in it; a path that names a page\n"
        + "names the page, as a shell's ls does.";

    public override ShellSyntax Syntax => new(
        ['l', 'R', 'r'],
        [],
        [new ShellOperand("path", ShellOperandKind.Prefix, 0, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        bool longListing = line.Has('l');
        bool recursive = line.Has('R');
        IReadOnlyList<ShellPath> nodes = await context.Store.NodesAsync(cancellationToken);

        List<ShellPath> scopes = [.. context.Paths];
        if (scopes.Count == 0) {
            scopes.AddRange(nodes.Where(node => node.Key.Key == "/"));
        }

        // Each path is listed on its own and in the order it was written, the way a shell groups its
        // operands.
        List<ShellPath> entries = [];
        foreach (ShellPath scope in scopes) {
            List<ShellPath> found = [.. nodes.Where(node =>
                ShellTree.IsDirectlyBelow(node.Key, scope.Key)
                || (recursive && ShellTree.IsBelow(node.Key, scope.Key)))];
            // ls on a page names the page, the way ls on a file names the file; a folder lists what is
            // in it and nothing else, even when that is nothing.
            if (found.Count == 0 && !scope.IsFolder) {
                found.Add(scope);
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Key.Key, right.Key.Key));
            entries.AddRange(found);
        }

        List<ShellPath> unique = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ShellPath entry in entries) {
            if (seen.Add(entry.Key.Key)) {
                unique.Add(entry);
            }
        }
        if (line.Has('r')) {
            unique.Reverse();
        }

        // A path that is not there was refused before this ran, and a folder is only a folder because
        // something lives below it, so a listing is never empty.
        string where = string.Join(' ', scopes.Select(scope => scope.Key.Key));
        foreach (ShellPath entry in unique.Take(context.Output.ListingLimit)) {
            context.Output.Line(longListing ? $"{entry.Mode} {entry.Key.Key}" : entry.Key.Key);
        }
        if (unique.Count > context.Output.ListingLimit) {
            context.Output.Stopped($"{context.Output.ListingLimit} entries shown, and there are more. List a path to see the rest.");
        }

        return ShellResult.Ok($"ls {where}");
    }
}
