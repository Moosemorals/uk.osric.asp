<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-09 — The specification suite describes the compiler, not business behaviour

| | |
| --- | --- |
| Severity | Medium |
| Area | Testing, specifications |
| Location | `test/uk.osric.asp.Specs/Features/Compiler.feature`, `test/uk.osric.asp.Specs/Steps/CompilerSteps.cs` |

## Problem

`docs/testing.md` says specifications "describe *what* the site does in plain language" and "double
as executable documentation". The only feature now says:

> Compile valid source / Reject invalid source

That is a unit test of `TemplateCompiler` written in Gherkin. No specification covers the domain the
site actually implements: rendering a stored page, reporting a template error, editing and saving a
page. The greeting feature was deleted and left only its generated code-behind behind (BUG-11).

The result is that the business behaviour has no executable description, and a low-level tool is
described twice (unit tests and specs) while the product is described nowhere.

## Fix outline

1. Move the compiler scenarios into `uk.osric.asp.UnitTests` (or the future runtime test project) —
   the behaviour is already covered by `TemplateSourceGeneratorTests` and the `EmitResult` assertion
   is a unit concern.
2. Write page-level features in the domain language, for example:
   - "Render a stored page": given a stored template, when it is requested, then the response
     contains the rendered output;
   - "A broken page reports an error": given a template with a syntax error, then the response is a
     500 and the error page is shown;
   - "Save a page": given the editor, when valid content is posted, then it can be requested back.
3. Let step definitions call the shell at its seam — an in-process host (ARCH-08) or the endpoint
   handlers with a fake `IStorage`, not the Roslyn compiler directly.
4. Delete `Compiler.feature` once moved, and update `docs/testing.md` to state that specs describe
   the site, not its libraries.
