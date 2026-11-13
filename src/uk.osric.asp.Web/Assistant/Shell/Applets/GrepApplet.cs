// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using System.Globalization;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;

/// <summary>Prints the lines that match a pattern, the keys that have one, or a count for each key.</summary>
internal sealed class GrepApplet : ShellApplet {

    public override string Name => "grep";

    public override string Usage => "grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]";

    public override string Description => "Print key, line and text for every matching line at each path, or below it with -r.";

    public override string? Notes =>
        "The query is a basic regular expression: ^ $ . * and [abc] are metacharacters, \\| is\n"
        + "either-this-or-that, and every other character is the literal one. -i ignores case, and -n\n"
        + "changes nothing because a match always carries its line. -l prints the keys that match, one\n"
        + "per key, and -c prints key:count for every key searched.";

    public override ShellSyntax Syntax => new(
        ['i', 'n', 'r', 'l', 'c'],
        [],
        [new ShellOperand("query", ShellOperandKind.Text), new ShellOperand("path", ShellOperandKind.Prefix, 0, int.MaxValue)]);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        string query = line.Words(ShellOperandKind.Text)[0].Text;
        if (!GrepPattern.TryCompile(query, line.Has('i'), out GrepPattern? compiled, out string? error)) {
            return ShellResult.Refused(error!, $"grep {MessageText.Sanitize(query)}: refused");
        }

        GrepPattern pattern = compiled!;
        bool recursive = line.Has('r');
        bool names = line.Has('l');
        bool counts = !names && line.Has('c');

        IReadOnlyList<PageRead> sources = await context.Store.SourcesAsync(
            Scopes(context.Paths, recursive), cancellationToken);
        (List<string> rows, bool truncated) = Fold(sources, pattern, names, counts, context.Output.MatchLimit);

        string summary = $"grep {MessageText.Sanitize(query)}";
        if (rows.Count > 0) {
            foreach (string row in rows) {
                context.Output.Line(row);
            }
        } else {
            context.Output.Raw(recursive ? "No match." : "No match at this level.");
            if (!recursive) {
                await ExplainDepthAsync(pattern, context, cancellationToken);
            }
        }

        if (truncated) {
            context.Output.Stopped($"{context.Output.MatchLimit} lines shown, in key order, and there are more. Narrow the query or the path.");
        }

        return ShellResult.Ok(summary);
    }

    // With no operand grep reads the root the way a shell reads the current directory: one level, and
    // everything with -r.
    private static IReadOnlyList<PageScope> Scopes(IReadOnlyList<PageNode> paths, bool recursive) =>
        paths.Count == 0
            ? [new PageScope(PagePath.Normalize("/"), IncludeSelf: true, Recursive: recursive)]
            : [.. paths.Select(path => new PageScope(path.Path, IncludeSelf: true, Recursive: recursive))];

    private static (List<string> Rows, bool Truncated) Fold(
        IReadOnlyList<PageRead> sources,
        GrepPattern pattern,
        bool names,
        bool counts,
        int limit) {
        List<string> rows = [];
        if (!names && !counts) {
            // The line scan is the one the search tool runs; only the way a match is written differs.
            LineMatches found = PageSourceSearch.Find(sources, ShellText.Split, pattern.IsMatch, limit);
            return ([.. found.Matches.Select(match => string.Create(
                CultureInfo.InvariantCulture, $"{match.Path.Key}:{match.Line}: {match.Text}"))], found.Truncated);
        }

        // -l and -c answer about a key rather than about a line, so they fold the sources themselves
        // and keep their own stopping rule.
        foreach (PageRead source in sources) {
            string[] lines = ShellText.Split(source.Text);
            if (names && !lines.Any(pattern.IsMatch)) {
                continue;
            }
            if (rows.Count == limit) {
                return (rows, true);
            }

            rows.Add(names
                ? source.Path.Key
                : string.Create(CultureInfo.InvariantCulture, $"{source.Path.Key}:{lines.Count(pattern.IsMatch)}"));
        }

        return (rows, false);
    }

    // "No match" about a folder whose folders were never read invites the wrong conclusion, so a
    // search that would have found something one level down says so.
    private static async Task ExplainDepthAsync(
        GrepPattern pattern,
        ShellContext context,
        CancellationToken cancellationToken) {
        IReadOnlyList<PageRead> deeper = await context.Store.SourcesAsync(
            Scopes(context.Paths, recursive: true), cancellationToken);
        if (deeper.Any(source => ShellText.Split(source.Text).Any(pattern.IsMatch))) {
            context.Output.Note("There are matches below this level: grep -r reads the folders too.");
        }
    }
}
