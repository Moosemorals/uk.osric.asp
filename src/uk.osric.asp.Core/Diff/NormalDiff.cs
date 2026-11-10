// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

using System.Globalization;
using System.Text;

/// <summary>Writes a <see cref="DiffDocument"/> in the normal format, which is the default one.</summary>
/// <remarks>
/// One line per change block — <c>3c3</c> for a replacement, <c>3a4,5</c> for lines added after line 3,
/// <c>3,4d2</c> for lines deleted after line 2 — then the earlier document's lines prefixed
/// <c>&lt; </c>, a <c>---</c> separator when both sides changed, and the later document's lines
/// prefixed <c>&gt; </c>. A range is one number when it covers one line and <c>n,m</c> when it covers
/// more, which is how POSIX writes it, and the letters are the commands <c>ed</c> would take.
/// </remarks>
public static class NormalDiff {

    /// <summary>Writes the normal-format diff text.</summary>
    /// <param name="diff">The comparison to write.</param>
    /// <returns>The change blocks, or the empty string when nothing changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diff"/> is <see langword="null"/>.</exception>
    public static string Format(DiffDocument diff) {
        ArgumentNullException.ThrowIfNull(diff);

        if (diff.IsEmpty) {
            return string.Empty;
        }

        StringBuilder text = new();
        foreach (DiffHunk hunk in diff.Hunks) {
            // The line each document has reached, so a block that only adds or only deletes can say
            // which line it works from. A hunk that covers no lines of one document carries the line
            // it follows instead of the line it starts at, which is what the count distinguishes.
            int before = hunk.BeforeCount > 0 ? hunk.BeforeStart - 1 : hunk.BeforeStart;
            int after = hunk.AfterCount > 0 ? hunk.AfterStart - 1 : hunk.AfterStart;

            int index = 0;
            while (index < hunk.Lines.Count) {
                DiffLine line = hunk.Lines[index];
                if (line.Kind == DiffLineKind.Context) {
                    before = line.BeforeLine!.Value;
                    after = line.AfterLine!.Value;
                    index++;
                    continue;
                }

                List<DiffLine> block = [];
                while (index < hunk.Lines.Count && hunk.Lines[index].Kind != DiffLineKind.Context) {
                    block.Add(hunk.Lines[index++]);
                }

                List<DiffLine> removed = [.. block.Where(item => item.Kind == DiffLineKind.Removed)];
                List<DiffLine> added = [.. block.Where(item => item.Kind == DiffLineKind.Added)];
                if (removed.Count > 0 && added.Count > 0) {
                    _ = text.Append(Range(removed[0].BeforeLine!.Value, removed.Count)).Append('c')
                        .Append(Range(added[0].AfterLine!.Value, added.Count)).Append('\n');
                    Append(text, removed, "< ");
                    _ = text.Append("---\n");
                    Append(text, added, "> ");
                } else if (removed.Count > 0) {
                    _ = text.Append(Range(removed[0].BeforeLine!.Value, removed.Count))
                        .Append('d').Append(Number(after)).Append('\n');
                    Append(text, removed, "< ");
                } else {
                    _ = text.Append(Number(before)).Append('a')
                        .Append(Range(added[0].AfterLine!.Value, added.Count)).Append('\n');
                    Append(text, added, "> ");
                }

                if (removed.Count > 0) {
                    before = removed[^1].BeforeLine!.Value;
                }
                if (added.Count > 0) {
                    after = added[^1].AfterLine!.Value;
                }
            }
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, List<DiffLine> lines, string prefix) {
        foreach (DiffLine line in lines) {
            _ = text.Append(prefix).Append(line.Text).Append('\n');
        }
    }

    private static string Range(int start, int count) => count == 1
        ? Number(start)
        : string.Concat(Number(start), ",", Number(start + count - 1));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
