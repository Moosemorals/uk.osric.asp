// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using System.Globalization;

/// <summary>Renders a Markdown table as HTML the site can serve.</summary>
/// <remarks>
/// Markdig's own table renderer writes column alignment as an inline <c>style</c> attribute and
/// writes header cells without a <c>scope</c>. The site's CSP is <c>style-src 'self'</c> with no
/// <c>'unsafe-inline'</c>, and its accessibility rules require a scope on every header cell, so
/// both profiles use this renderer instead: alignment is ignored and every header cell is a column
/// header.
/// </remarks>
internal sealed class AccessibleTableRenderer : HtmlObjectRenderer<Table> {

    protected override void Write(HtmlRenderer renderer, Table table) {
        if (!renderer.EnableHtmlForBlock) {
            renderer.WriteChildren(table);
            return;
        }

        renderer.EnsureLine();
        renderer.Write("<table>").WriteLine();

        bool hasBody = false;
        bool hasHeader = false;
        bool headerIsOpen = false;

        foreach (TableRow row in table) {
            if (row.IsHeader) {
                if (!hasHeader) {
                    renderer.WriteLine("<thead>");
                    headerIsOpen = true;
                }

                hasHeader = true;
            } else if (!hasBody) {
                if (headerIsOpen) {
                    renderer.WriteLine("</thead>");
                    headerIsOpen = false;
                }

                renderer.WriteLine("<tbody>");
                hasBody = true;
            }

            renderer.Write("<tr>").WriteLine();
            for (int index = 0; index < row.Count; index++) {
                TableCell cell = (TableCell)row[index];

                renderer.EnsureLine();
                renderer.Write(row.IsHeader ? "<th scope=\"col\"" : "<td");
                if (cell.ColumnSpan != 1) {
                    renderer.Write(" colspan=\"");
                    renderer.Write(cell.ColumnSpan.ToString(CultureInfo.InvariantCulture));
                    renderer.Write('"');
                }

                if (cell.RowSpan != 1) {
                    renderer.Write(" rowspan=\"");
                    renderer.Write(cell.RowSpan.ToString(CultureInfo.InvariantCulture));
                    renderer.Write('"');
                }

                renderer.Write('>');

                bool previousImplicitParagraph = renderer.ImplicitParagraph;
                if (cell.Count == 1) {
                    renderer.ImplicitParagraph = true;
                }

                renderer.Write(cell);
                renderer.ImplicitParagraph = previousImplicitParagraph;

                renderer.WriteLine(row.IsHeader ? "</th>" : "</td>");
            }

            renderer.WriteLine("</tr>");
        }

        if (hasBody) {
            renderer.WriteLine("</tbody>");
        } else if (headerIsOpen) {
            renderer.WriteLine("</thead>");
        }

        renderer.WriteLine("</table>");
    }
}
