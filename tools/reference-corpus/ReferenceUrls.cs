// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace ReferenceCorpus;

using uk.osric.asp.Web.Documentation;

/// <summary>The site path an entry is linked by.</summary>
/// <remarks>
/// An assembly with one namespace serves that namespace at <c>/admin/reference/{slug}</c>; one with
/// several serves a namespace index there and each namespace below it. The rule is here so a cref
/// and an inherited-documentation link cannot disagree.
/// </remarks>
internal static class ReferenceUrls {

    /// <summary>The page and fragment that answer for one entry.</summary>
    /// <param name="document">The document the entry belongs to.</param>
    /// <param name="entry">The entry to link to.</param>
    public static string Page(ReferenceDocument document, ReferenceEntry entry) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entry);

        string space = entry.Namespace.Length == 0
            ? document.Info.AssemblyName
            : document.Info.AssemblyName + "." + entry.Namespace;
        bool single = document.Content.Root?.Elements("namespace").Count() == 1;
        string prefix = single
            ? "/admin/reference/" + document.Info.Slug
            : "/admin/reference/" + document.Info.Slug + "/" + space;
        return prefix + "#" + entry.Anchor;
    }
}
