// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Pages;
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
        IReadOnlyList<PageNode> nodes = await context.Store.TreeAsync(includeDeleted: false, cancellationToken);

        List<PageNode> scopes = [.. context.Paths];
        if (scopes.Count == 0) {
            scopes.AddRange(nodes.Where(node => node.Path.Key == "/"));
        }

        // Each path is listed on its own and in the order it was written, the way a shell groups its
        // operands.
        List<PageNode> entries = [];
        foreach (PageNode scope in scopes) {
            List<PageNode> found = [.. nodes.Where(node =>
                PageTree.IsDirectlyBelow(node.Path, scope.Path)
                || (recursive && PageTree.IsBelow(node.Path, scope.Path)))];
            // ls on a page names the page, the way ls on a file names the file; a folder lists what is
            // in it and nothing else, even when that is nothing.
            if (found.Count == 0 && !scope.IsFolder) {
                found.Add(scope);
            }

            found.Sort((left, right) => string.CompareOrdinal(left.Path.Key, right.Path.Key));
            entries.AddRange(found);
        }

        List<PageNode> unique = [];
        HashSet<string> seen = [];
        foreach (PageNode entry in entries) {
            if (seen.Add(entry.Path.Key)) {
                unique.Add(entry);
            }
        }
        if (line.Has('r')) {
            unique.Reverse();
        }

        // A path that is not there was refused before this ran, and a folder is only a folder because
        // something lives below it, so a listing is never empty.
        string where = string.Join(' ', scopes.Select(scope => scope.Path.Key));
        foreach (PageNode entry in unique.Take(context.Output.ListingLimit)) {
            string name = entry.Path.Key;
            context.Output.Line(longListing ? $"{Mode(entry)} {name}" : name);
        }
        if (unique.Count > context.Output.ListingLimit) {
            context.Output.Stopped($"{context.Output.ListingLimit} entries shown, and there are more. List a path to see the rest.");
        }

        return ShellResult.Ok($"ls {where}");
    }

    // The permission column: a folder carries a leading d, and the rest is the same answer a page
    // carries about whether a save would be accepted.
    private static string Mode(PageNode node) => node.IsFolder
        ? PageMode.DescribeFolder(node.CanWrite)
        : PageMode.Describe(node.CanWrite);
}
