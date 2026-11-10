<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-026 — Remove the deprecated `Features` alias

| | |
| --- | --- |
| Size | S |
| Area | Sdk, Web |
| Location | `src/uk.osric.asp.Sdk/IPageFeatures.cs`, `src/uk.osric.asp.Sdk/IContext.cs` |

## Idea

DEC-228 renamed the page contract's capability seam and kept the old names for one release:
`IPageFeatures` is an empty `[Obsolete]` interface deriving from `IPageCapabilities`, `IContext.Features`
returns it with a `PAGE005` warning, and `PageCapabilities` implements both behind a suppression. That
was the only way to move stored pages without an outage, and it was always meant to end.

The alias goes when the page store no longer needs it — not on a date. Until then a page that says
`ctx.Features` keeps serving with a warning, and every new page is written clean because `/help` and
the assistant prompt teach `ctx.Capabilities`.

## Sketch

1. Decide by data: query every head whose source still names the old spelling.
   `SELECT head.Path, revision.Hash FROM PageHeads AS head JOIN PageRevisions AS revision ON revision.RevisionId = head.RevisionId AND revision.Path = head.Path WHERE revision.Template LIKE '%ctx.Features%' OR revision.Template LIKE '%IPageFeatures%'`
   — the columns are `Path`, `RevisionId` and `Template`, described in
   [storage](../storage.md#source-storage). Restore each hit from its seed or edit it through its own
   page, then run the query again until it is empty.
2. Delete `src/uk.osric.asp.Sdk/IPageFeatures.cs` and `IContext.Features`; drop the
   `PageCapabilities` second interface, the `SimpleContext.Features` property and both
   `#pragma warning disable PAGE005` blocks. `PageCapabilityDeprecation` goes with them.
3. `PublicAPI.Shipped.txt` keeps its historical lines; `PublicAPI.Unshipped.txt` gains
   `*REMOVED*` lines for `IContext.Features.get`, `IPageFeatures` and its inherited members, so the
   removal is a deliberate edit the analyzer sees.
4. Delete `APageThatSaysFeaturesCompilesAndWarns`, `TheDeprecatedWarningCarriesTheTemplatePosition`,
   `BothAccessorsHandOutTheSameRegistry`, `TheOldNamesAreMarkedObsolete` and
   `AStoredPageThatSaysFeaturesStillServesAndWarnsAsync`; rename `APageThatSaysCapabilitiesCompilesWithoutWarnings`
   to say the new spelling is the only one.
5. Drop the "old name still works" paragraph from the seeded `/help`, and move `page-author-v18` to
   `v19` if the prompt still mentions the alias.
6. DEC-228's alias sentence is superseded by a new row, not edited: the record says what the code said
   at the time.

The `RuntimeId` changes when `Sdk` changes, so this release recompiles every stored image — which is
the reason the query in step 1 is the gate rather than a courtesy.
