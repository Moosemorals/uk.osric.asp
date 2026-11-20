<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A listing cannot say the store moved under it

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | medium |
| Status | planned |
| Blocked by | — |
| Plan | [listing-fingerprint](../plans/listing-fingerprint.md) |
| Area | Core, Sdk, Web (pages, assistant), docs |
| Location | `src/uk.osric.asp.Core/Pages/`, `src/uk.osric.asp.Sdk/PageListing.cs`, `src/uk.osric.asp.Sdk/PageListingRequest.cs`, `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Assistant/PageStoreReader.cs`, `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs` |
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
   migration and the same bump discipline, and it is the version a real tree wants. *The owner chose
   this semantics on 2026-11-20 and the version derived rather than stored — see the answers below.*
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
- **There is no write path to change.** A stored sequence would make each write's bump the work, and
  a bump belongs inside the transaction the write already has, so a refused write leaves the version
  alone and the rollback path is the one most likely to be forgotten. Deriving the version from the
  rows removes the whole class: no write participates, so no write can forget.

## What the owner answered

Put to the owner on 2026-11-20, before any plan was written.

1. **Per prefix, not store-wide.** A walk of `/blog` is not invalidated by an edit under `/assistant`,
   which is the traffic this site actually has: the assistant writes while the owner reads. The item's
   open questions 3 and 4 fall out of it — a delete is a change, and the token is not a security claim.
2. **A warning, not a refusal.** `PageListing.Changed` states the fact and the caller decides; a host
   refusal inside a capability would be the invisible second gate `pages-reading-the-store-is-public`
   rejects.
3. **The version is derived, not stored.** The owner's Merkle intuition was right about the aggregate
   and wrong about the tree: nothing verifies membership, so a tree buys cost without a customer. The
   aggregate is computed from the heads when a slice is read — no column, no counter, no migration and
   no write path to forget, which is what makes an adopt and a content-hash repair visible without
   anyone being told about them.
4. **Seeds are untouched.** The three-hash verdict stays; a seed write and an adopt move the derived
   token for free. The plan's §Seeds answers this at length.

The plan is [listing-fingerprint](../plans/listing-fingerprint.md), which also carries the measured cost
of the derived answer against keeping a sequence on write, and the threshold at which that would change.

## Not in scope

- **A snapshot for the life of a walk.** Rejected above, and it would be a decision of its own.
- **A search cursor's version.** [`cannot-search-the-store`](cannot-search-the-store.md) has the same
  gap one layer up and carries the same token rather than inventing a second signal: it waits on this
  item, and a walk that finds the store moved starts again.
- **A page's history.** `RevisionsAsync` is bounded by saves rather than by the store, and its order
  cannot move; nothing is needed there.

**Origin:** the owner's question of 2026-11-19, reviewing the cursor slice
([`listing-has-no-cursor`](closed.md)): "how hard would it be to give someone paging through
something like a version token, so the list doesn't change underneath them while paging — or at least
doesn't change without warning". The answer then was that the read is cheap and the write paths are
the cost; this item is where that answer is kept until it is wanted.
