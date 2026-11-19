// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>A piece of a page template: text, code, a comment, declarations, or an escaped or raw write.</summary>
/// <param name="Type">What kind of content the token contains.</param>
/// <param name="Value">The token's content, without its delimiters.</param>
/// <param name="Position">Where the content starts, in template space.</param>
/// <param name="OpenerPosition">
/// Where the token's opener starts, or <see langword="null"/> for text, which has none. A shape
/// diagnostic names the block by its opener rather than by its content, so a block whose opener
/// stands on another line from what it holds still points at the line the author will look at.
/// </param>
internal readonly record struct Token(
    TokenType Type,
    string Value,
    TemplatePosition Position,
    TemplatePosition? OpenerPosition = null);
