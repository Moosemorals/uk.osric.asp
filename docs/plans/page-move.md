<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A key moves with its history

| | |
| --- | --- |
| Status | Proposed 2026-11-16 — waits for the owner. Nothing below is built |
| Size | M — one member and one outcome on the write capability, one store transaction, the seeded editor's action, and the tests, manual and prompt for each. No migration: the owner's answer of 2026-11-16 leaves nothing at the old key, which removes the column the job expected |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-16: `IPageWrites`, `PageWritesScope`, `PageEditor`, `PageSaver`, `SqlitePageStore`, `PageHead`, `PageRevision`, `PageTemplate`, `PageSeedInstaller`, `ApplySeedsEndpoint`, `RestoreEndpoint`, `PageTree`, `RevisionHash`, `ToolRegistry` and the migrations |
| Decisions | At the merge: **pages-a-key-moves-with-its-history** is new, and `pages-any-page-may-write-the-store` is edited in place to name the new member |
| Backlog | [`page-cannot-move`](../backlog/page-cannot-move.md) is this step; [`library-edit-cost-is-invisible`](../backlog/closed.md), [`cannot-search-the-store`](../backlog/cannot-search-the-store.md), [`listing-has-no-cursor`](../backlog/closed.md), [IDEA-041](../backlog/closed.md), [`check-without-storing`](../backlog/check-without-storing.md), [IDEA-043](../backlog/closed.md) and [`one-write-per-render`](../backlog/one-write-per-render.md) are its neighbours in [the IDE plan](ide-host.md) |

## The short version

A key is a page's identity, and nothing moves one. `MoveAsync(from, to, expectedHash)` on
[`IPageWrites`](../../src/uk.osric.asp.Sdk/IPageWrites.cs) moves the page, its history and its state
under the new key in one transaction, leaves **nothing** at the old key, and compiles nothing. The
caller names the head it believed the source had, so a move cannot be applied to a page that changed
underneath it, and every answer the store already gives about a save — stale, unchanged, target
occupied — is a value rather than an exception.

Nothing is copied. Every revision is re-filed under the new key in one write and keeps the hash it was
saved with, so the page's history is one history under its new name and a `?v=` link that named a
revision still answers — at the new key. The old key is gone: `/a` answers `404` exactly as any key
that never had a page, which is the owner's decision of 2026-11-16 (§3).

Nothing recompiles. A page is not in any other page's library closure, so no dependent goes stale,
and the stored image is path-independent, so the moved page serves the image it already had.

## What exists today

| | |
| --- | --- |
| The capability | `IPageWrites` is offered to every content layer and to no layout, bound to one render, with one action counter (`pages-any-page-may-write-the-store`) |
| Its members | two saves, `Resolve`, `SeedSourceAsync`, `AdoptSeedAsync`, `DeleteAsync`, `RestoreRevisionAsync`, `InstallSeedAsync` — every one either compile-then-store or a head update, and none changes a key |
| Its checks, in order | anonymous (`UserFacingException`) → `HEAD` → ended or past the deadline → second action → addressable key (`ArgumentException`) → reserved key (`InvalidOperationException`) |
| The store's shape | `PageHeads(Path PK, RevisionId, IsDeleted, SeedHash, ContentHash)`; `PageRevisions(RevisionId PK, Path, Hash unique, SavedAt, Template, CreationRuntimeId, TemplateKind, TemplateLibraries, TemplateHash, RuntimeId, Image, Symbols, Diagnostics)`; `PageState(PagePath, Key)` with no foreign key |
| The store's one oddity | `PageHeads(RevisionId, Path)` → `PageRevisions(RevisionId, Path)`, alternate key, `ON DELETE RESTRICT`. `HeadCannotReferenceARevisionAtAnotherPathAsync` is the test that pins it |
| A hash | `RevisionHash.Of(source, creationRuntimeId, savedAt, revisionId)` — the hash names a **save**, not a page and not content, and it does not include the key. `Hash` is globally unique, and `ContentHash` on the head is the content's identity |
| An image | path-independent: `PageCompiler` names the assembly `Dynamic_{guid}`, the handler is `LoadedPage.HandlerTypeName`, and nothing in `Runtime` reads a key. An image is reused while `TemplateHash` and `RuntimeId` match (`pages-compile-on-save`) |
| A tombstone | a joined head row with `IsDeleted = 1` that still points at its last revision, so its history keeps answering; a deleted key reads as absent, so its URL answers `404` like any missing page |
| The tree | `PageTree` is "below", "directly below" and "the folder above"; a folder is implicit, so a key may be both a page and a folder |
| Seeds and a missing head | `PageSeedInstaller.PlanAsync` calls a key with **no head** a *new* seed and a key that is **tombstoned** a *deleted* one: the seed page offers the new one ticked and the deleted one unticked, and writes whichever the owner ticks. What a move leaves at the old key therefore decides whether a released seed can come back there |
| Not built | the dependents read (040), the search (038), the cursor (039), the span (041), the batch (047), and any `move_page` assistant tool |

