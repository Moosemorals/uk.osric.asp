// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>A piece of a page template: either text or a code block.</summary>
/// <param name="Type">Whether the token is text or code.</param>
/// <param name="Value">The token's content, without its delimiters.</param>
/// <param name="Position">Where the token starts, in template space.</param>
public readonly record struct Token(TokenType Type, string Value, TemplatePosition Position);
