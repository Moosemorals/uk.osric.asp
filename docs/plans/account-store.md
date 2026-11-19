<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The per-account store

| | |
| --- | --- |
| Status | Proposed 2026-11-16 — waits for the owner. Nothing below is built |
| Size | Large as a programme, small as a step: six slices, each its own branch and its own stop. Three are `S` and three are `M`, and the first is the one worth arguing about |
| First customer | The assistant page's port ([`idea-conversation-is-not-a-page`](../backlog/idea-conversation-is-not-a-page.md) §3): a conversation's transcript is an ordered, filtered, paged list of one account's rows. Memory keeps `AssistantMemories` and stays the assistant's (`assistant-memory-belongs-to-the-assistant`); the memory view is that plan's, and this store's search is what will let it be a page |
| Spike | A scratch console over the real SQLite build before slice 1: an unpooled connection, a `TEMP VIEW` over a table with `INSTEAD OF` triggers, the four verbs through it, a trigger `RAISE` at a cap, and `SQLITE_LIMIT_ATTACHED = 0` |
| Decisions | None recorded yet. They are named at each merge (`product-plan-before-building`); this plan states the seven names it intends |
| Backlog | [`idea-no-account-store`](../backlog/idea-no-account-store.md) (this job), [`idea-conversation-is-not-a-page`](../backlog/idea-conversation-is-not-a-page.md) (the customer) |

## The short version

A page reaches three stores and none of them belongs to an account: `IPageStorage` is the page's and
every visitor shares it, `IPageSession` is one browser and goes when it signs out, and the page store
itself belongs to the site. So a page cannot keep a personal list, a preference or a record for the
person signed in, and a view that wants one has to be shell code that owns a table — which is the
third reason in `pages-page-first` and the reason such views are not written at all.

The owner's answer, recorded in [`idea-no-account-store`](../backlog/idea-no-account-store.md), is **SQL with one namespace per
account**: the page declares its tables and its columns, the host creates them, filters every statement
to the signed-in account, and bounds what a statement may do. This plan does not re-argue that. It
answers the seven questions the idea left to the plan, orders the work so each slice ships alone, and
states the four things that make it more than a capability:

- **The declaration is part of the revision**, extracted by the compiler that already has the page's
  syntax trees, applied in the save's own transaction and recorded in a catalogue. **The declaration
  is the table**: a save adds what is new and drops what the source no longer names. **A save is never
  refused for correctness**: a declaration the host cannot apply is a diagnostic stored with the
  revision, exactly as a compile error is, and the page answers "Handler failed to compile." until it
  is fixed — and a save that cannot be applied changes nothing, so it drops nothing either.
- **The owner filter is structural.** The page's SQL runs against a `TEMP VIEW`; the real table is a
  name it cannot type, and no page writes a `WHERE`.
- **The sandbox is a keyword allow-list and the limits SQLite itself exposes**, not a claim of safety.
  `pages-there-is-no-sandbox` still holds: a page runs in-process with the site's identity, and this is
  fencing for one statement, not a boundary for page code.
- **The caps are enforced inside the statement that writes**, the way `SqlitePageStateStore.UpsertAsync`
  already does, so a concurrent writer either fits or is refused.

The store's one entry point is the transaction handle the owner chose: `BeginTransaction()` returns an
`IDisposable` that rolls back on dispose, `CommitAsync()` commits and immediately begins the next
transaction, so a page does some work, commits it, and carries on. That shape decides the sandbox's
cost, the risks below, and why slice 2 has the deadline it has.

## The names this plan intends

Taken at each merge — bold rather than in backticks, because `make decisions` fails on a cited name the
record does not have yet:

- **pages-an-account-owns-its-rows** — the namespace is the account's, not the page's, so a page reads
  what another page declared for the same account. The consequence is stated rather than discovered: a
  page the assistant edits can read the owner's rows
- **pages-a-page-declares-its-tables** — the schema is the page's source, extracted by the compiler,
  judged on save, recorded in the catalogue and applied in the save's transaction
- **pages-a-statement-stays-in-its-tables** — one statement, four verbs, a `TEMP VIEW` for the owner,
  and no handle to the real table
