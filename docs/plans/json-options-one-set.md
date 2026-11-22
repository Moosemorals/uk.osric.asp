<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One set of JSON options

| | |
| --- | --- |
| Status | Proposed 2026-11-22 — the owner asked for the consolidation and for one copy of each variation in a type in Core. Nothing below is built; it waits for a yes |
| Size | Small: one type, one instance, and the call sites that stop naming their own |
| Decisions | The merge records **code-json-options-are-one-set** |
| Backlog | — (the owner asked for the change directly) |

## The problem

Seventeen `JsonSerializerOptions` instances live in seventeen files, in ten configurations. Five are
byte-for-byte copies of the template in `Core.Text.JsonSerialization` and add nothing. Three differ
only by accident: the two halves of an interrupt row were written with different encoders and
different null policies, and the page store's diagnostics and the record catalogue's columns were the
only PascalCase shapes in the product. The corpus writer and its reader, in two projects, each
carried the same seven lines.

## The shape

`JsonSerialization` keeps its encoder and gains the one instance every call site names. Web defaults
give camelCase names and case-insensitive reads; the relaxed encoder stops escaping markup and
non-ASCII; `JsonStringEnumConverter` writes an enum by its name. Nulls are written, because the tool
envelope and `/health` document their absent members and omitting them would change what a reader
sees.

The provider body needs no variation: its field names are dictionary keys, written verbatim, and the
anonymous objects it adds are single lowercase words. `ChatRequestJsonTests` pins the body, which is
what keeps the snake_case names snake_case.

`/health` loses its indentation and becomes one line. `Diagnostics` columns written from now on are
camelCase; the reader is case-insensitive, so older rows still load. Problem documents gain explicit
`"type": null` and `"detail": null`, because the null policy is now one policy.

The store is forward-only across this release. An older build reads a listed row with
`JsonSerializerDefaults.General`, which is case-sensitive, so a revision compiled by this build shows
no diagnostics and a declaration written by it names no columns; rolling back needs those revisions
recompiled. Every other stored shape kept its names, and the encoder only ever affected writing.

A page may still build its own options: its types and its API are its own.

## What changes

| Where | Change |
| --- | --- |
| `Core/Text/JsonSerialization.cs` | The one instance, documented with what it is and why each setting is there |
| `Core/Assistant/ChatRequestJson.cs`, `Core/Assistant/OwnerQuestions.cs`, `Core/Assistant/ToolResultMasker.cs` | The private options go; the masker writes through the site's encoder |
| `Web/Errors`, `Web/Health`, `Web/Assistant`, `Web/Assistant/Tools`, `Web/Pages` | The private options go, including five no-op copies; every store and response names the one instance |
| `Web/Metrics/PrometheusSource.cs` | Reads through the one instance |
| `tools/reference-corpus/Program.cs` | Writes the corpus through the same instance its reader uses |
| `.github/copilot-instructions.md`, `docs/architecture.md` | The rule, so the next variation is a reviewed decision rather than a new field |
| `docs/decisions.md` | The entry, written by the merge |

## Tests

- `JsonSerializationTests` — an enum is written by name.
- `ToolResultMaskerTests` — a masked call keeps its non-ASCII and markup text, and the stored row is
  written by the same serializer as the request body.
- `OwnerQuestionsTests` — a stored question keeps its text unescaped.
- `ReportWriterTests` and `HealthEndpointTests` — the status is present in the compact body.
- The existing round-trips prove the rest: diagnostics, page values, interrupt payloads, the provider
  body and the corpus.

## Open questions

None. They were answered before this was written.
