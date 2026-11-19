<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot move a page

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [page-move](../plans/page-move.md) |
| Area | Sdk, Web (pages, storage), docs |
| Location | `src/uk.osric.asp.Sdk/IPageWrites.cs`, `src/uk.osric.asp.Web/Pages/PageWritesScope.cs`, `src/uk.osric.asp.Web/Editor/PageEditor.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs`, `src/uk.osric.asp.Web/Pages/PageRevision.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

A key is a page's identity: its history is filed under it, a library's closure names it, a link points
at it and a seed's provenance is anchored to it. Nothing moves one. The workaround is a save under the
new key and a delete under the old, which is two revisions, two histories, an anchor left behind and
every inbound link broken silently — the site keeps serving a page whose name no longer describes it,
and the only evidence is a tombstone.

The wider class is "the store's shape changes": a page renamed, a set of pages moved under a new
folder, a seed re-slugged by a release. It is a store operation with a validity rule, not a view of one,
and every caller that needs it today does it badly:

- **The editor** offers a save and a delete, so an author does it by hand and says the history is what
  it is.
- **The assistant** has `write_page` and `delete_page` and would do the same two calls, and its
  edit-safety rules are about one key at a time (`assistant-edit-page-is-safe`).
- **A release** cannot move a seeded page at all: the seed's key is its identity
  ([seed provenance](../plans/archive/seed-provenance.md)), so a renamed seed is a new page with a new
  anchor and an orphan on the old
  key.
- **The IDE's tabs** hold keys in a URL, so a move leaves a tab pointing at a tombstone until the
  visitor closes it — an answer this job can give the page (the move, and what happened to the old key)
  but cannot give the URL.

## Sketch

1. **One member on the write capability.** `MoveAsync(from, to, expectedHash)` on the capability that
   already edits other keys, answering a result the way a save does: moved, or refused with the
   sentence that says why. `expectedHash` is the head the caller believed the source had, so a move
   cannot be applied to a page that changed underneath it — the same conditional save the editor
   already relies on.

2. **One transaction.** Move the head row and its history under the new key in one write: the revisions
   keep their hashes (a hash is an identity of content, not of key), the seed's anchor follows the
   content, and the old key is left as a tombstone with its history — or as nothing, which is the open
   question below. Nothing is recompiled: the dependent's closure names libraries by key, and a moved
   *page* is not in anyone's closure.

3. **The refusals are the store's own rules.** A reserved key, a key that already has a live head, a key
   that names a folder the move would empty, and the page the move is running in. The editor's own key
   is an ordinary key now, so moving it is allowed and `/admin/seeds` puts the seeded editor back
   (`pages-any-page-may-write-the-store`). A move to the same key, or a case-only rename, is a question
   the result answers rather than an exception.

4. **A library is the hard case.** Moving a library changes every dependent's closure: the dependents
   keep naming the old key and go stale, or the move recompiles them all, or the move is refused while
   dependents exist. That choice needs [`library-edit-cost-is-invisible`](closed.md) to know who the dependents are, and the
   answer is probably "refuse, name them, and let the caller move them too" — with a batch
   ([IDEA-043](closed.md)) making that one request.

5. **A move is not a rewrite.** Links inside other pages still point at the old key. Rewriting them is a
   search plus a batch save ([`cannot-search-the-store`](cannot-search-the-store.md), [IDEA-043](closed.md)), and it edits pages the
   caller may not have opened — a decision of its own, deliberately not bundled here.

6. **The documentation** is a `/help` paragraph for the capability (`product-manual-is-a-page`), a
   `PageAuthorPrompt` sentence with its `Version` bumped so the assistant moves rather than copies, and
   the storage section of [storage](../storage.md) for the transaction.

## Open questions

1. **A tombstone at the old key, or nothing.** A tombstone is how the store already records "this key
   had a page", and it makes a stale link render the deleted-page answer rather than a missing key. A
   *moved* marker needs a column (an ordinary row work, beside the anchor IDEA-036 landed), and it is the
  only way a visitor learns
   where the page went. Nothing at all is honest but loses the trail.
2. **Whether the history moves or is copied.** One history under the new key is the simple answer; a
   retained copy under the old key costs nothing but bytes and confuses "the history of this key". The
   store's rule is that history survives a delete, so a move that empties the old key's history is a
   second kind of forgetting.
3. **Whether `MoveAsync` is one member or `SaveManyAsync` with a rename row.** A batch of edits is
   [IDEA-043](closed.md)'s value; a rename is not an edit and does not compile. Keeping them apart is
   probably right, and it means a batch cannot rename.
4. **Case-only renames.** SQLite's default collation makes `/Blog` and `/blog` the same row to one query
   and different rows to another; the store already normalises keys, so a case-only rename may be a
   no-op it has to say out loud.

## Not in scope

- **Moving a folder.** That is a batch of moves, and it should be built from this member rather than as
  a second operation.
- **Rewriting links.** See the sketch; it needs a decision about editing pages the caller did not open.
- **An alias or redirect.** A key that 301s to another is a response rule and a store column; the
  nearest honest version is the tombstone, and a redirect is a different job with a different security
  story.
- **A rename in the release's own seed pack.** That is [IDEA-036](../plans/archive/seed-provenance.md)'s harvest and adopt
  round trip, which this member makes safe rather than necessary.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan names "rename or move a page" with the
workaround "done deliberately, with the inbound links edited by hand"; the same gap is visible from the
assistant's tools and from a release that renames a seed.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 6 there, last because it
needs the closure ([`library-edit-cost-is-invisible`](closed.md)), the search ([`cannot-search-the-store`](cannot-search-the-store.md)) and the batch
([IDEA-043](closed.md)) to be honest.