- **pages-a-write-is-a-transaction** — the capability offers a transaction, not a statement: commit
  ends one and begins the next, dispose rolls the uncommitted one back
- **pages-a-store-is-bounded-per-account** — rows and bytes are capped per (account, page), and the cap
  holds inside the statement that writes
- **pages-a-save-stores-what-it-cannot-run** — a save is never refused for correctness: a declaration
  the host cannot apply is a diagnostic stored with the revision, and the page stops serving until it
  is fixed
- **pages-a-page-owns-its-schema** — the declaration is the table: a save drops a column the source no
  longer names, with every account's rows in it, and the host drops nothing on its own initiative

## The seven open questions, answered

### 1. The owner filter is a `TEMP VIEW`

Each declared table exists once, in `main`, under a name the page cannot type —
`PageAccount_<12 hex of the page key and table name>` — carrying `Owner TEXT NOT NULL`, a host-managed
`Id INTEGER PRIMARY KEY AUTOINCREMENT`, and one column per declared property. When a transaction begins
the host opens **its own unpooled connection** (`Pooling = False`, so the `temp` schema is unambiguous),
records the account in `temp.PageAccountOwner`, and creates in `temp` one view per declared table under
the name the page wrote:

```sql
CREATE TEMP VIEW "notes" AS
SELECT "Id", "Title", "Body" FROM "PageAccount_…"
WHERE "Owner" = (SELECT "Owner" FROM temp.PageAccountOwner);
```

with an `INSTEAD OF` trigger for each of `INSERT`, `UPDATE` and `DELETE` writing the owner back. The
page's SQL runs against the view, so it cannot name another account's rows, cannot reach the real
table, and needs no predicate it might forget.

*Rejected:* one table per account (no filter to get wrong, but a table per account per page and a
catalogue to keep), and a query API that takes a table, a filter, an order and a limit (safe, and no
longer SQL — which is the one thing a page can already write).

### 2. The schema is declared in the page's source

```csharp
<%!
[PageTable("notes", Search = "body")]
public sealed class Note {
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset Written { get; set; }
}
%>
```

`PageTableAttribute` is new in `Sdk`; the declaration is a type in a `<%! %>` block, which
`pages-a-page-may-declare-types` already allows. The compiler extracts it from the syntax trees it
already holds, so it is part of the revision, judged on save, and needs no second parse. The extract
is a `PageAccountSchema` value in `Core` and travels on `CompileResult` beside `Kind` and `Libraries`.

The column types are the ones the transcript and a preference list need, and no more: `string`,
`string?`, `long`, `double`, `bool` and `DateTimeOffset` (TEXT, `"O"`). Anything else is a diagnostic
(`PAGE007`), and a declared table in a library is one too (`PAGE008`) — a library has no key and no
rows of its own, so a declaration there would be a table nothing owns.

*Rejected:* a configuration section (the owner writes it once, and it drifts from the page that uses
it), and runtime DDL from a render (racy, and authority inside a render).

### 3. A save is never refused, and the page owns its schema

A save stores, whatever the declaration says. That is the site's existing rule for a template that
does not compile — it is stored with its diagnostic, the editor shows it, and the page answers "Handler
failed to compile." (`pages-what-a-diagnostic-is`, `pages-a-page-has-a-state`) — and the declaration is
no reason to break it. Somebody on their way out of the door saves and closes the editor; arguing about
correctness is not the editor's job.

**The declaration is the table.** A save creates a table it has not made, adds a column it has not
made, and drops a column or a table the source no longer names — `ALTER TABLE … DROP COLUMN`, with
every account's rows in it. The source is the schema, exactly as it is for the columns of a record
anywhere else, and a page that stops naming a column has said the data is no longer wanted. The
catalogue is what makes the comparison possible: it holds the declaration last applied, so the save
knows what to add and what is gone.

**The host drops nothing on its own initiative.** Not a column, not a table, not on a delete, not on a
save it cannot apply — and a declaration the host cannot apply applies nothing, so a broken save is
never a destructive one. Every drop comes from a line of page source that somebody checked in, and the
editor's diff shows it before the save does.

A save whose revision does not compile applies **no DDL at all** — not the additions and not the drops.
A broken edit is the one save that must never destroy anything, and it is the save most likely to be
made in a hurry.

