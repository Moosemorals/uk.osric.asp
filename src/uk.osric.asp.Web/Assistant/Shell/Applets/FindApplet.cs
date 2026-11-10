// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell.Applets;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Shell;
using uk.osric.asp.Core.Text;

/// <summary>Prints the keys at or below each path that match every test it was given.</summary>
internal sealed class FindApplet : ShellApplet {

    public override string Name => "find";

    public override string Usage => "find [path ...] [-name <pattern>] [-path <pattern>] [-type f|d]";

    public override string Description => "Print the keys at or below each path that match every test given.";

    public override string? Notes =>
        "-name matches the last segment of a key and -path the whole key, both with the shell's own\n"
        + "patterns; -type takes f for a page or a shell file and d for a folder. The tests are joined\n"
        + "by and, there is no or and no negation yet, and find always descends. A path that is not\n"
        + "written is the root.";

    public override ShellSyntax Syntax => new(
        [],
        [],
        [new ShellOperand("path", ShellOperandKind.Prefix, 0, int.MaxValue)],
        Trailing: true);

    public override async ValueTask<ShellResult> RunAsync(
        ShellCommandLine line,
        ShellContext context,
        CancellationToken cancellationToken) {
        (FindExpression? expression, string? error) = FindExpression.Parse(line.Trailing);
        if (expression is null) {
            return ShellResult.Refused(error!, "find: refused");
        }

        IReadOnlyList<ShellPath> nodes = await context.Store.NodesAsync(cancellationToken);
        List<ShellPath> scopes = [.. context.Paths];
        if (scopes.Count == 0) {
            scopes.AddRange(nodes.Where(node => node.Key.Key == "/"));
        }

        List<ShellPath> found = [.. nodes.Where(node =>
            scopes.Any(scope => string.Equals(node.Key.Key, scope.Key.Key, StringComparison.Ordinal)
                || ShellTree.IsBelow(node.Key, scope.Key))
            && expression.Matches(node))];

        foreach (ShellPath node in found.Take(context.Output.ListingLimit)) {
            context.Output.Line(node.Key.Key);
        }
        if (found.Count > context.Output.ListingLimit) {
            context.Output.Stopped($"{context.Output.ListingLimit} keys shown, and there are more. Match more narrowly.");
        }

        return ShellResult.Ok($"find {string.Join(' ', scopes.Select(scope => scope.Key.Key))}");
    }
}

/// <summary>What a <c>find</c> line asks for: the tests, joined by and.</summary>
/// <remarks>
/// POSIX gives <c>find</c> an expression rather than options, so this is the command's own language,
/// the way <see cref="uk.osric.asp.Core.Assistant.GrepPattern"/> is <c>grep</c>'s. It is a subset: the
/// primaries below, joined by an implicit and, and nothing else — an unknown one is refused by name
/// rather than ignored.
/// </remarks>
internal sealed class FindExpression {

    private readonly List<Func<ShellPath, bool>> _tests = [];

    private FindExpression() {
    }

    public static (FindExpression? Expression, string? Error) Parse(IReadOnlyList<ShellWord> words) {
        ArgumentNullException.ThrowIfNull(words);

        FindExpression expression = new();
        int index = 0;
        while (index < words.Count) {
            string primary = words[index++].Text;
            if (primary is "-name" or "-path") {
                if (index == words.Count) {
                    return (null, $"{primary} needs a pattern. Run help find for the form it takes.");
                }

                // The pattern is read as text: quoting a word protects it from the shell's own
                // expansion, and has nothing to do with what find's pattern means.
                string pattern = words[index++].Text;
                expression._tests.Add(primary == "-name"
                    ? node => PageGlob.IsMatch(pattern, LastSegment(node.Key.Key))
                    : node => PageGlob.IsMatch(pattern, node.Key.Key));
                continue;
            }

            if (primary == "-type") {
                if (index == words.Count) {
                    return (null, "-type needs f or d. Run help find for the form it takes.");
                }

                string type = words[index++].Text;
                if (type is not ("f" or "d")) {
                    return (null, $"-type takes f or d, not {MessageText.Sanitize(type)}. Run help find for the form it takes.");
                }

                expression._tests.Add(type == "d" ? node => node.IsFolder : node => node.IsPage);
                continue;
            }

            return (null, $"{MessageText.Sanitize(primary)} is not a test this shell has. Run help find for the form it takes.");
        }

        return (expression, null);
    }

    public bool Matches(ShellPath node) {
        ArgumentNullException.ThrowIfNull(node);
        return _tests.All(test => test(node));
    }

    private static string LastSegment(string key) => key[(key.LastIndexOf('/') + 1)..];
}
