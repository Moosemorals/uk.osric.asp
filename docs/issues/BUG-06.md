<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-06 — An unterminated code block produces a generic 500

| | |
| --- | --- |
| Severity | Medium |
| Area | Core, error handling |
| Location | `src/uk.osric.asp.Core/TemplateTokenizer.cs:30`, `src/uk.osric.asp.Web/ErrorEndpoint.cs:12-18` |

## Problem

`TemplateTokenizer.Tokenize` throws `ArgumentException("Unterminated code block.")` when a `<%` has no
matching `%>`. That happens before compilation, in `GenerateTemplateSource`, so it is not reported as
a compile diagnostic; it escapes to the exception handler.

In production the error page shows "An unexpected error occurred. Please try again." because the
exception is not a `UserFacingException`. The author sees a generic failure instead of the one piece
of information that matters. Verified: a page containing `<% if(true) { ` returns 500 with the
generic message.

## Fix outline

1. Decide the contract: a malformed template is a *user* error, not a server fault. Make the
   tokenizer throw `UserFacingException` (it lives in `Core`) with the safe message, **or** return a
   result type (`TemplateParseResult` with errors) that `MainEndpoint` renders like a compile error.
2. Prefer the result type: it removes exceptions from normal input handling and lets the editor show
   the same message inline.
3. If an exception is kept, include the template position so the message is actionable.
4. Add a unit test asserting the message is user-facing, and a web test asserting it is displayed.