A declaration the host cannot apply is a **diagnostic on the stored revision**, at the position of the
declared property, in the same diagnostics the compiler writes: an unsupported type (`PAGE007`), a
declaration in a library (`PAGE008`), a table or column count over its cap, a live column whose type
disagrees with the declaration, or a name another page's declaration already holds. The revision is
stored, nothing is applied, the page's state is "Compile failed", and the editor shows the line as it
shows any other save. Fixing the declaration and saving again applies it.

Deleting a page drops its tables and their rows in the same transaction as the tombstone — the rule
`PageState` already follows, for the same reason the code states: live data is not history, and a page
saved at the same key must not inherit what the page before it kept. `PageRevisions` stay, because the
tombstone is the page's history, and **a scrub is what takes the history** — and any table an older
build left behind. This qualifies `pages-page-storage` at the merge rather than taking a name: live
data, the page's own state and its declared tables alike, goes with the delete. `users remove` removes
the account's rows everywhere, because that is the account's own data and the operator's explicit act.

A delete now destroys every account's rows for the page, so the editor's existing confirmation — the
"Delete {key}?" page — says what it takes and what it keeps, and `/help` and `/privacy` say it too.

| What a page touched | At a delete | At a scrub |
| --- | --- | --- |
| Its own state (`PageState`) | Deleted, in the tombstone's transaction | Deleted again, for a database written by an older build |
| A declared table and every account's rows | Deleted, in the same transaction | Nothing left to take |
| Its revisions and the seed anchor | Kept — the tombstone is the page's history | Deleted |
| A visitor's session values (`PageSessions`) | Untouched: one browser's data, swept at sign-out or when it goes idle | Untouched |
| The assistant's memory | Untouched: the assistant's, keyed by owner | Untouched |

The last two rows are why the rule is not "a delete takes everything a page touched": a session belongs
to a browser and memory belongs to the assistant, and neither is the page's to destroy.

*Rejected:* an additive schema, which makes the host the custodian of rows a page has abandoned and
turns `MaxColumnsPerTable` into a lifetime ceiling; and a separate retirement marker, which is the same
rule with a second vocabulary for a page that already says what it means in its source.

### 4. A write is a transaction the page ends

```csharp
using IPageAccountTransaction tx = ctx.Capabilities.GetRequired<IPageAccountStorage>().BeginTransaction();
await tx.ExecuteAsync("INSERT INTO notes (Title, Body, Written) VALUES (@title, @body, @written)",
    new PageParameters().Add("title", title).Add("body", body).Add("written", now));
await tx.CommitAsync();   // committed, and a fresh transaction is open
```

`BeginTransaction` is the capability's only member; `QueryAsync`, `ExecuteAsync`, `SearchAsync` and
`CommitAsync` live on the handle, which is `IDisposable` and rolls back what is uncommitted when it is
disposed. `CommitAsync` commits and immediately begins the next transaction, so a page can do some
work, commit it, and carry on without asking for a new one.

A page that never commits writes nothing. The handle is a render-bound capability
(`IRenderBoundCapability`, `IRenderLifetimeAware`), so an abandoned render rolls back and releases the
connection, and `Pages:Account:TransactionMaxSeconds` bounds one that is merely slow.

### 5. Per account really does mean cross-page readable

Confirmed, and the customer is the reason: a run is a **worker**, so the transcript row is written by a
job and read by a page. `PageJobRunner` already binds its scope to `job.Owner`, so the job's transaction
and the page's are the same account's rows. Only an account-shaped namespace lets one write what the
other reads, and `IPageSession`'s reads already follow the same rule for one visitor.

The consequence is written down, not discovered: a page the assistant edits can read what another page
stored for the owner. That is the site's model — every account is an owner (`product-one-owner`) and
pages are the owner's code.

### 6. One type, `IPageAccountStorage`

One type, registered for every content layer and every layout, and for a worker bound to the job's
owner. It refuses a write where page storage already refuses one — a `HEAD`, a layout, an ended render
— and it refuses an anonymous render at `BeginTransaction` with the sign-in sentence
(`UserFacingException`), because there is no account to own a row.