Two things follow from that table and they are the whole of the design.

## What the owner decided

| Question | Answer | What it settles here |
| --- | --- | --- |
| What is left at the old key after a move? | **Nothing**: "after moving `/a` to `/b`, `/a` should 404" (2026-11-16) | No marker row and no `MovedTo` column, so no migration, no copy of the head, and the whole history moves — §2 and §3 |

The alternative — a deleted head row that says where the page went — would *also* have answered `404`,
because a tombstone already reads as absent; it was a fact for the store and the listing, not a
response. The owner declined it, so nothing at the old key is remembered and a link that names it is
simply not found.

## The design

### 1. One member, one answer value

```csharp
/// <summary>Moves a page, its history and its state to another key.</summary>
Task<PageMoveResult> MoveAsync(string from, string to, string expectedHash);
```

`expectedHash` is required, never null. A save's `null` expectation means "the form was drawn from a
key with no live source", and a move has no such case: the source must be live, and the caller that
can name `from` can name the hash it read (`IPageCatalog.ReadAsync(from)` is where it comes from, and
the editor's `?edit=` view already carries it).

`PageMoveResult` is the save's shape, not a new vocabulary:

```csharp
public enum PageMoveOutcome {
    Moved,          // the page, its history and its state are under the new key
    Unchanged,      // from and to are the same key once normalised
    Stale,          // the source's head was not expectedHash; nothing was written
    SourceNotFound, // no live page at from
    TargetExists,   // a live page already has the new key
    FolderNotEmpty, // keys live below from, so it is a folder, and a folder is a batch
}

public sealed record PageMoveResult(
    PageMoveOutcome Outcome,
    string? Hash,
    IReadOnlyList<string> Below);
```

`Hash` is the head's hash when the outcome is `Moved` — the hash the page already had, because the head
is re-filed rather than rewritten. `Below` is the keys under `from` when the outcome is
`FolderNotEmpty` — the first few, and a count of the rest, so the page can say what to move first.
Every other outcome leaves both in their empty state.

### 2. One transaction, and the foreign key decides its order

A head row is a pointer to a revision **of its own path**, and the database enforces it. That leaves a
move no room to glide: the head row has to go before its revisions change path, and come back once
they have. It is four statements, and the order is the whole of the design.

```
BEGIN
  read the head at `from` with its revision, and the head at `to`            -- one snapshot
  assert the head at `from` is live and its hash is `expectedHash`
  assert the head at `to` is not live, and `from` has no keys below it
  delete any head at `to` (it can only be a tombstone; its revisions stay)
  DELETE FROM "PageHeads" WHERE "Path" = from AND "IsDeleted" = 0
    AND "RevisionId" = retained                                             -- the expectation
  UPDATE "PageRevisions" SET "Path" = to WHERE "Path" = from                -- every revision, the head included
  UPDATE "PageState" SET "PagePath" = to WHERE "PagePath" = from
  INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted", "SeedHash", "ContentHash")
    VALUES (to, retained, 0, seedHash, contentHash)
COMMIT
```

Four details are load-bearing:

- **The expectation is spent in a statement, not in a read.** The `DELETE` is conditional on the
  revision the head pointed at when the move began, which is `TryStoreRevisionAsync`'s technique: a
  save that landed between the read and the write leaves nothing to delete, so the move rolls back and
  answers `Stale` rather than overwriting the author's edit.
- **The head row goes before the revisions move and comes back after.** There is no point at which the
  foreign key is unsatisfied and no deferred constraint to ask for; the re-key is an `UPDATE` of rows
  that no head points at while it runs.
- **The new head is inserted with the source's two hashes, not upserted.** `WriteHeadAsync`'s
  `COALESCE(excluded."SeedHash", "PageHeads"."SeedHash")` is right for a save and wrong here: a move
  onto a tombstoned key must take the *moved* page's anchor, including when that anchor is `null`
  (unknown), rather than inherit a deleted page's. The target head is deleted first, so nothing of it
  survives.
- **A tombstoned target keeps its history.** Its revisions are already filed under `to` and are not
  touched, so the new key's history is that key's retained revisions joined by the moved page's. That
  is the same rule a save at a tombstoned key already follows.

The transaction takes the render's deadline rather than `CancellationToken.None`. A save outlives its
render because it waits for a compile slot and losing an edit is the worse loss; a move compiles
nothing and is four statements, so a deadline that passes rolls it back, and the caller still has the
form and the hash to try again. "Nothing half-moved" is the promise here, not "never abandoned".

`PageStorageRefresh.Request()` after the commit, as every other write does.

### 3. Nothing is left at the old key

This is the owner's answer, and it is worth stating what it costs as plainly as what it buys.

**It buys:** one history, under the page's new name, with every hash preserved and nothing duplicated.
The old key is indistinguishable from a key that never had a page, so there is no trail to leak an
author's older name, nothing for `/admin/scrub` to scrub, and no second copy of a save to explain in a
history view. And the store gets no column, no migration and no second meaning for `IsDeleted`.

**It costs three things**, and the manual has to say all three:

1. **A link or a tab that names the old key is simply not found.** That is the owner's decision and the
   job's own observation — the move can tell the *page* where it went but not the URL.
2. **A `?v=` link to the old key stops answering.** The revision it named still exists and still has
   that hash, but a versioned read needs the key and the hash, so it answers at `/b?v=…` and not at
   `/a?v=…`.
3. **A seed the release still ships at the old key will be installed there again.** With no head row,
   the seed page sees a *new* seed and offers it ticked; a tick writes it. A release that moves a
   seeded page must move the seed in the same release, or the next apply
   puts the old key back. This is the one consequence of leaving nothing that a marker would have
   prevented, and it is recorded rather than argued.

### 4. The validity rules, and which of them are values

The capability's four checks stay first, unchanged and in their order. Then:

| Rule | Answer | Why |
| --- | --- | --- |
| `from` or `to` cannot name a page (`?`, `#`) | `ArgumentException` | the call is wrong, as it is for every named member |
| `from` or `to` is a shell file | `InvalidOperationException` | no page is stored there; the reserved list is the host's |
| `from` is this render's own key | `InvalidOperationException` | a page that moved itself would spend the rest of its render answering for a key that no longer holds it — and the response in hand is the old page anyway |
| the live page at `from` is a library | `InvalidOperationException` | its key is in every dependent's `TemplateLibraries`, and a move would leave those dependents naming a key that is now empty; the refusal names that, and the route is a batch that recompiles them (040, 047) |
| `from` and `to` are one key | `Unchanged` | a key is lowercase by construction, so `/Blog` → `/blog` is the same key: the case-only rename the job asked about cannot happen, and saying so is better than an exception |
| the source's head is not `expectedHash` | `Stale` | the same conditional save the editor already relies on |
| no live page at `from` | `SourceNotFound` | a tombstone reads as absent, as it does for `ReadAsync` |
| a live page at `to` | `TargetExists` | the target has its own name: `Stale` is about the source's head, and reusing the word for a different question is how a caller gets the sentence wrong |
| keys below `from` | `FolderNotEmpty` | a folder is a batch of moves (`not in scope` below), and a move that left its children behind would split a subtree silently |

The division is the one the capability already uses: **values are the answers a caller can meet by
asking at the wrong moment or by naming the wrong key**, and exceptions are the calls the store never
allows. A library is the second kind — no argument the caller can supply makes it legal in this
release.

A move onto a **tombstoned** key is allowed, and it reads as a create that continues that key's history,
which is what a save there already does (`pages-delete-install-and-restore`). A move onto a **live** key
is `TargetExists` and writes nothing.

### 5. A move is not a rewrite

Links inside other pages still point at the old key, and now they are broken rather than redirected:
`/a` is not a page, so it answers `404`. That is the honest half the owner chose. Rewriting the links
is a search plus a batch save ([`cannot-search-the-store`](../backlog/cannot-search-the-store.md), [`one-write-per-render`](../backlog/one-write-per-render.md)) and
it edits pages the caller may never have opened — a decision of its own, deliberately not bundled here.

A library's dependents are the one case where the store *knows* it has invalidated something it cannot
rewrite, which is why that move is refused rather than performed. A worker is not in anyone's closure
and is started by key at run time, so a worker moves like any page and a page that starts it is the
caller's problem, exactly as a link is.

### 6. Nothing recompiles

The re-filed head carries `TemplateHash`, `RuntimeId`, `TemplateKind`, `TemplateLibraries`, the image,
the symbols and the diagnostics unchanged, so `PageImageStatus` still says *Compiled* and the next
request loads the bytes that already exist. No library closure changes, because a page is not a
library; no revision's `TemplateHash` changes, because no source did.

### 7. The seeded editor gets the action

The capability needs a shipped caller, and the editor is where an author meets it: a **Move** action
beside Delete, a confirmation like delete, install and restore (`?edit=<key>&cmd=move`), a field for
the new key, and the head hash the form was drawn from. The page renders the outcome — *moved* with a
link to the new key, *stale* with the read it must redo, *target exists*, *folder not empty* naming its
children — and the confirmation says the one thing an author must know: the old key stops answering.
The action is hidden on the editor's own row, as Delete is, because the capability refuses a page
moving itself.

The assistant's route to the same thing is a page: it writes pages, and a page may move one. Its tools
do not gain a `move_page` in this branch (see open question 3); its prompt gains the sentence that
stops it doing a save-and-delete instead.

## The contract

```csharp
/// <summary>Moves a page, its history and its state to another key.</summary>
/// <param name="from">The key the page has; normalised and checked.</param>
/// <param name="to">The key it should have; normalised and checked.</param>
/// <param name="expectedHash">The head hash the caller read, so a move never applies to a page that changed underneath it.</param>
/// <returns>
/// Moved, or the reason it did not move. A move onto a tombstoned key clears the tombstone and keeps
/// the history that key retained; a move onto a live page is <see cref="PageMoveOutcome.TargetExists"/>
/// and writes nothing.
/// </returns>
/// <remarks>
/// <para>
/// Every revision moves and keeps the hash it was saved with, so the page's history is one history
/// under its new name. Nothing is left at the old key: it answers as a key that never had a page, and
/// a versioned link that named a revision answers at the new key or not at all. Nothing is compiled —
/// the page serves the image it already had.
/// </para>
/// <para>
/// The rules: a reserved key at either end, a page moving itself, and a library are page bugs and
/// throw; the same key, a stale hash, a missing source, an occupied target and a key with pages below
/// it are answers. A move spends the render's one action, like every other write.
/// </para>
/// </remarks>
/// <exception cref="ArgumentException">A key cannot name a page.</exception>
/// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing was moved.</exception>
/// <exception cref="InvalidOperationException">A reserved key, a library, this page's own key, a <c>HEAD</c>, an ended render or a second write.</exception>
Task<PageMoveResult> MoveAsync(string from, string to, string expectedHash);
```

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageWrites.cs` | `MoveAsync`, and the remarks gain the move's three sentences |
| `src/uk.osric.asp.Sdk/PageMoveResult.cs` | New: `PageMoveOutcome` and `PageMoveResult` |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | Three lines: the member, the enum and its values, the record |
| `src/uk.osric.asp.Web/Pages/IPageStore.cs`, `SqlitePageStore.cs` | `MovePageAsync`, the transaction of §2, and the store's own result value |
| `src/uk.osric.asp.Web/Pages/PageEditor.cs` | `MoveAsync`, a straight call: no compile, no saver |
| `src/uk.osric.asp.Web/Pages/PageWritesScope.cs` | `MoveAsync`: the checks and the mapping of the store's answer to `PageMoveResult`, then `Acted()` |
| `src/uk.osric.asp.Web/Pages/Seeds/pages` | The Move action, its confirmation, and the own-row hiding |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The write section, and one sentence in the editor section |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The move paragraph, and `Version` → `page-author-v36` |
| `docs/pages.md`, `docs/storage.md` | The capability; the move's transaction and what it leaves behind |
| `docs/jobs/`page-cannot-move`.md`, `docs/plans/ide-host.md` | The plan pointer and the intended name |
| `docs/decisions.md` | At the merge only (below) |
| `test/…/FakePageStore.cs` | `MovePageAsync`, so the scope's checks stay testable without a database |

No migration, no entity, no change to `PageEntry`, `PageReadHead`, `SqlitePageReadStore` or
`PageCatalogScope`: nothing about a moved page is left for a read to report.

## Tests

Test-first, in the existing suites; one behaviour per test, named for the behaviour.

**The store** (`SqlitePageStoreTests`, against the real migrated database — this is where the
transaction is proved):

| Behaviour | Test |
| --- | --- |
| The page is live at the new key and the old key has nothing | `AMoveLeavesThePageAtTheNewKeyAndNothingAtTheOldAsync` |
| Every revision moves, the head included, and keeps its hash | `AMoveKeepsEveryHashAsync` |
| The old key reads as absent: no live page, no head, no history | `TheOldKeyReadsAsAKeyThatNeverExistedAsync` |
| The anchor and the content hash follow the page | `TheAnchorFollowsThePageAsync` |
| The image is reused: the new head lists as Compiled and its bytes are the same | `AMoveReusesTheStoredImageAsync` |
| State rows follow the key | `ThePagesStateFollowsTheKeyAsync` |
| A move onto a tombstoned key clears it and keeps the history that key retained | `AMoveOntoATombstoneKeepsTheHistoryThatKeyRetainedAsync` |
| A move onto a live key writes nothing | `AMoveOntoALivePageWritesNothingAsync` |
| A stale expectation writes nothing | `AStaleMoveWritesNothingAsync` |
| Moving the same page twice leaves the middle key empty | `AMoveOfAMovedPageWorksAgainAsync` |
| A library is refused | `ALibraryCannotBeMovedAsync` |
| A key with pages below it is refused and names them | `AKeyWithPagesBelowItCannotBeMovedAsync` |
| A failure mid-transaction leaves the store exactly as it was | `AFailedMoveWritesNothingAsync` |
| The head and its revisions still obey the composite key | existing `HeadCannotReferenceARevisionAtAnotherPathAsync`, unchanged |
| A release's seed is a new one again at the old key | `ASeedWhoseKeyMovedIsANewSeedThereAgainAsync` — this pins §3's third cost rather than forbidding it |

**The capability** (`PageWritesScopeTests`, over `FakePageStore`):

| Behaviour | Test |
| --- | --- |
| An anonymous render moves nothing and stores nothing | `AnAnonymousMoveIsRefusedBeforeAnythingIsMovedAsync` |
| A `HEAD`, an abandoned render, a past deadline and a second write are refused | `AHeadCannotMoveAsync`, `AnAbandonedRenderCannotMoveAsync`, `ADeadlineThatPassedCannotMoveAsync`, `AMoveSpendsTheOneActionAsync` |
| A reserved key at either end is refused | `AShellFileCannotBeMovedAsync` |
| A page cannot move itself | `APageCannotMoveItselfAsync` |
| The same key answers Unchanged, and a case-only spelling is the same key | `AMoveToTheSameKeyChangesNothingAsync` |
| A missing source and an occupied target are answers | `AMoveOfAMissingPageIsNotFoundAsync`, `AMoveOntoALivePageAnswersTargetExistsAsync` |
| A key with pages below it is answered with them, not thrown | `AMoveOfAKeyWithPagesBelowItNamesThemAsync` |
| Every outcome carries the head's hash exactly when it moved | `OnlyAMoveAnswersWithAHashAsync` |

**The seeded editor** (`PageEditorPageTests`): `TheEditorMovesAPageAndLinksToItAsync`,
`AMoveOntoALivePageIsAnsweredAndWritesNothingAsync`, `TheEditorOffersNoMoveForItselfAsync`,
`TheConfirmationSaysTheOldKeyStopsAnsweringAsync`.

**The prompt and the manual** (`PageAuthorPromptTests`, `HelpReferenceLinksTests`): the version
assertion moves to `page-author-v36`, the prompt contains `MoveAsync`, and every `/help` link still
names a documented member — the new anchor is `#m-IPageWrites.MoveAsync`.

**Over the real host** (`Web.IntegrationTests`, a new `PageMoveTests` beside `PageWritesTests`): a
stored page POSTs a move, the response is the outcome, the next request serves the new key, and the
old key answers `404` with no page. Nothing in a browser test: every behaviour here is provable by a
unit or integration test.

## Documentation

- **`/help`** is the author manual and is a page (`product-manual-is-a-page`), and it changes in the
  same commit: the write section gains the move — what it does, the six outcomes, that the history
  moves with its hashes, that **nothing is left at the old key** so links to it stop answering and a
  versioned link only answers at the new key, that a seed the release still ships there will be
  installed there again, that nothing recompiles, the two refusals and why, and that one render moves
  once. The editor section gains the Move action beside Delete and the sentence its confirmation
  carries.
- **`PageAuthorPrompt`** gains the same paragraph and the version bump, because it is the assistant's
  only manual: *rename by moving; never by writing the source at the new key and deleting the old*.
- **[pages](../pages.md)** adds `MoveAsync` to the `IPageWrites` bullet; the capability count does not
  change and the `IPageCatalog` bullet is untouched.
- **[storage](../storage.md)** records the transaction: the four statements, why the head row goes
  first, the re-filed history, the anchor that moves with it, the state, and that nothing is left at
  the old key.
- **[decisions](../decisions.md)** at the merge, below.

## The record at the merge

Two changes.

1. **Add pages-a-key-moves-with-its-history** — a key is a page's identity and `MoveAsync` is the one
   operation that changes it: one transaction, the history re-filed with its hashes, the state
   re-keyed, **nothing left at the old key**, no compile, one action, and the validity rules of §4.
   The owner's answer of 2026-11-16 that `/a` should answer `404` is part of the same entry, with its
   one consequence: a seed the release still ships at the old key is a new seed there again.
2. **Edit `pages-any-page-may-write-the-store` in place** — its list of the handle's members gains
   `MoveAsync`, because the authority it states is the same authority on the same handle.

`pages-a-page-may-save-itself` is checked and left alone: its one-write rule already covers the new
member. `pages-delete-install-and-restore` and `pages-a-seed-write-records-its-hash` are checked: a
move is neither a delete nor a seed write, and it copies the anchor rather than writing one.

The new name is cited in backticks nowhere until the merge, which is why every reference to it in this
branch is bold: `make decisions` fails on a cited name the record does not have.

## Slices

One branch, one gate, five commits, each readable on its own.

1. **The store.** `IPageStore.MovePageAsync`, the transaction, and the store's tests.
2. **The capability.** `PageMoveOutcome`, `PageMoveResult`, `IPageWrites.MoveAsync`, the public API,
   `PageEditor`, `PageWritesScope`, `FakePageStore`, and the scope's tests.
3. **The editor.** The Move action, its confirmation, and the editor and integration tests.
4. **The manual and the prompt.** `/help`'s two sections, `PageAuthorPrompt` and `page-author-v36`, and
   their tests.
5. **The docs.** [pages](../pages.md), [storage](../storage.md), [`page-cannot-move`](../backlog/page-cannot-move.md), and
   the slice-6 pointer in [the IDE plan](ide-host.md).

`make check` is the gate: format, licence, audit, decisions, `check-pages` (the seeds compile, so the
editor's new action is compile-proved), the tests and the publish. The merge is the owner's, and the
record is written there.

## Risks

- **A seeded page's old key comes back.** With nothing at the old key, the seed page reads the
  release's seed there as *new* and offers it ticked; a tick writes it. A
  release that moves a seeded page — the editor key included — must move the seed in the same release.
  The manual says it; the test `ASeedWhoseKeyMovedIsANewSeedThereAgainAsync` pins the behaviour so it
  cannot drift into an accident.
- **Every inbound link is broken, not redirected.** The job's workaround broke them silently; this
  breaks them loudly, because `/a` answers `404`. That is the owner's decision and the reason a link
  audit after a move is [`cannot-search-the-store`](../backlog/cannot-search-the-store.md)'s work.
- **A `?v=` link answers at the new key only.** The revision and its hash are unchanged; the address is
  not.
- **A library move stays refused until 040 and 047 exist.** The refusal is honest rather than helpful:
  with no dependents read, it cannot name the pages a library move would break.
- **The one action means one move per render.** A folder is several moves, so it is several requests
  until the batch ([`one-write-per-render`](../backlog/one-write-per-render.md)). The confirmation says so.
- **The transaction is the store's first that changes the path of stored revisions.** It is four
  statements in an order the foreign key demands, and `AFailedMoveWritesNothingAsync` is the test that
  keeps it whole; but it is the operation to re-read when the schema is next changed.
- **The store cannot say a page moved.** Nothing records it, so no view can offer "moved to …" and a
  trail cannot be reconstructed from the database. That is the accepted cost of §3.

## Open questions

The first is answered; the rest are the details the plan has already chosen, stated so the owner can
overrule them cheaply.

1. **~~A marker at the old key, or nothing?~~ Answered 2026-11-16: nothing.** `/a` answers `404` after
   `/a` moves to `/b`, so no row, no column and no migration. §3 records what it costs.
2. **`expectedHash` required?** Recommend required. A move of a head the caller did not read is a
   rename racing an edit, and the caller that can name `from` can name the hash.
3. **Does the assistant get a `move_page` tool here?** Recommend not in this branch: the job's location
   line stops at the prompt, the seeded editor is the shipped caller, and a page is the assistant's
   route to everything the host adds (`pages-page-first`). A tool is a small follow-on — one
   `AssistantTool`, a registry row, the changed-pages card learning `from` and `to`.
4. **A page with children is refused?** Recommend refused with the children named: a folder is a batch
   of moves, and a move that left a subtree behind would split it silently. The owner's note that `/a`
   should answer `404` settles the old key's fate rather than this one — a move that left `/a/child`
   behind would still leave `/a` with no page, and with an orphaned subtree under it. Moving the
   children first and the folder's own page last renames a folder by hand, which is the order the rule
   teaches.
5. **The editor's own Move action, and its own row.** Recommend the action, hidden on the editor's own
   row for the reason Delete is: the capability refuses the call, and a form that offers a refusal is a
   form that teaches the wrong thing.

## Not in scope

- **Moving a folder.** A batch of moves, built from this member rather than as a second operation
  ([`one-write-per-render`](../backlog/one-write-per-render.md)).
- **Rewriting inbound links.** A search plus a batch save, and a decision about editing pages the
  caller never opened ([`cannot-search-the-store`](../backlog/cannot-search-the-store.md)).
- **Moving a library.** Refused until the dependents are readable ([`library-edit-cost-is-invisible`](../backlog/closed.md)) and
  a batch can recompile them ([`one-write-per-render`](../backlog/one-write-per-render.md)).
- **An alias or a redirect.** A key that `301`s is a response rule and a store column, and the owner
  declined even the store's memory of the move; a redirect is a different job with a different security
  story.
- **Moving a tombstone.** A tombstone is history, not a page; it is scrubbed or saved over, not moved.
- **A rename in the release's own seed pack.** [IDEA-036](archive/seed-provenance.md)'s harvest and
  adopt round trip — and the reason §3's third cost has a way out.
- **The assistant's `move_page` tool, and the IDE's tree and tabs.** See open question 3; a page that
  moved one knows the new key, and that is what its own view shows.
