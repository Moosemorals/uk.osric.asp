<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-033 — A page has no persistent per-account store

| | |
| --- | --- |
| Size | L — a capability, a migration, a schema lifecycle, a statement sandbox, a quota per account, and the tests and docs for each. It wants a plan of its own |
| Area | Sdk, Web (storage and pages), docs |
| Location | `src/uk.osric.asp.Sdk/`, `src/uk.osric.asp.Web/Pages/`, `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

A page can reach three stores, and none of them belongs to an account:

| Store | Whose | How long |
| --- | --- | --- |
| `IPageStorage` | the page — every visitor shares it | until the key or the page goes |
| `IPageSession` | one browser | until sign-out, or 30 minutes idle (8 hours signed in) |
| `IPageCatalog` / `IPageWriter` | the page store itself | forever |
| **nothing** | **one signed-in account** | — |

So a page cannot keep a personal list, a preference or a record for the person signed in. A page that
wants one has to be shell code that owns a table, which is the third reason in DEC-210's rule and the
reason such views do not get written at all. The gap has been named since page state landed: DEC-125's
open-policy note says per-user state is "a later job with its own migration and decision", and the
identity hook it needs has existed since DEC-132–DEC-137. This is that job.

**The shape the owner chose is SQL, one namespace per account.** A page declares its tables and its
columns; the host creates them, filters every statement to the signed-in account, and bounds what a
statement may do. The alternatives — JSON documents keyed per account, or one store per
(account, page) — are in [Considered and rejected](#considered-and-rejected).

Why SQL rather than documents:

- **The things a per-account page wants are a list, an order, a filter and a search.** Documents would
  either load every value into the render or grow a query language of their own.
- **The host can build the index the page cannot.** A declared column can be a key, an index or an
  FTS5 index, created with the trigger discipline the assistant's memory migration had to write by
  hand (`AssistantMemorySearch`), and maintained by the host rather than by page code.
- **Aggregates and pagination stay inside SQLite**, which is the one place they are cheap at any size.

Why one namespace per account:

- **The data is the account's, not the page's.** A page that shows a profile another page keeps, and a
  later assistant page reading what a memory view wrote, are one account's rows.
- **It is the rule `IPageSession`'s reads already follow**: a page may read another page's value for
  the same visitor, because the visitor is the person the data is about.
- **The consequence is written down, not discovered.** A page the assistant edits can read what another
  page stored for the owner. That is the site's model — every account is an owner (DEC-03) and pages are
  the owner's code — but it is a fact the plan states.

## Sketch

1. **The capability.** One type offered through `ctx.Capabilities`, with a read and a write that are
   told apart the way page storage and the session already are: `QueryAsync(statement, parameters)`
   returns bounded rows, `ExecuteAsync(statement, parameters)` answers how many rows changed. The first
   keyword of each statement is checked against an allow-list (`SELECT` for a query;
   `INSERT`, `UPDATE`, `DELETE` for an execute), so a write call cannot smuggle DDL. An anonymous render
   is refused by the capability with the sign-in sentence (DEC-190's rule); a layout and a `HEAD` are
   offered the read and never the write; a second write in one render is a page bug. The name is to
   settle — `IPageAccountStorage` reads beside `PageUser`, `IPageUserStore` beside `IPageStorage` — and
   the split between one type and a read/write pair is one of the questions below.

2. **The schema is declared by the page.** The starting point is a declaration that generation already
   sees, like a library's shape rule (DEC-283): a table is a type the page declares, with its columns as
   properties, so the schema comes from the source and a restore restores it.

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

   The alternatives are in the questions; this one is preferred because the schema is part of the
   revision, judged on save rather than created by a render, and because the host can create the FTS5
   index and its triggers from the same declaration.

3. **The owner filter is structural.** The real tables carry an `Owner` column and a name the page cannot
   type; each connection gets a `TEMP VIEW` under the name the page declared, selecting the account's
   rows, and `INSTEAD OF` triggers write the owner back. A page's SQL runs against the view, so it
   cannot name another account's rows, cannot reach the real table, and needs no predicate it might
   forget. The three candidate mechanisms and their costs are the first open question.

4. **The sandbox.** One statement per call, parameters rather than literals, a command timeout and a
   cap on returned rows and bytes, from configuration. `trusted_schema = OFF`, no `ATTACH`, no `PRAGMA`,
   no DDL, no `VACUUM`; an authorizer refuses what the allow-list misses. The page API already denies
   `Microsoft.Data.Sqlite` (DEC-35), so a page cannot open a database of its own.
   [SQLite's security notes](https://www.sqlite.org/security.html) are where the assumptions start: the
   library is not defended against hostile SQL, so the host must be — nothing a page supplies may
   escape its declared tables and the four verbs.

5. **The quotas.** Tables per page, columns per table, rows and bytes per (account, page), and the
   result cap. A new `Pages:UserStore` section, every value required and a missing or out-of-range one
   stopping the site at startup (DEC-282). Each write enforces its quota in the same statement, the way
   `SqlitePageStateStore.UpsertAsync` does.

6. **The lifecycle.** A declaration on save adds tables and columns; a column that disappears from the
   declaration is refused or confirmed as destructive, never dropped silently; a page deleted takes its
   tables in the same transaction as the tombstone, and a scrub takes them with the revisions;
   `users remove` deletes the account's rows from every table; a write that was already running when
   the page was deleted is refused rather than recreating anything.

7. **Personal data has its own rules.** An archive never carries it — the export is pages and page
   state, and a session is deliberately not in one either (DEC-139). `/privacy` gains a paragraph for a
   store that survives sign-out, and says what a delete cannot reach: backups, snapshots and the
   operator's copies, exactly as a scrub does.

8. **The documentation.** A capability a page author can use is one of the things `/help` must cover in
   the same change (DEC-173): the declaration, the two calls, the quotas, the refusals and the
   lifetime. `PageAuthorPrompt` gains its section and `Version` bumps; [pages](../pages.md) counts an
   eleventh capability; [storage](../storage.md) gains a section for the new tables; the
   [glossary](../glossary.md) defines the term.

## Open questions

Each is a decision for the plan this job wants.

1. **How the owner filter becomes structural.** A per-connection `TEMP VIEW` plus `INSTEAD OF` triggers
   (one table, one owner column, no schema growth) against one table per account (no filter to get
   wrong, but a table per account per page and a catalogue to keep) against a query API that takes a
   table, a filter, an order and a limit and builds the SQL (safe, and no longer really SQL). The first
   is the one to beat.
2. **Where the schema is declared.** A declared type in the page's source (preferred: it is part of the
   revision and generation can judge it), a configuration section (the owner writes it once, but it
   drifts from the page that uses it), or runtime DDL from a render (racy, and authority inside a
   render).
3. **What a declaration that no longer matches the live table does.** The stored revision stays, the
   page's queries against a removed column fail, and something has to say so — the editor, `/status`, or
   the render's error page.
4. **Whether a write is one statement or a bounded transaction.** One statement is simple and enough for
   a list; two inserts that must both land are a different primitive (DEC-125 says the same about an
   atomic increment).
5. **Whether per-account really means cross-page readable.** The chosen namespace says yes; the plan
   should confirm it against the case of a page the assistant wrote reading another page's rows.
6. **The name and the split.** One type or a read/write pair, and which name sits best beside
   `IPageStorage`, `IPageSession` and `PageUser`.
7. **FTS5.** Whether search is declared per table or asked for per column, and how the index and its
   three triggers are created and dropped with the table.

## What would make it unnecessary

If every page that wants per-account data keeps fewer than a screenful of values and never orders,
searches or counts them, JSON documents keyed by account would do at a fraction of the cost — no schema,
no sandbox, no migration of a declaration. The trigger to revisit that is a page whose data fits in one
document; the trigger to want this job is a page that wants an owner's rows ordered, filtered or
aggregated. If the only per-account data the site ever needs is the assistant's memory, nothing is
needed at all: memory is the assistant's own, with its own store (DEC-291).

## Not in scope

- **Memory.** It stays the assistant's and its view arrives with the assistant page (DEC-291,
  [IDEA-025](IDEA-025.md)).
- **Sharing, public rows and roles.** One owner per row, no sharing between accounts, no roles
  (DEC-02, DEC-03).
- **Migration of existing tables** into the new store. Nothing exists to migrate.
- **Moving the data between sites.** An archive is pages and page state.

## Considered and rejected

- **JSON documents per (account, page), the shape `IPageStorage` has.** The cheapest thing that would
  work, and the shape `IPageStorage` and `IPageSession` already have. Rejected as the first version because ordering,
  filtering, aggregation and search are exactly what a per-account page wants, and a document store
  grows either a query language or a full load.
- **One store per (account, page).** Isolation is tidier, but the data belongs to the account: a page
  that shows a profile another page keeps is one account's rows, and the read rule already allows it
  for sessions.
- **A database file per page, attached per request.** Isolation for free, and a volume to back up, grow
  and sweep, plus a file-per-page failure mode the single SQLite database does not have.
- **Doing it now, because the gap is real.** The gap is real and no page uses the fix yet. A capability
  arrives with the view that needs it, and this one needs a plan, a migration and a named first
  customer first.

**Origin:** [the page-first re-scope](../plans/page-first.md) of 2026-11-14, recorded as DEC-291. The
gap was first named in DEC-125's open-policy note; the per-account SQL shape, the notice that a page the
assistant edits can read another page's rows, and the choice against JSON documents are this job's.
