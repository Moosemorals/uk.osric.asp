// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>A piece of a page template: either text, a code block or a declaration block.</summary>
/// <param name="Type">Whether the token is text, code or a declaration block.</param>
/// <param name="Value">The token's content, without its delimiters.</param>
/// <param name="Position">Where the token starts, in template space.</param>
internal readonly record struct Token(TokenType Type, string Value, TemplatePosition Position);
