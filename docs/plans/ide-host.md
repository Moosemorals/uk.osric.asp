<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Host support for the IDE

| | |
| --- | --- |
| Status | Proposed 2026-11-15 — waits for the owner. Slices 0, 1 and 2 are built and merged; the other four are unbuilt, and nothing below is the IDE |
| Size | Large as a programme, small as a step: seven capabilities, each its own job and its own branch, ordered so that each ships on its own and can be backed out on its own, and a closing sweep of the pages that could use what they leave behind |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-15: `IPageCatalog`, `IPageEditor`, `PageEditorScope`, `PageWriterScope`, `PageCompiler.MapDiagnostics`, `PageDiagnostics.ToSdk` and `PageJobRunner` |
| Decisions | `pages-a-diagnostic-names-its-source` (041), recorded at its merge. The rest are named at the merge (`product-plan-before-building`); this branch states the names it intends. One entry is retired rather than revised — the one that made the editor's registration the authority — and `pages-a-page-may-save-itself` is revised twice, once by 043 and once by 047 |
| Backlog | [`cannot-search-the-store`](../backlog/cannot-search-the-store.md), [`listing-has-no-cursor`](../backlog/closed.md), [`library-edit-cost-is-invisible`](../backlog/closed.md), [IDEA-041](../backlog/closed.md), [`page-cannot-move`](../backlog/closed.md), [IDEA-043](../backlog/closed.md), [`one-write-per-render`](../backlog/one-write-per-render.md) |

## The short version