`IPageAccountStorage` reads beside `IPageStorage` and `PageUser`, and the `PageParameters` and
`PageRow` values it hands back keep `object` and SQLite's storage classes out of page code: a `PageRow`
has `GetString`, `GetInt64`, `GetDouble`, `GetBoolean`, `GetDateTimeOffset` and `IsNull`, and a column
the statement did not produce is a page bug.

### 7. FTS5 is declared per table and asked for by a call

`Search = "body"` on the declaration creates an external-content FTS5 index and its three triggers with
the table and drops them with it, exactly as `AssistantMemorySearch` is built by hand today. The page
asks through `SearchAsync(table, term, limit)` on the transaction rather than with `MATCH` in its own
SQL, because a view cannot carry `MATCH` and the index itself holds every account's rows — a leak of
existence, not of content, that the store need not accept. The tokenizer is an invariant in code, not a
setting: it is the same one the assistant's memory uses, and a page cannot weaken it.

## The options

One section, `Pages:Account`, read through `ConfigurationReader` into a `PageAccountOptions` record with
a `FromConfiguration` factory, and each value added by the slice that enforces it
(`code-policy-values-are-configuration`). A missing or out-of-range value stops the site at startup with
the key, the accepted range and the recommended value.

| Value | Shipped | Slice | What it bounds |
| --- | --- | --- | --- |
| `MaxTablesPerPage` | 8 | 0 | how many tables one page may declare |
| `MaxColumnsPerTable` | 24 | 0 | how many columns one table may declare |
| `MaxRowsPerPage` | 10 000 | 2 | rows an account may hold across one page's tables |
| `MaxPageBytes` | 128 MiB | 2 | bytes an account may hold across one page's tables |
| `MaxResultRows` | 500 | 1 | rows one query may return |
| `MaxResultChars` | 256 KiB | 1 | characters one result set may total |
| `CommandTimeoutSeconds` | 5 | 1 | how long one statement may run |
| `TransactionMaxSeconds` | 10 | 1 | how long one transaction may stay open |

The byte cap is where a mistake meets a refusal, not a budget a page is rationed from: at 128 MiB it
no longer bounds the database on its own, which is why the storage figure slice 5 adds is part of the
same programme rather than a nicety.

## Slices

Each is one branch, one step through the gate and one stop for the owner. A long plan merges phase by
phase so a regression is bisectable, and its branch goes when its last slice is in.

