// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>One page an archive carries: its key, its source, and the state stored beside it.</summary>
/// <param name="Path">The canonical key. It is written unchanged; export never rewrites a key.</param>
/// <param name="Source">The template text exactly as stored.</param>
/// <param name="State">The page's state values, in ordinal key order; empty when it has none.</param>
internal sealed record PageArchiveEntry(PagePath Path, string Source, IReadOnlyList<PageStateValue> State) {

    /// <summary>The source size in UTF-8 bytes, which is what the archive file pays for it.</summary>
    public int SourceBytes => Encoding.UTF8.GetByteCount(Source);

    /// <summary>How many state keys the page carries.</summary>
    public int StateKeys => State.Count;
}
