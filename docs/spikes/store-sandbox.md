<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — the record sandbox (`storage-is-not-one-model`)

| | |
| --- | --- |
| Question | Can one unpooled SQLite connection, a `TEMP VIEW` per declared table filtered by a literal subject, and `sqlite3_set_authorizer` give a page SQL that sees only its own subject's rows, allows joins and aggregates, refuses everything else by SQLite's own parse, and cannot be escaped by spelling? |
| Answered | 2026-11-22 |
| Version measured | SQLite 3.53.4 (source id `bf7c7f30…`), `Microsoft.Data.Sqlite` 11.0.0, `SQLitePCLRaw.core` 3.0.5, .NET SDK 11.0.100 |
| Verdict | Yes. The fence held under every spelling the console threw at it, and six details in the plan's sandbox section were wrong and are corrected there |
| Recorded | Nothing decided. The findings amend [the storage model plan](../plans/storage-model.md#the-record-sandbox); the code arrives with slice 2 |

## The answer

A scratch console over the real SQLite build (`Microsoft.Data.Sqlite` 11.0.0, which pins
`SQLitePCLRaw.lib.e_sqlite3` 3.53.4) built the shape the plan describes — one unpooled connection, a
`TEMP VIEW` per declared table with the subject as a literal in its `WHERE` and an `INSTEAD OF` trigger
for each write, and an authorizer that allows only the action-and-name pairs the section lists — and
then attacked it. Thirty-two probes passed and none showed a leak. The load-bearing results:

- A page's `SELECT` and a two-table join with `COUNT(*)` answered only the subject's rows; the real
  table could not be named directly or through a sub-query.
- `sqlite_master`, `sqlite_temp_master`, `PRAGMA`, `ATTACH`, `CREATE`, `DROP`, `ALTER`, `REINDEX`,
  `ANALYZE`, `BEGIN` and `SAVEPOINT` were all refused with `SQLite Error 23: not authorized`, and the
  callback recorded the action and the name it refused.
- `load_extension` was refused as a `FUNCTION` in all four spellings tried — bare, double-quoted,
  bracketed and backticked — and `pragma_table_info` was refused as a `READ` of a virtual table the
  handle does not own.
- A write through the view stamped the literal owner; a write naming `Id` was refused by the trigger;
  and a row cap written as `RAISE(ABORT, …)` inside the statement refused the write, named the setting
  in the message, and left the row count untouched.
- `sqlite3_interrupt` ended a runaway statement in about 200 ms, and `SQLITE_LIMIT_ATTACHED = 0`,
  defensive mode and `trusted_schema = OFF` each refused what they were set to refuse.

The six corrections are in [what this changes in the plan](#what-this-changes-in-the-plan): the CTE
rule, the function name's parameter, the cap trigger in the allowance, the reach of `trusted_schema`,
the fact that `CommandTimeout` is not a statement clock, and how the one-statement rule is detected.

## What was measured

### The harness

One console project, `net11.0`, one package reference (`Microsoft.Data.Sqlite` 11.0.0), and a file
database opened with `Pooling=False`. The connection set its pragmas first — `journal_mode=WAL`,
`busy_timeout=5000`, `trusted_schema=OFF`, `SQLITE_LIMIT_ATTACHED = 0`,
`SQLITE_LIMIT_SQL_LENGTH = 100_000` — and `SQLITE_DBCONFIG_DEFENSIVE`,
`SQLITE_DBCONFIG_TRUSTED_SCHEMA = 0` and `SQLITE_DBCONFIG_ENABLE_LOAD_EXTENSION = 0` through
`sqlite3_db_config`, then installed the authorizer through `sqlite3_set_authorizer`. The console is
scratch: this document is what the slice commits, and the connection code arrives with slice 2.

The authorizer was a delegate with the shape the package exposes:

```csharp
int Authorize(object user, int action, utf8z p0, utf8z p1, utf8z dbName, utf8z inner);
```

It allowed `SELECT` and `RECURSIVE`, a closed function list, reads and writes of the handle's own view
names, and a read or write of a real table only when `inner` was one of the generated views or
triggers. Everything else was `SQLITE_DENY`. A boolean `host` flag in the user data allowed the
statements that create and seed the schema, and nothing a page writes could set it. Every callback was
logged as `action name column db=<db> inner=<view-or-trigger>` so the arguments the plan reasons about
could be read rather than assumed.

### The views

Two real tables held two subjects' rows, and the views were the generation the plan describes:

```sql
CREATE TEMP VIEW "notes" AS
SELECT "Id", "Title", "Body" FROM "Declared_notes" WHERE "Owner" = 'session:one';

CREATE TEMP TRIGGER "notes_insert" INSTEAD OF INSERT ON "notes" BEGIN
  SELECT CASE WHEN NEW."Id" IS NOT NULL THEN RAISE(ABORT, 'a Visitor table does not accept Id') END;
  INSERT INTO "Declared_notes" ("Owner", "Title", "Body")
  VALUES ('session:one', NEW."Title", NEW."Body");
END;
```

A Page-subject table was the same generation without an `Owner` column and without a predicate. The
row cap was a `BEFORE INSERT` trigger on the real table, refusing when the subject held two rows.

### What the callback reports

The trace is the plan's evidence, and it is worth quoting exactly. A join read the owner's rows through
the real table and the view:

```text
READ Declared_notes Id db=main inner=notes
READ Declared_notes Title db=main inner=notes
READ Declared_notes Body db=main inner=notes
READ Declared_notes Owner db=main inner=notes
READ Declared_comments Id db=main inner=comments
READ Declared_comments NoteId db=main inner=comments
READ notes Title db=temp inner=-
FUNCTION - count db=- inner=-
```

So the real table is named **with the view that reached it** in `inner_most_trigger_or_view`; a view
read is named with `db=temp` and no inner name; and a function's name is in **`p1`**, the second
parameter, while `p0` is null. A write through the view reported the view, the generated trigger, and
the cap trigger:

```text
INSERT notes - db=temp inner=-
SELECT - - db=- inner=notes_insert
READ notes Id db=temp inner=notes_insert
INSERT Declared_notes - db=main inner=notes_insert
READ Declared_notes Owner db=main inner=notes_cap
FUNCTION - count db=- inner=notes_cap
```

The cap trigger is a generated trigger too, and its read of the real table is only allowed if the
allowance names every generated trigger rather than the three write triggers.

### The refusals

Each of these answered `SQLite Error 23: not authorized`, and the callback's recorded action and name
are what a refusal message can quote:

| Statement | Action and name recorded |
| --- | --- |
| `SELECT "Title" FROM "Declared_notes"` | `READ Declared_notes` |
| `SELECT (SELECT "Title" FROM "Declared_notes" LIMIT 1) FROM "notes"` | `READ Declared_notes` |
| `SELECT "name" FROM "sqlite_master"` | `READ sqlite_master` |
| `SELECT "name" FROM "sqlite_temp_master"` | `READ sqlite_temp_master` |
| `PRAGMA table_info("notes")` | `PRAGMA table_info` |
| `ATTACH DATABASE ':memory:' AS "other"` | `ATTACH :memory:` |
| `CREATE TABLE "x" (…)` | `INSERT sqlite_master` |
| `DROP TABLE "Declared_notes"` | `DELETE sqlite_master` |
| `ALTER TABLE "Declared_notes" ADD COLUMN …` | `ALTER_TABLE main` |
| `REINDEX "ix_notes"` | `REINDEX ix_notes` |
| `ANALYZE` | `ANALYZE Declared_notes` and its siblings |
| `BEGIN` | `TRANSACTION BEGIN` |
| `SAVEPOINT "s"` | `SAVEPOINT BEGIN` |
| `SELECT load_extension('x')` and its three spellings | `FUNCTION load_extension` |
| `SELECT "name" FROM pragma_table_info('notes')` | `READ pragma_table_info` |
| `SELECT randomblob(4)` | `FUNCTION randomblob` |

Two results in that table are worth naming. `pragma_table_info` is not a function call in the
authorizer's eyes — it is a read of a virtual table with that name, so the function list does not see
it and the read allow-list refuses it by omission. And an unknown function never reaches the callback
at all: `SELECT no_such_function(1)` answered SQLite's own `SQLite Error 1: no such function`, so a
closed function list only decides among functions SQLite knows.

### The write side

- The insert through the view wrote `Owner = 'session:one'`, and the host's read of the real table
  confirmed it.
- `INSERT INTO "notes" ("Id", "Title") VALUES (99, 'collide')` answered
  `SQLite Error 19: a Visitor table does not accept Id`, and wrote nothing.
- The cap trigger answered `SQLite Error 19: MaxRowsPerSubject reached: 2`; the subject's row count was
  still two afterwards. The refusal is inside the statement and carries the setting's name, which is
  what `Full` is built on.
- `UPDATE` and `DELETE` through the view reached the owner's rows only; the other subject's row was
  untouched.

### The limits, the defensive flag and `trusted_schema`

| Setting | What the console measured |
| --- | --- |
| `SQLITE_LIMIT_ATTACHED = 0` | `ATTACH` answered `too many attached databases - max 0`, even with the host flag on |
| `SQLITE_DBCONFIG_DEFENSIVE = 1` | `PRAGMA writable_schema=ON` was accepted, and `UPDATE sqlite_master SET sql=sql` answered `table sqlite_master may not be modified` |
| `trusted_schema = OFF` | a **main**-schema view over a non-innocuous function answered `unsafe use of probe_fn()`; the same view created in `temp` did not, and was left to the authorizer, which answered `not authorized to use function: probe_fn` until the name was allowed |
| FTS5, external content | a `notes` table, an external-content `notes_fts` index and its three triggers were created and searched with `trusted_schema = OFF`; the match answered the row |

The `trusted_schema` result matters because the plan claimed the setting as a fence over the views.
It is a fence over `main` — the declared tables and their generated FTS5 triggers — and the temp views
are governed by the authorizer's function list. Neither is weaker than the plan needed; the section
now says which is which. The FTS5 result rules out an exemption or a second connection for the index.

### The clocks

`sqlite3_interrupt` from a timer ended a recursive CTE in about 200 ms and answered
`SQLite Error 9: interrupted`. Nothing in `Microsoft.Data.Sqlite` did it on its own. Measured against
a safety interrupt at three seconds, a one-second clock never fired:

| Attempt | When the statement stopped |
| --- | --- |
| `CommandTimeout = 1`, `ExecuteScalar` | 3,000 ms, by the safety interrupt |
| `CommandTimeout = 1`, `ExecuteScalarAsync` | 3,002 ms, by the safety interrupt |
| `DefaultTimeout = 1`, `ExecuteScalar` | 3,001 ms, by the safety interrupt |
| `ExecuteReaderAsync(token)` cancelled at 1 s | 2,998 ms, by the safety interrupt |
| `ExecuteScalarAsync(token)` cancelled at 1 s | 3,001 ms, by the safety interrupt |

`CommandTimeout` is a busy timeout — how long a statement waits for a lock — not a statement clock.
`StatementMaxSeconds` and `TransactionMaxSeconds` must arm a timer that calls `sqlite3_interrupt`, the
pattern `PageStorageSampler` already uses. This changes no interface and adds no dependency; it rules
out the shortcut the plan's wording invited.

### One statement per call

`SELECT 1; INSERT INTO "notes" ("Title") VALUES ('smuggled')` ran both statements, and every callback
the second statement made was allowed. The rule is therefore one the store enforces, not something
the authorizer provides — and it is enforced by SQLite's own parser, not by scanning for semicolons.
`SQLitePCLRaw` exposes `sqlite3_prepare_v2(sqlite3, string, out sqlite3_stmt, out string tail)`, so the
store can prepare the text, take the tail and prepare the tail again: a second statement that resolves
is a refusal. Measured:

| Text | Statements the tail-driven prepare found |
| --- | ---: |
| `SELECT 1` | 1 |
| `SELECT 1;` | 1 |
| `SELECT 1;` followed by spaces, tabs and a newline | 1 |
| `SELECT 1; -- trailing comment` | 1 |
| `SELECT 1; /* trailing comment */` | 1 |
| `SELECT ';' AS "a"` | 1 |
| `SELECT 1;;` | 1 |
| `SELECT 'x;y' AS "a"; SELECT 2` | 2 |
| `SELECT 1; DROP TABLE "t"` | 2 |

A trailing semicolon, whitespace, a comment and a semicolon inside a string literal all count as one
statement, and only a second statement that parses counts as two. The plan said the prepare tail "is
not exposed"; that is true of `SqliteCommand` and not of the handle the store already uses for the
authorizer and the interrupt.

### Pooling

`Pooling = False` behaved as the plan says: a temp view created on one connection was gone —
`no such table: v` — after the connection closed and the string opened another. `Pooling = True` handed
the same connection back and the view was still there. A pooled connection would therefore carry both
the views and the authorizer of whichever handle it last served.

`Pooling = False` behaved as the plan says: a temp view created on one connection was gone —
`no such table: v` — after the connection closed and the string opened another. `Pooling = True` handed
the same connection back and the view was still there. A pooled connection would therefore carry both
the views and the authorizer of whichever handle it last served.

## What this changes in the plan

The plan's [record sandbox](../plans/storage-model.md#the-record-sandbox) section now says:

1. **A common table expression is allowed.** `READ` with a null database is a CTE read. Without the
   rule every `WITH RECURSIVE` statement is refused, and the owner's answer put `WITH` in scope.
2. **The function name is the callback's second argument**, and an unresolvable function is refused by
   SQLite before the allow-list is consulted.
3. **The allowance names every generated trigger**, the cap trigger included, not only the three write
   triggers.
4. **`trusted_schema = OFF` guards `main`, not the temp views**, and FTS5 works with it off.
5. **The statement and transaction clocks arm `sqlite3_interrupt`**; `CommandTimeout` does not bound a
   statement.
6. **The one-statement rule is detected by preparing the tail**, not by scanning the text and not by
   the authorizer. `sqlite3_prepare_v2`'s `out tail` is exposed through `SQLitePCLRaw`, so the store
   prepares twice rather than counting semicolons.

## What this rules out

- **A tokenizer or keyword scan.** A quoted, bracketed or backticked `load_extension` is refused by the
  authorizer's function list; `pragma_table_info` is refused as a read, not as a function; and a
  second statement is invisible to both. Only SQLite's parse sees all three.
- **`CommandTimeout` as the statement clock.**
- **A pooled connection for the record store.**
- **Trusting a cap in page code.** `RAISE(ABORT)` inside the statement refuses and leaves the rows
  before it, which is the atomicity the plan asks for.
- **`trusted_schema` as the fence over the views.** The authorizer's function list is.

## How to repeat it

The console is not committed. To repeat it, create a scratch `net11.0` console with
`Microsoft.Data.Sqlite` 11.0.0, open a file database with `Pooling=False`, set the pragmas above,
create a real table and a temp view and trigger per the shape above, install an authorizer that logs
`(action, p0, p1, dbName, inner_most_trigger_or_view)` and allows only the plan's list, and run the
statements in the tables. The expected answers are the ones quoted above; the version matters, because
the authorizer's argument shape and `trusted_schema`'s reach are SQLite's, not ours.
