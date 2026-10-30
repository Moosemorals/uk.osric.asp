<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# DOC-01 — `.github/copilot-instructions.md` still describes MVC, Razor and a reference-free core

| | |
| --- | --- |
| Severity | Medium |
| Area | Documentation, agent instructions |
| Location | `.github/copilot-instructions.md:10-15,44-50,86` |

## Problem

The repository-status block says:

- "ASP.NET Core **MVC** (controllers + Razor views) on .NET 10" (line 10);
- `src/uk.osric.asp.Core` is a "functional core: pure functions and values, **no references at all**"
  (line 14);
- `src/uk.osric.asp.Web` is "controllers, Razor views, hosting, configuration" (line 15).

All three are wrong after the endpoint-handler and Roslyn changes: there are no controllers, no
Razor views and no `*.cshtml`; `Core` references `Microsoft.CodeAnalysis.CSharp`; the Web project has
endpoint handler classes. The architecture section repeats "Core must not gain project or package
references" (line ~48), and the deployment section justifies image choices with "MVC does not support
trimming" (line 86).

These instructions steer every agent that works in the repo, so the stale claims actively cause wrong
changes (and would have caught out the runtime work).

## Fix outline

1. Rewrite "Repository status" to describe the page host: tokenizer/generator/contracts, runtime,
   web endpoints, SQLite storage, editor.
2. State the real dependency rule ("Core has no references; Roslyn lives in Runtime", once ARCH-01
   lands).
3. Replace "controllers and views are a thin shell" with "endpoint handlers are a thin shell".
4. Remove the MVC trimming sentence; trimming is still off, but for a runtime/reflection reason, not
   MVC.
5. Add the web test project and the `make test-web`/`test-integration` targets to the testing section.
6. Keep this file in sync when ARCH-01 changes the project layout.
