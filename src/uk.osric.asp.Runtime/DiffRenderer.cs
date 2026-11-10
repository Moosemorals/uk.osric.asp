// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Globalization;
using System.Net;
using System.Text;
using uk.osric.asp.Core.Diff;
using IDiffRenderer = uk.osric.asp.Sdk.IDiffRenderer;

/// <summary>The host's diff renderer: the page and editor table and the assistant's unified text.</summary>
/// <remarks>
/// <para>
/// <see cref="ToHtml"/> is the page capability and the editor's view. It escapes every source line,
/// so the result is safe for any string, and writes no inline style: the table, the hunk row, the
/// change markers and the sideways scroll container are all classes the site's sheets style.
/// </para>
/// <para>
/// <see cref="ToUnifiedDiff"/> is the assistant's form, beside the table the way
/// <see cref="MarkdownRenderer.ToRestrictedHtml"/> sits beside the page profile. It is not on the
/// Sdk capability, because a page does not need text; a page that does can render the changes itself.
/// </para>
/// <para>
/// The renderer is stateless. The diff itself is pure and bounded by <see cref="LineDiff"/>.
/// </para>
/// </remarks>
public sealed class DiffRenderer : IDiffRenderer {

    private const string CoarseNotice =
        "<p class=\"diff-coarse\">These documents differ too much to align line by line; the changed middle is shown as replacement blocks.</p>";

    /// <inheritdoc/>
    public string ToHtml(string before, string after) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        DiffDocument diff = LineDiff.Compare(before, after);
        if (diff.IsEmpty) {
            return "<p class=\"diff-empty\">No changes.</p>";
        }

        StringBuilder builder = new();
        if (diff.IsCoarse) {
            builder.Append(CoarseNotice);
        }

        builder.Append("<div class=\"diff-scroll\" tabindex=\"0\"><table class=\"diff\">")
            .Append("<thead><tr><th scope=\"col\">Before</th><th scope=\"col\">After</th>")
            .Append("<th scope=\"col\">Change</th><th scope=\"col\">Line</th></tr></thead><tbody>");

        foreach (DiffHunk hunk in diff.Hunks) {
            AppendHunkHeader(builder, hunk);
            foreach (DiffLine line in hunk.Lines) {
                AppendLine(builder, line);
            }
        }

        return builder.Append("</tbody></table></div>").ToString();
    }

    /// <summary>Compares two documents with the renderer's own engine.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <returns>The line changes, which <see cref="ToHtml"/> and <see cref="ToUnifiedDiff(DiffDocument)"/> consume.</returns>
    /// <exception cref="ArgumentNullException">Either document is <see langword="null"/>.</exception>
    public DiffDocument Compare(string before, string after) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return LineDiff.Compare(before, after);
    }

    /// <summary>Writes the comparison as unified diff text, for a tool result.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <returns>The text <see cref="UnifiedDiff.Format"/> writes; empty when nothing changed.</returns>
    /// <exception cref="ArgumentNullException">Either document is <see langword="null"/>.</exception>
    public string ToUnifiedDiff(string before, string after) {
        return ToUnifiedDiff(Compare(before, after));
    }

    /// <summary>Writes an existing comparison as unified diff text.</summary>
    /// <param name="diff">The comparison to write.</param>
    /// <returns>The text <see cref="UnifiedDiff.Format"/> writes; empty when nothing changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diff"/> is <see langword="null"/>.</exception>
    public string ToUnifiedDiff(DiffDocument diff) {
        ArgumentNullException.ThrowIfNull(diff);

        return UnifiedDiff.Format(diff);
    }

    private static void AppendHunkHeader(StringBuilder builder, DiffHunk hunk) {
        builder.Append("<tr class=\"diff-hunk\"><td colspan=\"4\">@@ -")
            .Append(hunk.BeforeStart.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(hunk.BeforeCount.ToString(CultureInfo.InvariantCulture))
            .Append(" +")
            .Append(hunk.AfterStart.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(hunk.AfterCount.ToString(CultureInfo.InvariantCulture))
            .Append(" @@</td></tr>");
    }

    private static void AppendLine(StringBuilder builder, DiffLine line) {
        builder.Append(line.Kind switch {
            DiffLineKind.Added => "<tr class=\"diff-added\">",
            DiffLineKind.Removed => "<tr class=\"diff-removed\">",
            _ => "<tr class=\"diff-context\">",
        });

        builder.Append("<td>");
        if (line.BeforeLine is { } beforeLine) {
            builder.Append(beforeLine.ToString(CultureInfo.InvariantCulture));
        }
        builder.Append("</td><td>");
        if (line.AfterLine is { } afterLine) {
            builder.Append(afterLine.ToString(CultureInfo.InvariantCulture));
        }
        builder.Append("</td>");

        builder.Append(line.Kind switch {
            DiffLineKind.Added => "<td><span class=\"visually-hidden\">Added</span>+</td>",
            DiffLineKind.Removed => "<td><span class=\"visually-hidden\">Removed</span>-</td>",
            _ => "<td><span class=\"visually-hidden\">Unchanged</span> </td>",
        });

        builder.Append("<td><code>")
            .Append(WebUtility.HtmlEncode(line.Text))
            .Append("</code></td></tr>");
    }
}