**The assistant builds the IDE. The host builds what the IDE needs.** The design is `/ide-plan` — a
stored page that is not in this repository, served at
[moosemorals.com/ide-plan](https://moosemorals.com/ide-plan) — and its
slices T0–T10 are page work: a document, a grid, tabs, a menu bar, keys, a phone layout, a tree, a
statusline. The host's part is the seven things the page contract cannot do today, and nothing more —
no editor route, no shell-rendered tree, no `Ide` capability.

That split is not a courtesy, it is the rule: anything a page can serve is a page (`pages-page-first`),
and the editor is already a page (`pages-the-editor-is-a-page`). An IDE built by the host would be the
largest view the shell had ever held, and the one view the assistant could not repair. What the
assistant cannot do for itself is widen the contract, and that is this plan.

The comparison that produced the backlog is worth stating once: of the IDE plan's twelve wishes, two
were already jobs ([`no-account-store`](../backlog/no-account-store.md) for per-account preferences and favourites,
[`editor-is-a-textarea`](../backlog/editor-is-a-textarea.md) for highlighting), one was not the host's business, and seven had
nothing behind them. One of those seven split into two jobs, and the check it produced now has a
plan of its own, so the list above is seven.

## What the host does, and what it does not

| The host builds | The assistant builds |
| --- | --- |
| A search that returns matches (038) | The shell, the grid, the menu bar |
| A listing that resumes (039) | Tabs, and the URL grammar that holds them |
| The closure a page names (040) | Dirty state, Save all, the conflict panel |
| A diagnostic with a span and a source (041) | The tree, the filter, the splitter, the marks |
| A key that moves with its history (042) | Keys, quick open, the phone layout |
| A write any page may make (043), and a transaction a page commits (047) | `/ide.js`, `/ide.css`, `/help`'s editor sections |

The IDE plan's own claim about its prerequisites holds and was checked: `layout.Layout = null` needs no
capability ([IPageLayout](../../src/uk.osric.asp.Sdk/IPageLayout.cs)), the same-origin `fetch` its Save all
leans on is already used by the site's own scripts (`/assistant.js` is a seed), `ctx.AssetUrl`, `ctx.AllowScripts`,
`ctx.AntiforgeryField` and `ctx.FlushAsync` are all on the context, and `PageEntry.State` already gives
the tree the host's own compile words.

## Which slice needs which capability

The IDE plan's TODO, in its own order, against what the host must add. "Nothing" is the honest answer
for eight of the eleven, and those eight are why the page can start before any of this lands.

| IDE slice | What it needs | Where |
| --- | --- | --- |
| T0 — check the assumptions | Nothing. The fetch is shipped; `<details>`, the tab strip and synthetic `keydown` are page measurements | — |
| T1 — the page writes its own document | Nothing. `Layout` is a settable `string?` and streaming already requires clearing it | — |
| T2 — the shell, one pane | Nothing | — |
| T3 — tabs | Nothing. Tabs live in the URL and presentation in `sessionStorage` | — |
| T4 — dirty, Save all, the conflict panel | A transaction over several writes, and a commit report that names every conflict; its dry run waits on the check, which is no longer this plan's | 047 |
| T5 — the old stages become panels | Nothing. Every existing URL keeps working | — |
| T6 — keys | Nothing for the handlers; a span makes F8 underline rather than point | 041 (optional) |
| T7 — the phone | Nothing. Container queries, `dvh`, a drawer — all page work | — |
| T8 — tree polish | The closure, so a row can say what a library edit will recompile; a cursor, so a big site is listable | 040, 039 |
| T9 — the head poll | Nothing. `IPageCatalog` answers the hashes, and a `POST` may read | — |
| T10 — the docs | The page edits `/help` itself; a capability the host adds gives it a section to write | — |

## The two rules this plan revises

An entry in [decisions](../decisions.md) is a guideline against relitigating the same problem, not a
constraint that outlives its reason. Two of them have outlived theirs, and the owner decided on
2026-11-15 that both go. They are two steps rather than one: [IDEA-043](../backlog/closed.md) takes the
authority and has [a plan of its own](archive/page-writes.md), and [`one-write-per-render`](../backlog/one-write-per-render.md) takes the
transactions.

**The authority to write is the visitor's, not the page's.** The entry that makes the isolated
registration — one content key, `Pages:Editor:Key` — *is* the authority is retired and replaced by
**pages-any-page-may-write-the-store**. It was the right guard for a handle that names a key: handing it
to every page makes every stored page a writer of the store. The owner's answer is that nothing about
editing is special to one page, so the guard becomes what it was always about: a signed-in visitor,
checked in the capability before anything is compiled or stored. `Pages:Editor:Key` keeps exactly one
job, saying where the editor is served, which is what `ctx.EditorPath` has always used it for. The same
step merges the two write handles into `IPageWrites`, so a page that may write any page writes itself
through the same capability, and lets the editor's own key be deleted like any other — `/admin/seeds`
is the way back.

**Writes are transactions.** `pages-a-page-may-save-itself` says "one render makes at most one save",
which makes Save all a sequence of requests that cannot be atomic and a multi-file edit by the
assistant N requests. The replacement is the store's own vocabulary: `IPageWrites` keeps its write
members, each a transaction of one, and gains `BeginTransaction()` for the page that needs several
writes in one unit. A transaction stages the writes, re-proves every condition at the commit and
applies them in one SQLite transaction, or none, and it reports one row per operation. A render may
commit as many transactions as it has work for. The owner settled the shape on 2026-11-20, replacing
the three policies the first sketch named.

The names this plan intends, taken at the merge — bold rather than in backticks, because `make
decisions` fails on a cited name the record does not have yet. The one already taken is cited in
backticks beside the slice that took it:

- **pages-a-search-returns-matches** (038)
- `pages-a-diagnostic-names-its-source` (041) — recorded: the span and the key are one rule, a
  diagnostic points at the thing in the file it is in
- `pages-a-listing-resumes-from-a-key` (039) — recorded: a slice is a keyset, and the cap is the
  largest slice rather than a refusal
- `pages-a-page-names-its-libraries` (040) — recorded: a row carries its closure's keys
- **pages-a-key-moves-with-its-history** (042)
- **pages-any-page-may-write-the-store** (043) — the authority is the signed-in visitor and the two
  write handles are one capability
- **pages-writes-are-transactions** (047) — the write members move onto a transaction a page opens and commits, and the one-write rule goes

## The wider problem, not this one

Each capability is scoped to the class of caller, not to the IDE. This is the owner's rule — solve the
generic problem — applied one job at a time, and it is what decides several of the shapes below.

| Capability | The IDE asks for | What it actually answers |
| --- | --- | --- |
| A search that returns matches (038) | Find in files | "Who names this library", a link audit after a move, a seed-harvest check, the assistant's search before an edit |
| A listing that resumes (039) | A tree that survives 500 pages | Every bounded read: a history, a folder, a scan's matches |
| The closure (040) | A mark in the tree | What an edit invalidates, before and after it; the order a transaction applies its writes in; which dependents a library move breaks |
| A diagnostic with a span and a source (041) | Underline the token | VS Code's squiggle, `make check-pages`'s output, the assistant reading a failed save, and the library whose line 12 it is |
| A key that moves (042) | Rename a page | A release re-slugging a seed; a folder moved; the assistant fixing a key it got wrong |
| A transaction (047) | Save all | The assistant's multi-file edit; an import; a bulk restore; a folder move; a find-and-replace |

A capability shaped around one caller is the failure mode this table exists to prevent: the editor is
the first customer of all seven and the owner of none.

## Slices

Each is one job, one branch, one step through the gate, and one stop for the owner
(`product-steps-wait-for-the-owner`). The order is by cost and by dependency, not by IDE value: the
smallest changes come first because they are what every later slice is written against.

0. **041 — the diagnostic value.** Landed, and the record is `pages-a-diagnostic-names-its-source`.
   Smallest, and a breaking change to a public record, so it landed before three more consumers learned
   the point-shaped version. Proved: a token diagnostic has an extent, a library's diagnostic names the
   library, an unknown position is still `null`.
1. **039 — the cursor.** Landed, and the record is `pages-a-listing-resumes-from-a-key`. Small,
   independent, and the shape 038 and the tree both need. Proved: a cursor walks the store in slices
   with no key twice and none missed, a cursor past the last key is an empty answer, and the row cap
   became a limit rather than a refusal.
2. **040 — the closure.** Landed, and the record is `pages-a-page-names-its-libraries`. Small, and it
   is what makes a tree's marks true. Proved: a page that names a library reports it, the page that
   names none reports nothing, and the closure is the one the save stored rather than a second parse
   of the source.
3. **043 — the widened authority.** The step with its own plan ([page writes](archive/page-writes.md)), because
   it renames two public interfaces and breaks every stored page that names them. Proves: an anonymous
   render is refused; a layout never gets the handle; every content page does; two saves answer with
   their own values; a page writes another key; one render still makes one write; the editor's key is
   deletable and `/admin/seeds` is the way back.
4. **038 — the search.** Needs 039 for its cursor; it shares nothing with 043, but it is the last thing
   that reads every source, so it comes after the budget rules are settled.
   Proves: a scan finds a literal in a scope, is bounded, resumes from a cursor and never hands a
   source to the page.
5. **042 — the move.** Built on `idea/page-cannot-move` and waiting for the merge; the intended record
   is **pages-a-key-moves-with-its-history**. It turned out not to need 040 and 038: a library move is
   refused rather than recompiled, and the broken-link report is
   [`cannot-search-the-store`](../backlog/cannot-search-the-store.md)'s work. It is the only operation
   that writes more than one row, and nothing is left at the old key. Proved: a page moves with its
   history and state and keeps every hash, the old key answers `404`, and the editor, `move_page` and
   `mv` all move a page.
6. **047 — the transaction.** The largest, and the last capability: it changes the shape of every save,
   so it lands after the saves it moves are settled. Proves: a commit with a stale operation writes nothing and
   the report names every conflict; a committed transaction stores every revision, move and tombstone
   or none of them; a lone write is a transaction of one and answers exactly as it did; the commit
   takes the store's writer from its first statement, so a second writer cannot act on the heads it
   has read; a contending commit writes nothing and answers a row the page can act on; a save staged
   before the library it names compiles against the library it named and is stale, with nothing
   reordered behind the page's back; and the store runs WAL, with startup refusing when it cannot. *The owner re-prioritised it on 2026-11-18 to ahead of this plan: it is
   the assistant's daily friction and the gate in front of a job writer. If it lands first, this step
   shrinks to wiring Save all to the transaction, and T4 loses its last dependency.*

7. **The pages that could use it.** The step that spends the capability, and the one that can honestly
   find nothing: review every seeded page under `Pages/Seeds` and every store-writing route the shell
   owns — `/pages` and `/admin/seeds` are the two that write today — and ask of each whether it makes
   several writes that belong together, or makes them as N requests because it could only ever write
   once. The shapes already visible: `/pages` refuses to move or delete a folder because the store
   refuses a key with pages below it, and that is a transaction of moves or of tombstones; `/admin/seeds`
   installs one page per confirmation, where "install everything this release moved" is one commit;
   and the assistant's multi-file edit becomes one request rather than a sequence, which its prompt
   already says. A page keeps its one call when one write is all it makes — the transaction is
   all-or-nothing, so a page that wants partial success must not use one, and nothing here trades that
   away to save a line.

   Proves: every page that gained a transaction has a test that a refusal writes nothing; `/help`
   describes what those pages now do (`product-manual-is-a-page`); and a note names the owner's stored
   pages — the IDE, the plan page — that could adopt it, because those live in the site rather than in
   this repository and are edited there.

**037 — the check — has left this plan.** The assistant and VS Code want the same seam, so the
owner folded all three callers into one item, `check-without-storing`,
which gets its own plan. The IDE is still its first customer and no longer its owner: the editor's
underline is page work on top of a capability this plan does not carry.

## Tests

- **One suite per capability**, beside the thing it changes, in the existing projects: `Sdk` has none,
  so the values are proved through `Runtime` (the diagnostic mapping), `Web` (the capabilities and the
  store) and `Web.IntegrationTests` (a page that uses the capability end to end).
- **One assistant test per capability**: `PageAuthorPrompt` is the assistant's only manual, so a
  capability that lands without its prompt section is invisible to the caller that is meant to use it,
  and its `Version` bump is the signal that the prompt changed.
- **Nothing in a browser test** that a unit test can prove. Everything the IDE's own slices need —
  tabs, undo, the tab strip's responsiveness — is the assistant's work and its own tests.
- `make check` is the gate for every slice; `make decisions` is what proves the names, at the merge.

## Docs

- **`/help` in the same change**, for every capability and its words — the transaction, its commit
  report, the ordering rule a library and its dependents follow, what to do about a contention row,
  the cursor, the span's limits, the closure, the move's refusals (`product-manual-is-a-page`).
- **`PageAuthorPrompt` and its `Version`**, so the assistant knows a scan, a cursor, a transaction
  and a move exist. Without that the assistant keeps the workaround the IDE plan calls "save, read,
  accept the noise".
- **[pages](../pages.md)** for the contract, **[storage](../storage.md)** for the move's transaction,
  and **[decisions](../decisions.md)** at each merge.
- **The `/ide-plan` page** is the assistant's, not this plan's. It says itself that it becomes the
  record of the second version of the IDE when the first slice lands; this plan asks only that the
  wishlist entries a capability answers are struck off there when it does.

## Risks

- **Any page may now write the store.** That is the owner's decision, and the blast radius is real: a
  page the assistant writes can rewrite the site. The guards that remain are the signed-in visitor, the
  reserved keys, `/admin/seeds` for a broken editor and `/admin/scrub` for the irreversible delete.
  The assistant's own edit-safety rules are unchanged (`assistant-edit-page-is-safe`) and are what stop
  it from doing so by accident.
- **A commit holds the store's one writer.** It is `BEGIN IMMEDIATE`, so the lock is site-wide from
  the first statement and the wait before a `SQLITE_BUSY` is the store's busy timeout: a compile or
  any other wait inside a commit would stall every other writer and then fail it. Staging compiles
  outside the commit for that reason, and the commit stays statements only.
- **Contention is the page's to answer.** A commit that loses the writer wrote nothing and answers a
  contention row; the host does not retry on the page's behalf, because a silent retry is the host
  deciding something the page asked to decide. The cost is that pages which want a retry write a
  small loop of their own, and `/help` shows that loop rather than the host hiding it. The time a
  page waits to be told "busy" is a configured busy timeout, so the owner sets it rather than the
  client's default.
- **A scan reads every source in the store.** It is bounded by a setting of its own and it must be, or
  a search box is a way to make the host read a megabyte per keystroke.
- **The IDE lands before the capabilities do.** That is the intended order: T0–T3 and T5, T7 and T9
  need nothing, and the page can be built and used while the host catches up. A page that waits for
  the host would be the thing this plan exists to prevent.
- **A breaking change to a public value.** 041 moves `PageDiagnostic` in two assemblies, 043 renames two
  interfaces every stored page may name, and 039 and 047 add members to interfaces every stored page
  compiles against. Each is a change of the page contract's version and each is a recompile of the site;
  that is the ordinary cost of a contract change here, and it is recoverable in the editor and from
  `/admin/seeds`.

## Open questions

The first three were answered by the owner on 2026-11-15, and 043's plan records them: 043 is two jobs
(the authority here, the transactions in [`one-write-per-render`](../backlog/one-write-per-render.md)); the merged capability is
`IPageWrites`; and the editor's key is deletable, with `/admin/seeds` as the way back.

The fourth was answered on 2026-11-20: **the assistant's safety rules do not change.** It may transact
where it may already write, each write inside is judged by the same per-key rule, and a multi-page
call is still one approval because approval was already once per call rather than once per write.

1. **What the tree does with 300 pages.** 039 makes it listable; whether the page asks for one listing
   or one per folder is a page decision, and the host's answer is that both are affordable.

## Not in scope

- **The IDE.** Its document, shell, tabs, menu bar, keys, phone layout, tree, splitter, `ide.css`,
  `ide.js` and `/help` sections are the assistant's work against `/ide-plan`. This plan neither designs
  nor reviews them.
- **An editor route in the shell.** A `/pages/tree` that could save was the old shape; with 043 the
  editor may become sub-pages if it needs to, and they are pages.
- **A watcher job for the tree.** A job can poll head hashes and a page can long-poll for them
  (`IPageJobs`, `IWorkerContext`), and T9's one request per focus is enough for the first version. The
  mechanism is recorded here so the next version finds it.
- **Search inside a worker's run.** A worker's read budget is built once per job, so it does not lift
  the scan's ceiling; 038 gives the scan its own allowance instead.
- **Per-account preferences, favourites and tags.** [`no-account-store`](../backlog/no-account-store.md), unchanged, and the
  IDE plan itself declines to put its own state there.
