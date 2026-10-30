<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# STYLE-03 — Two top-level types share `TemplateTokenizer.cs`; `Token` is a class record

| | |
| --- | --- |
| Severity | Low |
| Area | Core, code style |
| Location | `src/uk.osric.asp.Core/TemplateTokenizer.cs:8-17` |

## Problem

The conventions say "one top-level type per file, named after the file". `TemplateTokenizer.cs`
declares `Token`, `TokenType` and `TemplateTokenizer`.

`Token` is also a reference-type `record` (`public record Token(TokenType Type, string Value)`), used
as a short-lived value produced by a hot tokenizer. A `readonly record struct` avoids an allocation
per token and better expresses a small immutable value (the project uses structs for exactly this).
`TokenType` is only meaningful with `Token`.

## Fix outline

1. Move `Token` and `TokenType` into `Token.cs` (or `TokenType.cs` and `Token.cs`), keeping
   `TemplateTokenizer.cs` for the tokenizer.
2. Change `Token` to `public readonly record struct Token(TokenType Type, string Value)`.
3. Keep the tokenizer's `yield return new Token(...)` — it still reads well.
4. Update tests only if equality/comparison behaviour changes (record structs compare by value, so
   `Is.EqualTo(new Token[] { ... })` keeps working).
5. Consider `TokenType` nested inside `Token` if the two are never used separately (`Token.Type`),
   though a top-level enum is fine as long as it has its own file.
