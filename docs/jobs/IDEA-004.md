<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-004 — `Core` is both the page SDK and the host's compiler support

| | |
| --- | --- |
| Size | Large |
| Area | Core, Runtime, structure |
| Location | `src/uk.osric.asp.Core` |

## Idea

`Core` currently holds two different things:

- what a page is written against — `IContext`, `PageRequest`, `PageResponse`, `IHandler`,
  `UserFacingException`, `PageApiPolicy`;
- what only the host needs to turn a template into an assembly — `TemplateTokenizer`,
  `TemplateSourceGenerator`, `Token`, `TokenType`, `TemplateLines`, `TemplateParseResult`,
  `PageDiagnostic`, `ContentFingerprint`.

Page code gets the whole assembly either way, and the only thing keeping the second group out of a
page's reach is the API policy's symbol filter. Splitting them would make the page-facing surface
an explicitly versioned assembly, shrink what a page compiles against, and give "is this allowed in
a page?" a structural answer instead of a list-shaped one.

## Sketch

1. Two projects: `uk.osric.asp.Sdk` (page-facing, still reference-free) and `uk.osric.asp.Core`
   (host support, may reference the SDK). `Runtime` references both; `Web` references both;
   pages compile against the SDK alone.
2. `PageApiPolicy` moves with the compiler support but its allowed-namespace root becomes the SDK's
   namespace rather than `Core`'s.
3. The invalidation coupling matters: the stored image's `RuntimeId` includes `Core`'s module
   version id. After the split it must include the SDK's, because the SDK is what a page binds
   against — get this wrong and every stored image silently stays valid across an incompatible SDK
   change. Look at `ContentFingerprint` and the `RuntimeId` it feeds before moving anything.
4. Re-check the `Runtime` reference set: trimming `Core` out of what a page can see is only real if
   the compiled reference list stops handing pages the host's assembly.
5. No behaviour change is intended: the existing tests should pass with only namespaces edited.
   Migrate in one commit rather than half-splitting the namespace.
6. Every file move needs its SPDX tags carried over, and `docs/architecture.md` and
   `docs/testing.md` need the new project.
