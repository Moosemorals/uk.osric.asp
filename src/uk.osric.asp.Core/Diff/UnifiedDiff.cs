// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

using System.Text;

/// <summary>Writes a <see cref="DiffDocument"/> as unified diff text.</summary>
public static class UnifiedDiff {

    /// <summary>Writes the canonical unified diff text.</summary>
    /// <param name="diff">The comparison to write.</param>
    /// <returns>
    /// One <c>@@ -a,b +c,d @@</c> header per hunk followed by lines prefixed with a space, <c>-</c>
    /// or <c>+</c>, or the empty string when nothing changed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="diff"/> is <see langword="null"/>.</exception>
    public static string Format(DiffDocument diff) {
        ArgumentNullException.ThrowIfNull(diff);

        if (diff.IsEmpty) {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (DiffHunk hunk in diff.Hunks) {
            builder.Append("@@ -")
                .Append(hunk.BeforeStart)
                .Append(',')
                .Append(hunk.BeforeCount)
                .Append(" +")
                .Append(hunk.AfterStart)
                .Append(',')
                .Append(hunk.AfterCount)
                .Append(" @@\n");

            foreach (DiffLine line in hunk.Lines) {
                builder.Append(line.Kind switch {
                    DiffLineKind.Added => '+',
                    DiffLineKind.Removed => '-',
                    _ => ' ',
                });
                builder.Append(line.Text).Append('\n');
            }
        }

        return builder.ToString();
    }
}
