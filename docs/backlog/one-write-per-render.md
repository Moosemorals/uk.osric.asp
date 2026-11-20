<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A render cannot write several pages as one transaction

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [ide-host](../plans/ide-host.md) |
| Area | Sdk, Web (pages, editor, work), docs, assistant |
| Location | `src/uk.osric.asp.Sdk/IPageWrites.cs`, `src/uk.osric.asp.Sdk/IPageTransaction.cs`, `src/uk.osric.asp.Web/Pages/PageWritesScope.cs`, `src/uk.osric.asp.Web/Pages/IPageBatchStore.cs`, `src/uk.osric.asp.Web/Storage/SqliteErrors.cs`, `src/uk.osric.asp.Web/Program.cs`, `src/uk.osric.asp.Web/Pages/PageStorageOptions.cs`, `src/uk.osric.asp.Web/appsettings.json`, `src/uk.osric.asp.Web/Editor/PageSaver.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

> **The commit's report is one row type — 2026-11-20.** The owner settled the last shape question: one
> row type carrying the key, the operation, the outcome, the hash and the diagnostics, with
> `PageEditResult`, `PageMoveResult` and `PageDeleteResult` becoming views of it rather than three
> parallel definitions. A lone write keeps the answer it has today.

> **A transaction may span a library and its dependents — 2026-11-20.** The owner's answer is yes, and
> the page's own order decides what that means. A staged save compiles against the store's libraries
> plus the ones already staged before it in the same transaction, so a page that writes
> `/lib/format` and then `/blog/post` commits a post built from the new library, and one that writes
> them the other way round commits a post built from the previous one — which its own fingerprint
> makes stale, and it recompiles lazily like any dependent outside the transaction. **The host does
> not reorder the calls or compile a second time to make the order irrelevant**: an order that matters
> is stated in `/help` rather than fixed behind the page's back. A page that wants both without
> depending on order commits the library, then the pages in a second transaction.

> **The assistant's safety rules are unchanged — 2026-11-20.** The owner's answer to the last open
> question: nothing about transactions changes them. The assistant may transact where it may already
> write, each write inside is judged by the same per-key rule, and a multi-page call is still one
> approval because approval was already once per call. A transaction is not a new authority, and it
> relaxes nothing.

> **No silent fixes — 2026-11-20.** The owner's rule for this capability: where the page's request
> could be quietly improved, either the page makes the choice and `/help` states the consequence, or
> the bad case throws or answers a refusal. Nothing is reordered, retried or repaired on the page's
> behalf without saying so.

> **Isolation and contention settled — 2026-11-20.** The owner asked for serializable and for the store
> to run WAL. Measured on the client and the store as they are: SQLite never shows an uncommitted row,
> the client's transaction is `Serializable` and issues `BEGIN IMMEDIATE`, and a second writer waits on
> the connection's 30-second busy timeout before failing. The answers below are what the item records:
> **the commit is the store's own serializable transaction, taken with the writer lock from its first
> statement; a commit that cannot take the writer writes nothing and answers a contention row, and
> whatever the page does about it is the page's decision; and WAL is in scope.**

> **Three policies became one transaction — 2026-11-20.** The first sketch gave `SaveManyAsync` a
> policy the caller named: all or nothing, stop at the first refusal, or save what it can and report
> the rest. The owner rejected it as anticipating variations on a batch save that nobody has asked
> for. A page gets a transaction instead. The owner's answers, same day: **a transaction sits beside
> the write members rather than replacing them, so a lone write stays one call; every write member is
> on both; a render may commit as many transactions as it has work for;** and the merge records
> **pages-writes-are-transactions**.

> **A transaction must be able to move, not only save — 2026-11-19.** The owner settled
> [`page-cannot-move`](closed.md) with a single `MoveAsync`, and a folder rename is several moves. An
> operation is therefore not only an edit: the same transaction and the same commit cover saves,
> moves, tombstones and a restored or installed source. It is the only way to rename a folder, because
> `page-cannot-move` refuses a key with pages below it.

> **Re-prioritised 2026-11-18** by the owner. The rule is the friction the assistant meets on every
> multi-file edit, and it is the gate in front of a job writer
> ([`job-borrows-a-credential`](job-borrows-a-credential.md)), so it moves from the bottom of the order
> to just after `check-without-storing`, whose compile-without-store the staging save borrows.

## Idea

`pages-a-page-may-save-itself` says "one render makes at most one save", which makes Save all a
sequence of requests that cannot be atomic: a refusal on the third page leaves the first two saved with
no way to know that from the fourth. It is also what makes a multi-file edit by the assistant N
requests with N chances to leave the site half-changed. The owner decided on 2026-11-15 that the rule
goes; what replaces it was settled on 2026-11-20.

The site already knows the answer. An import compiles every page with nothing written, then commits
revisions, heads and state in one SQLite transaction — all of it or none of it ([storage](../storage.md)).
What an import does once, for an archive, is what a page should be able to ask for itself. So the
replacement is the store's own vocabulary rather than a new one: a transaction.

The guarantee the owner asked for is the one the store already keeps. SQLite shows no dirty reads — a
reader sees the last commit and nothing of a transaction in flight — and its transactions are
serializable, so two renders that write cannot interleave. What the capability has to add is not an
isolation level but a place for several writes to happen under that guarantee, and a story for what a
page is told when the guarantee means it has to wait.

This job was split out of [IDEA-043](closed.md) on the same day as the rule was retired. 043 widens
the authority and merges the two write handles; this is the half that gives the writes somewhere to
happen.

The wider class is "a page that maintains the site": the editor's Save all, the assistant's multi-file
edit, an import, a bulk restore, a seed harvest and a rename of a folder are one shape — several
writes, one commit, one report. Building it for Save all alone would be building it six times.

## Sketch

1. **A transaction sits beside the writes.** `IPageWrites` keeps everything it has — the reads
   `Resolve` and `SeedSourceAsync`, beside the seven write members — and gains `BeginTransaction()`,
   which answers an `IPageTransaction`. A single write stays one call and is a transaction of one
   under the seam: it stages, commits and answers before it returns, exactly as it does today. A
   transaction carries the same seven members and stages them, so a page that needs several writes in
   one unit calls `BeginTransaction`, makes the calls it already knows, and commits once; the members
   keep their names and their arguments, so a page that can do one can do several.

   A transaction that is disposed without a commit — or never disposed at all — writes nothing. A
   render holds one open at a time and commits as many as it has work for, which is what ends
   `PageWriteAction` and its one-action counter. The guard that counter gave does not come back with
   it: a listing that embeds a self-saving widget can still write more than once, because the
   widget's line is in the widget. That is the owner's decision, and the guards that remain are the
   signed-in visitor and the reserved keys. Nesting is refused: one open at a time, because the store
   has one transaction and a second model is not wanted (`code-n-is-small`).

2. **Staging compiles; the commit decides.** Each save compiles when it is called, so the page has its
   diagnostics — and its answer about a page that will not serve — at the moment of the edit, and no
   database lock is held while a compile waits for a slot. A lone write then checks its conditions and
   is applied before it returns, so its answer is the one it gives today. A transaction's operations
   are checked at the commit: every expectation is re-proved against the head it names, then every
   revision, head, move and tombstone is applied in one transaction. It is the import's shape with
   the conditional checks a page edit adds. The render's deadline still bounds the wait for a compile
   slot and nothing else: a write or a commit that has begun is not abandoned, and a transaction that
   is never committed has nothing to abandon.

   A staged save reads the libraries staged before it as well as the store's, and nothing else: the
   page's call order is what decides which library a dependent is compiled against. Staging a library
   after a page that uses it is not an error and is not corrected — the page was built from the
   library that was there, its own closure fingerprint says so, and it is stale and recompiles lazily
   exactly as a dependent outside the transaction is. `/help` states the rule, and a page that wants
   the new library without relying on order commits the library, then the dependents in a second
   transaction.

3. **The commit is the store's own serializable transaction, and it takes the writer first.** The
   client's transaction is `Serializable` and issues `BEGIN IMMEDIATE`, so the commit holds the
   store's one writer from its first statement: the heads it re-reads to prove the expectations cannot
   move under it, and a stale expectation is therefore always a clean answer rather than a lost
   update. Nothing else weakens it — `ReadUncommitted` is accepted by the client and does nothing
   outside shared cache, which the site does not use — so the capability exposes no isolation choice
   and a page cannot ask for less.

   Two consequences are rules rather than notes. **Nothing slow runs inside a commit**, because the
   writer lock is site-wide: the staging compile is outside it not by preference but because a compile
   inside it would make every other writer — a save, a seed install, a job, the assistant — wait out
   the busy timeout and then fail. And **the commit is statements only**: reads of the heads it
   proves, the inserts, the moves, the tombstones, the commit.

4. **Contention is a row, and what to do about it is the page's.** A commit that cannot take the
   writer wrote nothing, and the commit answers a row whose outcome is contention rather than
   improvising a retry the page did not ask for. What the page does next is its choice — retry the
   same transaction, split it, or answer "the site is busy" — and `/help` shows the pattern rather
   than the host performing it invisibly. The wait before that row is the store's busy timeout, which
   becomes an explicit configured value with a shipped default rather than the client's thirty-second
   default: a limit is a policy value (`code-policy-values-are-configuration`), and the time a page
   waits to be told "busy" is the owner's to choose.

5. **The store runs WAL.** In rollback-journal mode a held read transaction blocks a commit for the
   same busy timeout — measured at 30 s, where WAL commits in 0 ms — so a reader would be able to fail
   a page's write. The mode is switched on and **verified** at startup, and startup refuses rather
   than continuing in the other mode, because a filesystem that cannot do WAL would otherwise leave
   the guarantee silently weaker. WAL is persistent in the file, so this happens once per database;
   the scrub's checkpoint path already handles both modes; and the `-wal` and `-shm` files join the
   backup rules the [storage](../storage.md) doc already states, where a backup is a transaction
   rather than a file copy and the WAL can hold committed data. A single host with a local volume is
   what makes it safe: WAL is not for a network filesystem.

6. **A transaction is all of it or none of it.** A stale expectation, a target that has appeared, a
   source that has gone — any refusal writes nothing, and the report names every operation that
   refused — with the head it saw, when the head moved — so a Save all can show all of its conflicts
   in one answer. A page that wants to commit the rest disposes that transaction and opens another
   with the operations it still wants; that is page code, not a policy the host enumerates. Refusals
   that are page bugs — a reserved key, a key that is not addressable, a library moved — throw at the
   call, as they do today; a second operation on a key the transaction already holds is not a bug, so
   it is refused as AlreadyStaged instead.

7. **The report is one row per operation, and the row is one type.** The commit answers what each
   operation became — the key, which member ran, the outcome, the revision hash and the diagnostics
   the compile gave, and for a refusal the reason and the head the transaction saw — as one row type.
   `PageEditResult`, `PageMoveResult` and `PageDeleteResult` stay the single-write answers and become
   views of that row rather than parallel definitions, so a save, a move and a delete cannot describe
   themselves one way alone and another way in a batch, and a page iterating a mixed report needs no
   test per entry. A staged call answers only what the compile knows — the hash and the diagnostics —
   because the outcome is not decided until the commit. The key is the row, so a second operation on
   one key cannot be staged: it is refused as AlreadyStaged when it is made — stupid rather than a
   bug, so an answer and not an exception — because one of the two expectations cannot hold. A page
   that does not
   compile still commits — a save stores its source with its diagnostics, and a compiler fault still
   stores the source without an image — exactly as a single save does today
   (`pages-a-warning-never-stops-a-page`, `pages-a-compiler-fault-is-ours`).

8. **A lone write is a transaction of one, under the seam.** The editor's save, the assistant's save
   tool, a restore, a seed install and the bound self-save keep their one call. Each stages, checks
   and commits through the same path a transaction uses, so the conditional save, the stale answer and
   the diagnostics cannot drift between a single write and a batch, and the editor's Save all is the
   transaction's first caller rather than a second implementation. It is that write's *own*
   transaction, never folded into one the render has open: "save this" means stored when the call
   answers, and only `BeginTransaction` defers.

9. **The documentation.** `/help` gains the capability's section — the transaction, the commit and its
   report, the difference between staging and committing, that a library must be staged before the
   pages compiled against it, and what a contention row means and that retrying is the page's choice
   (`product-manual-is-a-page`);
   `PageAuthorPrompt` gains the transaction and its `Version` is bumped, because the assistant is the
   second caller and should make a multi-file edit one request rather than a sequence.
   **The transaction changes none of the assistant's safety rules**, and the prompt says so: it may
   transact where it may already make those writes, each write inside is judged by the same per-key
   rule — including the no-op refusal — and an approval still shows every page the call touches,
   because approval was already once per call rather than once per write
   (`assistant-edit-page-is-safe`, `assistant-a-run-waits-on-one-owner-interrupt`);
   [pages](../pages.md) records the contract and [storage](../storage.md) records the commit, the
   writer lock and WAL; and the merge writes **pages-writes-are-transactions** and edits
   `pages-a-page-may-save-itself`, which loses "one render makes at most one save". The isolation rules
   and the WAL switch are recorded in that one entry rather than a second name: they are the same rule
   about how a page's writes reach the store, and two entries would be two paragraphs about one
   decision.

## Not in scope

- **The widened authority and the merged capability.** [IDEA-043](closed.md) landed first.
- **A member per shape of batch.** `SaveMany`, `DeleteMany`, `MoveFolder`, `InstallAll`. A transaction
  and a loop is the vocabulary, and a member for each batch is what this item exists to avoid. An
  import keeps its archive reader above the seam.
- **A search and a check.** [`cannot-search-the-store`](cannot-search-the-store.md) and
  `check-without-storing` are their own items; a check inside a transaction would compile and store
  nothing, so it stays where it is.
- **A second transaction model.** One database, one transaction; no outbox, no two-phase commit, no
  queue, no nested transactions. `n` is small (`code-n-is-small`). That includes a journal or WAL of
  the site's own above SQLite's: a commit of fifty revisions takes about 6 ms and one of ten about
  0.7 ms, so there is no contention to engineer around, the compile is the only slow step and it is
  already outside the lock, and a write-behind queue would owe recovery, a revision hash before the
  hash exists, and a read path that consults the queue. WAL at the store's own level gives the part
  that is worth having — a reader never waits on a writer.
- **An isolation level for the caller to choose.** The store's guarantee is the only one on offer; a
  page cannot ask for less, and there is no shared cache or second connection mode behind a setting.
- **Undo.** A committed transaction is a set of revisions, and the history is where a mistake is
  answered.

**Origin:** split out of [IDEA-043](closed.md) by the owner on 2026-11-15 ("yes to splitting out batch
save to a separate problem, that's a bit further down the backlog"), with the three policies the owner
sketched the same day. The owner replaced those policies with a transaction on 2026-11-20, and the IDE
plan's Save all ([the programme](../plans/ide-host.md)) is the caller that found the rule.