0. **The declaration and its tables** (`S`). `PageTableAttribute` in `Sdk` (plus `PageRow` and
   `PageParameters`, in `PublicAPI.Unshipped.txt`), `PageAccountSchema` in `Core`, extraction and its
   two diagnostics in `Runtime`, `Pages:Account` with its first two values, a `PageAccountTables`
   catalogue migration, and the DDL in the save's transaction: create, `ALTER … ADD COLUMN`, `ALTER …
   DROP COLUMN` for what the source no longer names, and a diagnostic stored with the revision when the
   declaration cannot be applied — applying nothing. Delete drops the tables and the catalogue in the
   tombstone's transaction, beside the state it already drops; scrub removes the revisions, and any
   table left by an older build.
   *Proves:* a save creates exactly the declared tables and catalogue rows; adding a column alters and
   changes nothing else; a column the source no longer names is dropped with every account's rows; a
   table it no longer names goes the same way; a revision that does not compile applies no DDL at all;
   a declaration the host cannot apply stores the revision, reports `PAGE007` at the property, makes
   the page answer "Handler failed to compile." and leaves the live schema and every row untouched;
   deleting the page drops its tables and every account's rows with the tombstone, and leaves the
   revisions; scrubbing it removes the catalogue and the history; a library's declaration is `PAGE008`;
   a page with no declaration creates nothing.
1. **The read** (`M`). The capability, the handle, the unpooled connection, the `TEMP VIEW` and the
   owner row, `QueryAsync`, `PageRow` and `PageParameters`, the keyword allow-list (`SELECT` for a
   query; `INSERT`, `UPDATE`, `DELETE` for a write) and the single-statement check, `SQLITE_LIMIT_ATTACHED
   = 0`, `SQLITE_DBCONFIG_DEFENSIVE`, `trusted_schema = OFF`, the result caps, the timeouts and the
   anonymous refusal.
   *Proves:* a query returns only my account's rows; `ATTACH`, `PRAGMA`, `CREATE`, `VACUUM` and a
   second statement are refused before anything runs; the real table cannot be named; a layout reads
   and an anonymous render is refused; a result over its row or character cap is cut and says so; the
   deadline interrupts the statement.
2. **The write** (`M`). `ExecuteAsync`, the three `INSTEAD OF` triggers, the owner written back, the
   row and byte caps enforced in the trigger (`SELECT RAISE(ABORT, …) WHERE (SELECT COUNT(*) …) >=
   cap`), `CommitAsync` beginning the next transaction, and dispose rolling back.
   *Proves:* an insert lands with my owner and no other; an update or delete touches only my rows; the
   caps refuse at the boundary and the message names the cap; an uncommitted write is gone after
   dispose; a commit followed by a second write lands two; a `HEAD` and a layout may not write.
3. **The search** (`S`). `SearchAsync`, the external-content index and its triggers created and
   dropped with the table.
   *Proves:* a search returns my rows and never another owner's; the index follows an insert, an
   update and a delete; the index's table cannot be named by a page.
4. **The worker** (`S`). The capability offered to a job, bound to `job.Owner` as `IPageJobs` already
   binds its scope.
   *Proves:* a job writes its starter's rows and not another account's; a render and the job it
   started see the same rows.
5. **The ends** (`M`). `users remove` deletes the account's rows from every declared table; the
   storage sampler counts the account tables, because its payload formula hand-lists `PageRevisions`
   and `PageState` and would otherwise silently stop counting; `/privacy` gains its paragraph,
   including what a page edit, a delete and a scrub can and cannot reach; `/status` lists the declared
   tables.
   *Proves:* removing an account leaves no row of it anywhere; the storage figure moves when an
   account writes; a deleted page's tables are gone from the catalogue and the database, and a scrub
   leaves no revision of it behind either.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/` | `PageTableAttribute`, `IPageAccountStorage`, `IPageAccountTransaction`, `PageRow`, `PageParameters`; `PublicAPI.Unshipped.txt` |
| `src/uk.osric.asp.Core/` | `PageAccountSchema` and its validation, the statement allow-list and its refusal values |
| `src/uk.osric.asp.Runtime/` | Declaration extraction in the compile, `CompileResult.Schema`, `PAGE007` and `PAGE008` |
| `src/uk.osric.asp.Web/Pages/` | `PageAccountOptions`, the scope registered in `PageChain.CreateLayer` for every layer and in `PageJobRunner` for a job, the DDL in the saver's transaction, and the drop in `SqlitePageStore.DeletePageAsync` beside the state it already removes |
| `src/uk.osric.asp.Web/Storage/` | The `PageAccountTables` catalogue and its migration, the view and trigger DDL, the sampler's payload |
| `src/uk.osric.asp.Web/Pages/Seeds/` | `help` (the declaration, the two calls, the caps, the refusals and the lifetime), `privacy`, and the `pages` editor's "Delete {key}?" page, which says what a delete takes |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | A "Per-account storage" section and `Version` to `page-author-v35` |
| `src/uk.osric.asp.Web/appsettings.json` | The `Pages:Account` section |
| `deploy/operations.md` | One line under storage: a save drops what the declaration left, a delete takes the page's live data with its tables, and a scrub takes the history |

## Tests

- **`Core.Tests`** for the schema value and the statement allow-list: no columns, an unsupported type, a
  `DROP`, a second statement, `ATTACH` — values, no database.
- **`Runtime.Tests`** for the extraction and its two diagnostics, beside the shape check.
- **`Web.Tests`** for the DDL lifecycle and the handle against a throwaway database: create, add,
  drop, store-and-report, drop at scrub, commit, roll back, cap, search. One behaviour per test, named
  for the behaviour.
- **`Web.IntegrationTests`** for one page that declares a table, writes a row and reads it back, then
  asks for the same row as a second account — the only test that proves the owner filter across the
  real pipeline.
- **Nothing in a browser test**: none of this has markup.
- `make check` is the gate for every slice and `make decisions` is what proves the names, at the merge.

## Docs

- **`/help` in the same change** for everything a page author gains — the declaration, the handle, the
  caps, the refusals and the lifetime (`product-manual-is-a-page`).
