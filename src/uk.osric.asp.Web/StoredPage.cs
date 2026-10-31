// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Core;

/// <summary>A page's source together with the compile result stored beside it.</summary>
/// <param name="Path">The canonical key the page is stored under.</param>
/// <param name="Content">The template source.</param>
/// <param name="Compilation">
/// The stored compile result, or <see langword="null"/> when the page has never been compiled by
/// this build. A page without one is compiled on its next request.
/// </param>
public sealed record StoredPage(PagePath Path, string Content, PageCompilation? Compilation = null);
