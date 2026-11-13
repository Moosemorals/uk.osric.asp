<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-07 — Retained page history has no restore UI

| | |
| --- | --- |
| Severity | Medium |
| Area | Storage, assistant, product |
| Location | `src/uk.osric.asp.Web/Storage/StoredRevision.cs`, `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs` |

## Problem

Every save is retained as a separate revision. The editor can browse a page's revision hashes,
timestamps and compilation state, and inspect any revision's saved source. It cannot yet restore a
revision, so recovering from a mistaken save still requires copying its source into a new edit.
The site's content is code, which is exactly the content most likely to need a reviewable rollback.

The assistant writes and tombstones pages, and reaches two retained revisions through `diff_page`,
but it cannot list a page's history or read one revision, so a change it made by mistake is
invisible to it after the fact.

## Fix outline

The [editor tools and retained revisions plan](../plans/archive/editor-tools-and-immutable-assets.md)
delivered the storage foundation together with ARCH-06, ARCH-10 and ARCH-11:

1. Every save appends a row with source, optional compilation/debug data and a unique revision hash
   derived from source, creation runtime id, timestamp and a unique save identity. A transactional
   head pointer selects the current revision; compilation metadata is not the revision identity.
2. Keep source and revision identity indefinitely unless explicitly scrubbed. The supported metrics
   report logical revision payload, whole-database allocated pages and containing-volume capacity;
   they do not claim exact per-table allocation. Review policy only when payload exceeds half the
   volume, with no automatic pruning.
3. Delete tags the head as a tombstone. Recreating the canonical path shares its old history.
   Only explicit scrub wipes that path and every retained revision, with the secret-cleanup
   limitations and SQLite cleanup requirements documented in the combined plan.
4. Migrate existing source, images, debug symbols and diagnostics without loss.

The first history-browsing slice is implemented:

1. Authenticated `GET /edit/<path>?cmd=history` lists retained hashes, save times, compilation
   status and creation runtime, including for tombstoned paths. The listing query does not load
   source or binary BLOBs.
2. Selecting a hash opens `cmd=revision` and displays only that revision's HTML-encoded saved
   source and metadata. It neither compiles nor executes the page; hashes from other paths are
   rejected. Both views send `Cache-Control: no-store`.

The remaining work is restore:

1. Add a confirmed, antiforgery-protected `cmd=restore` POST selecting a revision belonging to that
   path. Restore saves its source as a new revision under the current runtime, preserving history;
   it does not rewind or mutate an old row. Restoring a deleted path clears its tombstone.
2. Test that a revision from another path cannot be restored and restore appends a distinct
   revision even when the restored source is identical.

## The assistant reads history and never writes it

The assistant may create, edit, replace and tombstone a page, and its `diff_page` already reads two
retained revisions to compare them, but nothing lets the model list a page's revisions or read one.
So after a mistaken `write_page` or `delete_page` — which is how this gap was found — the model that
made the change cannot say what the page used to hold, and the owner has to open the editor to find
out. The page-facing capability has had both reads for a while (`IPageCatalog.RevisionsAsync` and
`ReadRevisionAsync`); the tools have not.

Two read-only tools close it:

1. `list_revisions` takes a `path` and answers the history `PageEditor.ListRevisionsAsync` already
   returns, newest first: each revision's hash, save time, creation runtime id, image state, whether
   it is the head, and the editor URL for that revision (`PageUrls.EditorRevision`). It lists a
   tombstoned path too, because that is the answer a caller most often needs, and it says when the
   listing was capped rather than trimming it silently.
2. `read_revision` takes a `path` and a `hash` and returns that revision's stored source, with the
   1-based physical line numbering, line-range selection and result budget `read_page` uses
   (DEC-112), plus `editorUrl`. A hash that is not 64 hexadecimal characters, or that belongs to
   another path, is refused rather than served — the same rule the editor's `cmd=revision` applies.

Both go through the gate `read_page` repeats (`ToolPaths.ResolveRead`, plus a refusal for a shell
file, which has no revisions), declare `Writes = false`, and need no approval: they change nothing.
Neither can restore, scrub or save — the editor's restore stays the owner's, as the assistant plan
already decided — so what the model gains is the ability to quote a revision accurately and name the
one the owner should restore. Reading a revision sends its source to the provider, exactly as
`read_page` does, so the description says so rather than leaving it to be inferred.

The answers come from the two reads `PageEditor` already exposes, so no query and no second shape of
read is added (ARCH-16). Nothing here reaches for the `?v=` token: that is a public, cacheable URL
for one live, code-free asset ([IDEA-010](IDEA-010.md)), and it cannot answer for a revision with
code, for a tombstoned path or for a wrapped page. A tool that wants a revision reads the store.

**Done when:** `Web.Tests` proves the listing's order and head mark, that a tombstoned path both
lists and reads, that a foreign hash and a malformed one are refused, that the line range and the
budget behave like `read_page`'s, that a shell file is refused, and that no variant writes;
`ToolRegistry` registers both where its declared order is the provider's cached prefix, so
`PageAuthorPrompt` gains the section and its version bumps; `PageAuthorPromptTests` pins it;
`docs/assistant.md` counts seventeen tools and names the two reads; and `docs/testing.md` says what
the tool tests prove.

Historical asset execution and PageLayout links are [IDEA-010](IDEA-010.md), not part of this
history UI. A revision address identifies saved code, not necessarily deterministic output.
