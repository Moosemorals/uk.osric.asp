<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-024 — `/admin/import` and `/admin/export` are shell code because an archive is not a form

| | |
| --- | --- |
| Size | Large |
| Area | Sdk, Web, storage |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Web/Admin/`, `src/uk.osric.asp.Web/Pages/` |

## Idea

The two admin views are lists, forms and reports like the memory page was, and
[DEC-210](../decisions.md#architecture-and-code-placement) puts them in the shell's third column: a
page cannot do what they do. Export builds a SQLite archive and hands it to the browser as a
download; import takes that file back as a multipart upload, stages it, shows a per-page review and
then applies it all or nothing.

Everything about that is outside the page contract. A page reads `ctx.Request.Form`, `Query`,
`Headers` and `Cookies` — text values with the body capped and buffered as a form — and it writes
text into a buffered response with a character budget. Neither end is a byte stream. This job is what
would move both routes, and it is worth recording that the interesting shape is **not** a generic
upload and download.

## Sketch

1. **A domain capability, not a file API.** `IMemoryCatalog` and `IPageCatalog` are domain-shaped for
   a reason: a generic `IFileSystem` or `IRequestBody` in `Sdk` would hand page code a byte stream and
   the archive's format, and the export's correctness is `Web`'s (`PageArchiveWriter`,
   `sqlite3_serialize`) rather than a page's. So the capability is
   `ctx.Features.GetRequired<IPageArchive>()` with `Task<byte[]> ExportAsync()`,
   `Task<ArchivePlan> StageAsync(string fileName, byte[] archive)` and
   `Task<PageSaveResult[]> ApplyAsync(ArchivePlan plan)`, refusing an anonymous render exactly as the
   memory writer does (DEC-211's rule).
2. **The caps follow the capability.** `Pages:Archive:MaxUploadBytes` bounds `StageAsync`, and the
   export is bounded by the store; a page never sees the whole page store's bytes in one string, which
   is why the export would have to be a page-level capability rather than `IPageCatalog` plus a loop.
3. **The review step is the hard part.** Import stages an archive, shows what it would change and
   applies it on a second request. The staging table (`ImportStageStore`) belongs to the shell and to
   no owner, and a page's own `IPageStorage` is per path and shared by every visitor, so a plan cannot
   live there. Staging needs an owner-scoped row — a new table and a sweep, the same shape memory
   needed — or the flow becomes one request that applies without a review, which is exactly what
   [the archive plan](../plans/archive/page-import-export.md) refused to do (DEC-139's all-or-nothing
   rule is about the *commit*, not about skipping the review).
4. **What does not change.** The compile-everything-then-commit transaction, the hostile-input
   validation and the archive format are `Web`'s. The page renders the list, the upload form and the
   review table; it decides nothing about what an archive may contain.
5. **The order.** Staging is the design decision; the capability is plumbing once it is settled. The
   review page is what makes this Large rather than a sitting.

## What would make it unnecessary

If the review step were dropped, an import would be one `POST` that applies an archive all or
nothing, and the capability would be a single `ImportAsync(bytes)`. That is a product decision about
how much a destructive action may skip, and it is not this job's to make.

**Origin:** [the page-first plan](../plans/page-first.md) and DEC-210, which named this route as one of
the shell's three exceptions with a job behind it.
