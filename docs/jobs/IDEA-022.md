<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-022 — `SimpleContext` is one class doing five jobs

| | |
| --- | --- |
| Size | S |
| Area | Web |
| Location | `src/uk.osric.asp.Web/Pages/SimpleContext.cs` |

## Idea

`SimpleContext` is 364 lines implementing the whole page contract: response buffering and the body
cap under a lock, status/content type/headers, logging, redirects, antiforgery, the fetch budget and
delegation, storage, the deadline, the abandonment flag and the final `ToResponse`. Finding one of
those is a scroll, and moving one is a risk to the rest.

The [page-features plan](../plans/archive/page-features.md) moved storage and fetch out first, which
took roughly a fifth of the file with them. Re-measure before doing anything else: if what is left
is still the hard-to-navigate shape, split it.

## Sketch

1. Split by concern into partials beside the main file — the response surface (status, content
   type, allowed headers, `AllowScripts`), the body (buffer, lock, cap, `GetBody`), and the
   request/identity/deadline core. Partials keep one type and one constructor, so no call site
   changes and no DI wiring moves.
   - Name the files for the concern, not the member: `SimpleContext.Response.cs` rather than
     `SimpleContext.SetStatus.cs`.
   - Keep the fields in the main file even when only a partial uses them, or the "where is the
     state" question just moves.
2. Or compose instead: the capabilities change proves the shape — a small collaborator object with
   an interface, constructed by the endpoint, handed to the page through `IPageCapabilities`. If a second
   concern wants that shape, take it rather than a partial, because it is unit-testable on its own
   and a partial is only a layout of one file.
3. The real target is that `SimpleContextTests` can construct the part it tests without a full
   context. If a split does not get there, it was cosmetic.

Whichever way it goes, the page contract is unchanged, so this is a `Web`-only change with no
`RuntimeId` effect and no recompilation of stored images.
