// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One matching line: where the match is, and a snippet of the line around it.</summary>
/// <param name="Key">The key the line was read from; a live page or one of the shell's records.</param>
/// <param name="Line">The 1-based line the match is on.</param>
/// <param name="Column">The 1-based column in the line where the match starts.</param>
/// <param name="Text">The snippet: the line cut to the requested context, centred on the match.</param>
/// <param name="TextColumn">The 1-based column in the line where <paramref name="Text"/> starts.</param>
/// <param name="Clipped">
/// Whether <paramref name="Text"/> is a fragment of the line rather than the whole of it.
/// </param>
/// <remarks>
/// The match sits inside the snippet at <c>Column - TextColumn</c>, so a view marks it without asking
/// the host again, and a line with more than one match is still one row — the first match's position
/// and a window around it. A caller that wants the whole line reads it with
/// <see cref="IPageCatalog.ReadAsync"/>.
/// </remarks>
public readonly record struct PageSearchMatch(
    string Key, int Line, int Column, string Text, int TextColumn, bool Clipped);
