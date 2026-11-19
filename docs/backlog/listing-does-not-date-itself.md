<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A listing cannot say the store moved under it

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Storage, Sdk, Web (pages, assistant), docs |
| Location | `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Sdk/PageListing.cs`, `src/uk.osric.asp.Sdk/PageListingRequest.cs`, `src/uk.osric.asp.Web/Assistant/PageTools.cs` |
| Spike | — |

## Idea

A listing resumes from a key (`pages-a-listing-resumes-from-a-key`), so a caller can walk a store of
any size one slice at a time. What it cannot do is tell whether the thing it is walking changed
between two slices. A page saved under the cursor is skipped; a page saved above it can be seen
twice; a delete removes a row the caller was about to reach. Every one of those is silent, and the
caller's only defence is to compare what it has, which a tree of a few hundred rows can do and a
list of a few thousand should not have to.

The cursor answered "where am I". This is the other half: "is this still the list I started on".

## What a token can and cannot be

It is worth saying plainly, because the obvious design oversells itself. A version token is an
**optimistic signal**: it can say *the store moved since you started*, and it can say *the store moved
since your last slice*. It cannot stop a row you have not reached from moving, because the rows are
read per request.

Making the list genuinely immutable for a caller means holding a read snapshot across HTTP requests.
SQLite in WAL mode can do it — a `BEGIN` on one connection holds a snapshot — but it pins a reader,
its pages and its WAL for the life of the pager's walk, which for a browser tab can be minutes. That
is a pool with a queue at the end of it, for a listing. It is recorded here as the rejected
alternative, not the design.

So the deliverable is a warning, and the honest ones are:

1. **A store-wide version.** One row holding a monotonic counter, bumped in the same transaction as
   every head write: save, delete, restore, seed install and adopt, archive import, scrub. A slice
   carries the version it read; the answer carries the store's current one, and a difference is the
   warning. Cheap on the read, and its whole cost is that **every write path must bump**, or the
   token lies — a write that forgets is worse than no token, because it is a token that says "safe".
2. **A version per prefix.** Each head carries a `ChangeSeq`, and a slice answers the largest one in
   its subtree. A walk of `/blog` is then not invalidated by an edit under `/assistant`, which is the
   common case on this site: the assistant writes while the owner reads. It costs a column, a
   migration and the same bump discipline, and it is the version a real tree wants.
3. **A cheap heuristic.** The head count, the largest `SavedAt`, or SQLite's `PRAGMA data_version`.
   Each is a lie in a different way — a delete changes neither of the first two, and `data_version` is
   per connection and moves for writes in another store — so none of them is proposed.

Whatever is chosen, the version has to be read **in the same transaction as the rows**, or the answer
names a version its own slice did not come from.

## Sketch

- **The value carries it.** `PageListingRequest` gains the version the caller started from, and
  `PageListing` gains the version it read and whether the two differ. Both records are young and
  unshipped, so the contract moves without a deprecation.
- **What the caller does with it is the caller's business.** A tree may redraw and carry on, an editor
  may refuse to continue, and a one-shot list ignores it. The host answers; it does not decide.
- **The assistant gets it too.** `list_pages` already pages with `after`; a token there is what lets a
  run notice that a page it was about to write moved under its own read — which is the same fact
  `edit_page`'s expected hash answers one page at a time.
- **The write paths are the work.** Each write's bump belongs inside the transaction it already has,
  so a refused write leaves the version alone, and each path needs a test that says so. The rollback
  path is the one most likely to be forgotten.

## Open questions

1. **Store-wide or per prefix.** The first is a row and a bump; the second is a column, a migration
   and a query. The second is the one that stops an unrelated edit from crying wolf, and the site's
   own traffic — the assistant writing while the owner reads — is what makes that matter.
2. **A warning or a refusal.** `PageListing.Changed` says the fact; whether a slice in flight should
   then be refused with a restart rather than answered is a caller's policy, and both are reasonable.
3. **Whether a delete is a change for a walk in progress.** It removes a row the caller would have
   seen, so yes — which is what makes the store-wide counter necessary rather than a page's own head
   hash.
4. **Whether the token is a security claim.** It says the store moved, not what moved, and it must
   not become a way to watch another visitor's writes: a page may already list anything it may read
   (`pages-reading-the-store-is-public`), so the answer discloses no more than the listing does.

## Not in scope

- **A snapshot for the life of a walk.** Rejected above, and it would be a decision of its own.
- **A search cursor's version.** [`cannot-search-the-store`](cannot-search-the-store.md) has the same
  gap one layer up; it can carry the same token once this exists.
- **A page's history.** `RevisionsAsync` is bounded by saves rather than by the store, and its order
  cannot move; nothing is needed there.

**Origin:** the owner's question of 2026-11-19, reviewing the cursor slice
([`listing-has-no-cursor`](closed.md)): "how hard would it be to give someone paging through
something like a version token, so the list doesn't change underneath them while paging — or at least
doesn't change without warning". The answer then was that the read is cheap and the write paths are
the cost; this item is where that answer is kept until it is wanted.
