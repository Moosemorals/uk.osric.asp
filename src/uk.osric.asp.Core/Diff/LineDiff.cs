// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

using System.Text;

/// <summary>Compares two documents line by line.</summary>
/// <remarks>
/// <para>
/// The ordinary case is small: the common prefix and suffix are trimmed first, so one edit in a large
/// page leaves only the lines around it. The middle is then diffed with Myers' O(ND) algorithm. Past
/// <see cref="MaxEditDistance"/> or <see cref="MaxLines"/>, the middle becomes one removed block and
/// one added block and <see cref="DiffDocument.IsCoarse"/> says so: every line is still present, but
/// the alignment was given up rather than the work left unbounded.
/// </para>
/// <para>
/// Lines are what <see cref="StringReader.ReadLine"/> returns: CRLF, CR and LF are one break, and a
/// final newline terminates the last line rather than creating an empty one. Two templates that
/// differ only in line endings therefore compare equal. The result is pure and has no I/O.
/// </para>
/// </remarks>
public static class LineDiff {

    /// <summary>How many unchanged lines surround each changed region.</summary>
    public const int ContextLines = 3;

    /// <summary>How many edits the algorithm may find before it gives up on alignment.</summary>
    public const int MaxEditDistance = 1_000;

    /// <summary>How many differing lines a document pair may have before the algorithm gives up on alignment.</summary>
    public const int MaxLines = 100_000;

    /// <summary>Compares two documents line by line.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <returns>The changes, grouped into hunks with <see cref="ContextLines"/> lines of context.</returns>
    /// <exception cref="ArgumentNullException">Either document is <see langword="null"/>.</exception>
    public static DiffDocument Compare(string before, string after) => Compare(before, after, ContextLines, ignoreSpaceChange: false);

