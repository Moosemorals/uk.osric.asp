// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>How a search reads the text it was given: as itself, or as the shell's pattern language.</summary>
/// <remarks>
/// A literal is the characters themselves, so a search for <c>foo.</c> finds those four characters.
/// A basic regular expression is the language the assistant's shell uses: <c>^</c>, <c>$</c>, <c>.</c>,
/// <c>*</c> and <c>[abc]</c> are metacharacters, <c>\|</c> is alternation, and everything else —
/// <c>(</c>, <c>)</c>, <c>+</c>, <c>?</c>, <c>{</c> included — is the literal character. A pattern is
/// compiled when the search runs, before a source is read, and a construct the host cannot carry is
/// refused then.
/// </remarks>
public enum PageSearchPatternKind {

    /// <summary>The pattern is the text itself; no character is syntax.</summary>
    Literal,

    /// <summary>The pattern is a basic regular expression, matched in linear time.</summary>
    BasicRegularExpression,
}