- **`PageAuthorPrompt` and its `Version`**, or the assistant keeps reaching for `IPageStorage` and a
  shared row.
- **[pages](../pages.md)** counts a twelfth capability and gains its section, **[storage](../storage.md)**
  gains the tables, the catalogue and the DDL lifecycle, and the **[glossary](../glossary.md)** defines
  the term.
- **`/privacy` and `deploy/operations.md`** for a store that survives sign-out and dies with the page —
  the privacy page names what a delete takes and what a scrub takes, in the same paragraph as the
  backup caveat.
- **[the record](../decisions.md)** at each merge, and this plan and
  [`idea-no-account-store`](../backlog/idea-no-account-store.md) pointed at what landed.

## Risks

- **A page can hold the site's write lock.** One database means one writer, so a page that writes and
  then streams for a minute stalls the editor, the session store and every save behind it.
  `Pages:Account:TransactionMaxSeconds`, the render's deadline and `AttachLifetime` are the bounds; a
  deferred transaction takes no write lock until its first write, and every commit releases it.
- **The owner filter is one generated statement away from a leak.** A view that forgets `Owner`, a
  trigger that writes a null, or a guessable real name all break the guarantee. The names are hashed per
  page, generation is one function with one test, and slice 1's integration test asks for another
  account's row.
- **A page edit can destroy a column of every account's data.** That is the rule the owner chose: the
  declaration is the table, so deleting a property and saving drops the column with its rows. The
  editor's diff is the warning, and the assistant's edit safety (`assistant-edit-page-is-safe`) is the
  guard on the one author most likely to tidy a declaration — a restore of an older revision is the
  other path, because it is an ordinary save, and it drops whatever the older source does not name.
- **A delete destroys every account's rows for the page.** That is the owner's rule, and it is the same
  one `PageState` follows: live data is not history. The editor's "Delete {key}?" page is the guard, and
  it must say what the delete takes — the page's state and every account's rows — beside what it keeps,
  which is the revisions. A visitor reading `/privacy` learns the same thing without opening the editor.
- **A broken declaration stops the page, not the save.** That is the owner's rule and it is the right
  one, and its cost is that a page can be saved into a state where it does not serve. The editor must
  say so on the same save, in the diagnostics it already shows, and `/admin/restore` plus the scrub are
  the ways back. No page state is invented: a declaration error is a compile failure, which the listing
  already speaks.
- **A column nobody declares is invisible but still there.** An operator reading the database sees
  columns no page names, holding rows no view exposes. That is the price of never dropping data, and it
  is why the catalogue is the record of what is applied rather than the table's own shape.
- **Six stops for the owner.** Each slice is small enough to back out on its own; 3 and 4 are
  mechanical once 1 and 2 are in.
- **A contract change.** `Sdk` gains five public types, so the page contract's version moves and every
  stored image recompiles on its next request — the ordinary cost of a change here, and recoverable in
  the editor and from `/admin/restore`.

## Open questions

1. **Slices 1 and 2 as one stop or two.** A store a page can read but not write is useless in a
   release, but the sandbox is the risk and reading alone proves it. Two stops is the proposal; one is
   fine if the owner prefers fewer.
2. **How a page names its parameters.** A `PageParameters` value with `Add` is the proposal because it
   keeps a `Dictionary<string, object?>` out of page source; a dictionary named directly is the
   alternative if the value costs more than it saves.
3. **What `/status` shows of a declaration.** The catalogue makes every table listable; whether the
   status page shows the tables, their row counts, or only the totals is a shell decision that slice 5
   can take either way.

## Not in scope

- **Memory.** `AssistantMemories`, its versions, pins and FTS5 stay the assistant's
  (`assistant-memory-belongs-to-the-assistant`); the view arrives with [`idea-conversation-is-not-a-page`](../backlog/idea-conversation-is-not-a-page.md),
  and this store's search is what will let it be a page.
- **Sharing, roles, or a row another account can read.** One owner per row,
  `product-one-owner`.
- **Migrating existing tables.** Nothing exists to migrate, and an archive is pages and page state: it
  never carries account rows (`pages-an-archive-is-a-database`).
- **A store for an anonymous visitor.** Refused at `BeginTransaction` with the sign-in sentence.