    /// <summary>Compares two documents line by line, with as much context as asked for.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <param name="contextLines">How many unchanged lines surround each changed region.</param>
    /// <param name="ignoreSpaceChange">
    /// Whether a run of blanks compares equal to any other run of blanks, and to none at the end of a
    /// line, as <c>diff -b</c> means it to.
    /// </param>
    /// <returns>The changes, grouped into hunks with <paramref name="contextLines"/> lines of context.</returns>
    /// <exception cref="ArgumentNullException">Either document is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contextLines"/> is negative.</exception>
    /// <remarks>
    /// The ignored difference is the whitespace, never the line: the comparison runs on a collapsed
    /// copy of each line and the text in the result is put back as it was written.
    /// </remarks>
    public static DiffDocument Compare(string before, string after, int contextLines, bool ignoreSpaceChange) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);

        string[] beforeWritten = SplitLines(before);
        string[] afterWritten = SplitLines(after);
        string[] beforeLines = ignoreSpaceChange ? [.. beforeWritten.Select(WhitespaceKey)] : beforeWritten;
        string[] afterLines = ignoreSpaceChange ? [.. afterWritten.Select(WhitespaceKey)] : afterWritten;

        int prefix = 0;
        while (prefix < beforeLines.Length
            && prefix < afterLines.Length
            && string.Equals(beforeLines[prefix], afterLines[prefix], StringComparison.Ordinal)) {
            prefix++;
        }

        int suffix = 0;
        while (suffix < beforeLines.Length - prefix
            && suffix < afterLines.Length - prefix
            && string.Equals(beforeLines[^(suffix + 1)], afterLines[^(suffix + 1)], StringComparison.Ordinal)) {
            suffix++;
        }

        string[] beforeMiddle = beforeLines[prefix..(beforeLines.Length - suffix)];
        string[] afterMiddle = afterLines[prefix..(afterLines.Length - suffix)];

        (List<Operation> middle, bool isCoarse) = DiffMiddle(beforeMiddle, afterMiddle);

        List<DiffLine> script = [];
        int beforeNumber = 1;
        int afterNumber = 1;
        for (int index = 0; index < prefix; index++) {
            script.Add(new DiffLine(DiffLineKind.Context, beforeLines[index], beforeNumber++, afterNumber++));
        }
        foreach (Operation operation in middle) {
            switch (operation.Kind) {
                case DiffOperationKind.Equal:
                    script.Add(new DiffLine(DiffLineKind.Context, operation.Text, beforeNumber++, afterNumber++));
                    break;
                case DiffOperationKind.Delete:
                    script.Add(new DiffLine(DiffLineKind.Removed, operation.Text, beforeNumber++, null));
                    break;
                default:
                    script.Add(new DiffLine(DiffLineKind.Added, operation.Text, null, afterNumber++));
                    break;
            }
        }
        for (int index = beforeLines.Length - suffix; index < beforeLines.Length; index++) {
            script.Add(new DiffLine(DiffLineKind.Context, beforeLines[index], beforeNumber++, afterNumber++));
        }

        DiffDocument diff = new(
            BuildHunks(script, contextLines),
            script.Count(line => line.Kind == DiffLineKind.Added),
            script.Count(line => line.Kind == DiffLineKind.Removed),
            isCoarse);

        return ignoreSpaceChange ? AsWritten(diff, beforeWritten, afterWritten) : diff;
    }

    /// <summary>The comparison's alignment, with each line's text put back as it was written.</summary>
    private static DiffDocument AsWritten(DiffDocument diff, string[] before, string[] after) => diff with {
        Hunks = [
            .. diff.Hunks.Select(hunk => hunk with {
                Lines = [
                    .. hunk.Lines.Select(line => line with {
                        Text = line.Kind == DiffLineKind.Added
                            ? after[line.AfterLine!.Value - 1]
                            : before[line.BeforeLine!.Value - 1],
                    }),
                ],
            }),
        ],
    };

    // POSIX -b: any run of blanks compares equal to any other, and a run at the end of a line compares
    // equal to no blanks at all, so changing trailing white space is not a change.
    private static string WhitespaceKey(string line) {
        StringBuilder key = new(line.Length);
        bool pending = false;
        foreach (char character in line) {
            if (character is ' ' or '\t') {
                pending = key.Length > 0;
            } else {
                if (pending) {
                    _ = key.Append(' ');
                    pending = false;
                }
                _ = key.Append(character);
            }
        }

        return key.ToString();
    }

    private static string[] SplitLines(string text) {
        List<string> lines = [];
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line) {
            lines.Add(line);
        }
        return [.. lines];
    }

    private static (List<Operation> Operations, bool IsCoarse) DiffMiddle(string[] before, string[] after) {
        if (before.Length == 0) {
            return (after.Select(line => new Operation(DiffOperationKind.Insert, line)).ToList(), false);
        }
        if (after.Length == 0) {
            return (before.Select(line => new Operation(DiffOperationKind.Delete, line)).ToList(), false);
        }
        if (before.Length + after.Length > MaxLines) {
            return (Replace(before, after), true);
        }

        List<Operation>? aligned = Align(before, after);
        return aligned is null ? (Replace(before, after), true) : (aligned, false);
    }

    private static List<Operation> Replace(string[] before, string[] after) {
        List<Operation> operations = new(before.Length + after.Length);
        foreach (string line in before) {
            operations.Add(new Operation(DiffOperationKind.Delete, line));
        }
        foreach (string line in after) {
            operations.Add(new Operation(DiffOperationKind.Insert, line));
        }
        return operations;
    }

    // Myers' greedy algorithm, bounded at MaxEditDistance. A null answer means the middle needed
    // more edits than the cap allows. The frontier is cloned once per distance for the backtrack;
    // with the cap that is a few megabytes at worst, not the square of the document size.
    private static List<Operation>? Align(string[] before, string[] after) {
        int limit = Math.Min(before.Length + after.Length, MaxEditDistance);
        int offset = limit;
        int[] frontier = new int[(2 * limit) + 1];
        List<int[]> trace = new(limit + 1);

        for (int distance = 0; distance <= limit; distance++) {
            trace.Add((int[])frontier.Clone());
            for (int diagonal = -distance; diagonal <= distance; diagonal += 2) {
                int x;
                if (diagonal == -distance
                    || (diagonal != distance && frontier[diagonal - 1 + offset] < frontier[diagonal + 1 + offset])) {
                    x = frontier[diagonal + 1 + offset];
                } else {
                    x = frontier[diagonal - 1 + offset] + 1;
                }

                int y = x - diagonal;
                while (x < before.Length && y < after.Length
                    && string.Equals(before[x], after[y], StringComparison.Ordinal)) {
                    x++;
                    y++;
                }

                frontier[diagonal + offset] = x;
                if (x >= before.Length && y >= after.Length) {
                    return Backtrack(trace, before, after, distance, offset);
                }
            }
        }

        return null;
    }

    private static List<Operation> Backtrack(
        List<int[]> trace,
        string[] before,
        string[] after,
        int distance,
        int offset) {
        List<Operation> operations = [];
        int x = before.Length;
        int y = after.Length;

        for (int step = distance; step > 0; step--) {
            int[] frontier = trace[step];
            int diagonal = x - y;
            int previousDiagonal;
            if (diagonal == -step
                || (diagonal != step && frontier[diagonal - 1 + offset] < frontier[diagonal + 1 + offset])) {
                previousDiagonal = diagonal + 1;
            } else {
                previousDiagonal = diagonal - 1;
            }

            int previousX = frontier[previousDiagonal + offset];
            int previousY = previousX - previousDiagonal;

            while (x > previousX && y > previousY) {
                operations.Add(new Operation(DiffOperationKind.Equal, before[x - 1]));
                x--;
                y--;
            }

            if (x == previousX) {
                operations.Add(new Operation(DiffOperationKind.Insert, after[y - 1]));
                y--;
            } else {
                operations.Add(new Operation(DiffOperationKind.Delete, before[x - 1]));
                x--;
            }

            x = previousX;
            y = previousY;
        }

        while (x > 0 && y > 0) {
            operations.Add(new Operation(DiffOperationKind.Equal, before[x - 1]));
            x--;
            y--;
        }
        while (x > 0) {
            operations.Add(new Operation(DiffOperationKind.Delete, before[x - 1]));
            x--;
        }
        while (y > 0) {
            operations.Add(new Operation(DiffOperationKind.Insert, after[y - 1]));
            y--;
        }

        operations.Reverse();
        return operations;
    }

    private static IReadOnlyList<DiffHunk> BuildHunks(List<DiffLine> script, int contextLines) {
        List<int> changes = [];
        for (int index = 0; index < script.Count; index++) {
            if (script[index].Kind != DiffLineKind.Context) {
                changes.Add(index);
            }
        }
        if (changes.Count == 0) {
            return [];
        }

        int[] beforeCounts = new int[script.Count + 1];
        int[] afterCounts = new int[script.Count + 1];
        for (int index = 0; index < script.Count; index++) {
            beforeCounts[index + 1] = beforeCounts[index] + (script[index].BeforeLine is null ? 0 : 1);
            afterCounts[index + 1] = afterCounts[index] + (script[index].AfterLine is null ? 0 : 1);
        }

        List<DiffHunk> hunks = [];
        int change = 0;
        while (change < changes.Count) {
            int first = changes[change];
            int last = first;
            int next = change + 1;

            // Changes no more than two context distances apart would share their context, so they
            // belong to one hunk rather than two adjacent ones.
            while (next < changes.Count && changes[next] - last - 1 <= 2 * contextLines) {
                last = changes[next];
                next++;
            }

            int start = Math.Max(0, first - contextLines);
            int end = Math.Min(script.Count, last + contextLines + 1);
            int beforeCount = beforeCounts[end] - beforeCounts[start];
            int afterCount = afterCounts[end] - afterCounts[start];

            hunks.Add(new DiffHunk(
                beforeCount > 0 ? beforeCounts[start] + 1 : beforeCounts[start],
                beforeCount,
                afterCount > 0 ? afterCounts[start] + 1 : afterCounts[start],
                afterCount,
                script.GetRange(start, end - start)));
            change = next;
        }

        return hunks;
    }

    private enum DiffOperationKind {
        Equal,
        Delete,
        Insert,
    }

    private readonly record struct Operation(DiffOperationKind Kind, string Text);
}
