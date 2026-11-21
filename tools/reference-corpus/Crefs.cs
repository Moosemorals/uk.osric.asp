// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using System.Xml.Linq;
using uk.osric.asp.Web.Documentation;

/// <summary>Turns every <c>cref</c> in the corpus into a link, or into a name and a Learn link.</summary>
/// <remarks>
/// One pass over every document, after inheritdoc has copied text, so a cref inside copied text is
/// resolved exactly like one the author wrote.
/// </remarks>
internal static class Crefs {

    /// <summary>Annotates every <c>see</c> and <c>exception</c> in place.</summary>
    /// <param name="documents">The corpus, already normalised.</param>
    /// <param name="entries">Every entry of every document, by documentation id.</param>
    public static void Annotate(
        IReadOnlyList<ReferenceDocument> documents,
        IReadOnlyDictionary<string, (ReferenceDocument Document, ReferenceEntry Entry)> entries) {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(entries);

        foreach (ReferenceDocument document in documents) {
            foreach (XElement element in document.Content.Descendants()
                .Where(item => item.Name == "see" || item.Name == "exception")) {
                if ((string?)element.Attribute("cref") is not { Length: > 0 } cref) {
                    continue;
                }

                bool hasContent = element.Nodes().Any();
                if (entries.TryGetValue(cref, out (ReferenceDocument Document, ReferenceEntry Entry) target)) {
                    element.SetAttributeValue("href", ReferenceUrls.Page(target.Document, target.Entry));
                    if (!hasContent) {
                        element.SetAttributeValue("text", target.Entry.Text);
                    }
                } else {
                    string text = ReferenceDisplay.CrefText(cref);
                    if (element.Name == "exception" || !hasContent) {
                        element.SetAttributeValue("text", text);
                    }

                    if (ReferenceDisplay.LearnUrl(cref) is { } learn) {
                        element.SetAttributeValue("href", learn);
                    }
                }
            }
        }
    }
}
