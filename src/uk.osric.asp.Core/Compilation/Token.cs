// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>A piece of a page template: text, code, declarations, or an escaped or raw write.</summary>
/// <param name="Type">What kind of content the token contains.</param>
/// <param name="Value">The token's content, without its delimiters.</param>
/// <param name="Position">Where the content starts, in template space.</param>
/// <param name="OpenerPosition">Where a write token's opener starts, or <see langword="null"/> otherwise.</param>
internal readonly record struct Token(
    TokenType Type,
    string Value,
    TemplatePosition Position,
    TemplatePosition? OpenerPosition = null);
