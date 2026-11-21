<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# One storage model: two subjects, four shapes

| | |
| --- | --- |
| Status | Proposed 2026-11-21 — waits for the owner. Nothing below is built |
| Size | Large as a programme, small as a step: a spike, five slices and the release that closes the window, each its own branch and its own stop. The values slice is the floor; the record slice is the risk |
| First customer | The two plans this supersedes: the assistant's transcript (a Record for the signed-in visitor) and the redirect flash (a `NextRead` value). Editor mode is the first `Browser` value, and `/help`'s counter is the first Page value |
| Spike | Slice 0: the sandbox console, written to `docs/spikes/store-sandbox.md` before anything is built. The item names it as the spike this plan needs |
| Decisions | None recorded yet. Taken at each merge; this plan states the eleven names it intends, and the eight entries it edits |
| Backlog | [`storage-is-not-one-model`](../backlog/storage-is-not-one-model.md) is this job. It absorbs [`no-account-store`](../backlog/no-account-store.md) and [`message-does-not-survive-a-redirect`](../backlog/message-does-not-survive-a-redirect.md), whose plans [account-store](account-store.md) and [page-inbox](page-inbox.md) are superseded in shape |

## The short version

The owner answered the six questions on the item on 2026-11-21. The model is settled: **the subject is
the binding, the shape is the interface**. Two subjects — `Page`, shared by every visitor, and
`Visitor`, this person — carry four shapes: values, records, messages and objects. A lifetime is a
property of the entry, not of the store. Reads are open across page keys, writes are bound to the
caller's key, and `For(key)` is the one deliberate escape, for a library's or a worker's own
namespace.

This plan does four things the item left to it:

- **It designs the record sandbox** the owner's first answer calls the cost centre: one unpooled
  connection per subject handle, a `TEMP VIEW` per declared table filtered by the subject, joins and
  aggregates over those views, a keyword and identifier allow-list, SQLite's own limits, the statement
  and transaction clocks, and the result caps.
- **It slices the work** so each shape ships alone: the values and identity floor first, then records,
  then messages, then objects, then the ends. Every slice is one branch and one stop.
- **It settles what the item left open in the implementation**: the archive keeps its two tables and
  skips ephemeral values; the old two capabilities stay one release as obsolete adapters over the new
  stores and are then removed; the seeded layout does not take a flash on every render; objects are
  bytes in SQLite with a fixed content-type allow-list, because a page cannot be handed a stream.
- **It names what replaces what**: `IPageStorage` and `IPageSession` become `ctx.Stores.Page.Values`
  and `ctx.Stores.Visitor.Values`; `PageState` and `PageSessionValues` become one `StoreValues` table;
  `Pages:Storage` and `Pages:Session` become one `Pages:Stores`; the account store and the inbox
  become `Visitor.Records` and `Visitor.Messages`.

The model is not a sixth store beside the patchwork. It is the patchwork replaced.

## What exists today

A page reaches a patchwork of stores, and each answers "whose data is this, and how long does it
live?" differently:

| Store | Whose | Lifetime | Cap unit |
| --- | --- | --- | --- |
| `IPageStorage` (`PageState`) | the page, shared by every visitor | the page's | per page |
| `IPageSession` (`PageSessions`, `PageSessionValues`) | one browser | 30 minutes idle, 8 hours signed in, cleared on sign-out | per visitor |
| `IPageCatalog` / `IPageWrites` | the site's source | forever, until a scrub | per store |
| a job's journal and channel | one run, in memory | the run's | per job |
| — | — | — | — |
| planned `IPageAccountStorage` | one account | the account's | per (account, page) |
| planned `IPageInbox` | one account or session | 30 days from inbox activity, or the session | per inbox |

The gaps fall between the parts, exactly as the item lists them: no queryable store a page, a browser
or the site owns; no home for bytes; no atomic change; page values unreadable across pages while
session values are readable, with no reason for the asymmetry; and no rule that says whether an
anonymous visitor's data should survive sign-in.

The rest of the current shape is known and load-bearing: a capability is either registered or absent
(`pages-capabilities-are-one-seam`); every value crosses as JSON with a `PageValue<T>` that tells
absent from stored-null; a write on `HEAD`, an abandoned render or a passed deadline is a page bug and
throws; and the store's caps are enforced in the same statement as the write, so a concurrent writer
either fits or is refused (`pages-page-storage`, `pages-the-session`).

## The model

Two subjects and four shapes. `Site` is a reserved column, not a subject, until a use case needs it.

| | Page | Visitor |
| --- | --- | --- |
| Value (JSON, ordinal key, last-write-wins) | yes | yes |
| Record (declared table, SQL) | yes | yes |
| Message (durable, consumed once) | — | yes |
| Object (bytes, content-addressed) | yes | yes |

- **Page** is shared by every visitor and is the page's own data. Its subject key is the canonical page
  key; its default lifetime is the page's own, so an edit or a restore keeps it and a move re-keys it.
  It is the only subject an archive carries.
- **Visitor** is this person. The subject key is the account when signed in, the session id while
  anonymous, and the browser id for a `Browser`-lifetime entry. On sign-in the anonymous session's
  `Person` and `NextRead` entries are re-keyed into the account in one transaction; `Session` entries
  go with the rotation; `Browser` entries are untouched.
- **Site** is reserved: a subject kind with no writer, no route and no configuration.

A lifetime belongs to the entry, is answered by a read, and is selectable in a listing filter:

| Lifetime | Where | Dies when |
| --- | --- | --- |
| `Page` (Page default) | Page | the page goes — delete, scrub, or a move that leaves nothing |
| `Person` (Visitor default) | Visitor | the account goes; anonymous, the session, unless adoption moves it |
| `Session` | Visitor | the session rotates — sign-out and sign-in both — or the identity row is swept |
| `Browser` | Visitor | the browser's `b` cookie expires or is cleared |
| `NextRead` | both | the next successful take of that key, atomically |
| `Until(expiry)` | both | the clock says so; composes with any of the others |

Two rules hold the model together:

- **The subject is the binding.** A handle is bound to one subject key, and a write uses it. A page's
  handle for `/a` writes `page:/a`; a visitor's handle writes the reader's subject. There is no
  argument that names another subject.
- **The shape is the interface.** `Values`, `Records`, `Messages` and `Objects` are four interfaces,
  and a subject offers the shapes it has. `Visitor` has `Messages`; `Page` does not, and its absence is
  a type, not a runtime surprise.

Reads are open across page keys; writes are bound to the caller's key. `/a` reads `/_lib/b`'s Page
values with `Values.TryGetValueAsync("/_lib/b", key)`. That is **ownership, not protection**: page code
runs in-process with the site's identity (`pages-there-is-no-sandbox`), and the page store's source is
deliberately writable by any page the visitor can use (`pages-any-page-may-write-the-store`). Data is
the subject's, so only the subject's own render writes it; one writer per namespace is what gives a
page's delete, move, scrub and archive a single owner.

`ctx.Stores.For(key)` is the one escape. It accepts a key whose live head is a **library** or a
**worker** — never a page, never a reserved key — and returns a handle bound to that key with the
caller's reader. A library declares its tables and its data under its own key, and a page reaches the
data by calling the library's methods. A worker's key is its static namespace, shared by every run.

Authority lives in the verbs, not in the layer: every write refuses a `HEAD`, an abandoned render and
a passed deadline, on every subject and every shape. Content, layout and embedded layers all get
`ctx.Stores`; the role decides the **response** a layer owns, not what it can reach. The response half
of `pages-one-render-three-roles` and `pages-layouts-are-markup` stays; the capability half goes.

Caps are per subject instance, not per store: one person's values, records, messages and objects draw
on one person's budget, and `Pages:Stores` chooses the numbers together.

## The contract

The capability is one type with two subjects and one escape. The four shape interfaces are what a page
binds against; the subject interfaces are what a store answers with.

```csharp
public interface IPageStores {
    IPageSubject Page { get; }          // bound to ctx.Path
    IPageVisitorSubject Visitor { get; } // bound to the reader
    IPageSubject For(string key);       // a library or worker key, with the caller's reader
}

public interface IPageSubject {
    IValueStore Values { get; }
    IRecordStore Records { get; }
    IObjectStore Objects { get; }
}

public interface IPageVisitorSubject : IPageSubject {
    IMessageStore Messages { get; }
}
```

A page never sees a subject key, a session id, an account name or a browser id; the host resolves them
once per request, beside the session, and every layer of the chain shares the answer. `Page` is bound
to the layer's own key, so an embedded page writes its key and a layout writes its key.

### Values

```csharp
public interface IValueStore {
    Task<PageValue<T>> TryGetValueAsync<T>(string key);
    Task<PageValue<T>> TryGetValueAsync<T>(string pageKey, string key);
    Task<PageValue<T>> TryTakeAsync<T>(string key);
    Task SetAsync<T>(string key, T value, StoreLifetime? lifetime = null);
    Task<bool> DeleteAsync(string key);
    Task<IReadOnlyList<StoreEntry>> KeysAsync(StoreFilter? filter = null);
    Task<IReadOnlyList<StoreEntry>> AllKeysAsync(StoreFilter? filter = null);
}
```

- `TryGetValueAsync(key)` reads the bound page's key; `TryGetValueAsync(pageKey, key)` reads any page's
  key. The path is normalised before use, as `IPageSession` normalises it today.
- `TryTakeAsync(key)` is the subject-wide one-shot. On a `Visitor` subject it takes the first
  `NextRead` row for that key across the reader's pages, ordered by page key; on a `Page` subject it is
  the bound page's `NextRead` row. It is one `DELETE … RETURNING`, so nothing can take a value twice.
  `KeysAsync` and `AllKeysAsync` list without consuming.
- `SetAsync` writes under the bound page's key. A `null` lifetime takes the subject's default —
  `Page` for the Page subject, `Person` for the Visitor subject. A `Page` lifetime on a Visitor, or a
  `Person`/`Session`/`Browser` on a Page, is a page bug and throws. `Until(expiry)` composes with
  either default; `Person.WithExpiry(expiry)` narrows a named one.
- `PageValue<T>` gains `StoreLifetime Lifetime` and `string? Page`, both `init`-only, so the read says
  which rule the entry follows and which page wrote it. The positional `Found`/`Value` pair does not
  change.
- Keys are ordinal, case-sensitive, 1 to `Pages:Stores:MaxKeyChars` characters, no control characters,
  the rule `PageStateKey` enforces today.

```csharp
public readonly record struct StoreLifetime {
    public static StoreLifetime Default { get; }   // the subject's own
    public static StoreLifetime Page { get; }
    public static StoreLifetime Person { get; }
    public static StoreLifetime Session { get; }
    public static StoreLifetime Browser { get; }
    public static StoreLifetime NextRead { get; }
    public static StoreLifetime Until(DateTimeOffset expiry);
    public StoreLifetime WithExpiry(DateTimeOffset expiry);
    public StoreLifetimeKind Kind { get; }
    public DateTimeOffset? ExpiresAt { get; }
}
```

`StoreFilter` is a small record: a lifetime to select and a limit bounded by the configuration.
`StoreEntry` is `(Page, Key, Lifetime, ExpiresAt)`. `StoreLifetimeKind` is `Default`, `Page`,
`Person`, `Session`, `Browser` or `NextRead`; `Default` is the subject's own and is what a `null`
lifetime on `SetAsync` means.

### Records

```csharp
public interface IRecordStore {
    Task<IReadOnlyList<PageRow>> QueryAsync(string sql, PageParameters? parameters = null);
    Task<PageRecordResult> ExecuteAsync(string sql, PageParameters? parameters = null);
    Task<IReadOnlyList<PageRow>> SearchAsync(string table, string term, int? limit = null);
    IPageRecordTransaction BeginTransaction();
}

public interface IPageRecordTransaction : IAsyncDisposable {
    Task CommitAsync();
    Task RollbackAsync();
}
```

The statements live on the store and the transaction is only the frame, exactly as
`IPageWrites` carries the writes and `IPageTransaction` carries one commit (`pages-writes-are-transactions`):
a second spelling of `QueryAsync` and `ExecuteAsync` on the transaction would be the copy that entry
exists to prevent, and would leave a page choosing which of two identical methods to call.

`IRecordStore` knows whether a transaction is open, and the answer changes what a write means rather
than which method is called:

- **No transaction open** — the statement is a transaction of one, in SQLite's own autocommit mode, and
  is durable when the call answers. `PageRecordResult.Outcome` is `Applied`.
- **A transaction open** — the statement runs on the same connection inside it, so a later `QueryAsync`
  reads what the statement wrote, and the write is durable only at `CommitAsync`. `Outcome` is
  `Staged`. A page that must not risk an uncommitted write asserts on the outcome; that is the only
  signal it needs, because the alternative — a page silently forgetting to open a transaction — is what
  the value is for.

`BeginTransaction` is the atomic case the model exists to give a page: a counter is
`UPDATE "counters" SET "n" = "n" + 1 WHERE "Id" = @id`, and two renders cannot interleave it once they
share a transaction. The transaction is `BEGIN` (deferred), so a page that only reads never takes the
writer; the first write takes it, and a write that cannot take it within `Pages:Stores:BusyTimeoutSeconds`
answers `Busy`.

`CommitAsync` commits and ends the transaction — it does not begin a second one, because the store is
the write surface and a page that wants another transaction asks for one. `RollbackAsync` discards
everything written since the `BEGIN`, and so does disposing the handle without committing, which is why
it is `IAsyncDisposable` like `IPageTransaction`. One handle has one open transaction at a time, and a
`BeginTransaction` while one is open is a page bug and throws rather than opening a second connection
or a savepoint; so is a commit or a rollback of one that has already ended.
`Pages:Stores:Records:TransactionMaxSeconds` and the render's deadline bound it.

A refused statement writes nothing and leaves the transaction open, so a page decides what to do with
the work it already staged: commit it, or roll it back. That is deliberately not the page store's
all-or-nothing rule — there, statements are staged and run only at the commit, so a refusal can undo
them all at no cost; here the statements have already run, and only the page knows whether the rows
before the refusal are worth keeping or are half a form. The refusal is the value the call answers,
and the page is the one that acts on it.

`PageRecordResult` is `(Affected, Outcome)` and `PageRecordOutcome` is `Applied`, `Staged`, `Busy`
(another writer held the store, so nothing was written) or `Full` (the subject is at its row or byte
cap). The last two are values because they depend on data the page did not choose; `Busy` is the same
value `PageWriteOutcome.Busy` is.

Everything the page itself got wrong throws, which is the site's rule for storage misuse
(`pages-outbound-fetch`): a verb outside the allow-list, an undeclared table, a write to a row the
reader does not own, a `HEAD`, an abandoned render, a disposed handle. A statement over
`MaxResultRows` or `MaxResultChars`, and one past `StatementMaxSeconds`, throws too, because the page
wrote the statement and can bound it with `LIMIT`; the message names the setting and the cap, as an
oversized value does today.

Values are not part of it. The record transaction holds one connection to one subject; a value write
through `IValueStore` is still its own statement and does not join it, because a value is JSON in a
different table and mixing the two would put a page's counter and its preferences behind one clock.

`PageRow` keeps SQLite's storage classes out of page code — `GetString`, `GetInt64`, `GetDouble`,
`GetBoolean`, `GetDateTimeOffset`, `IsNull`, and a column the statement did not produce is a page bug.
`PageParameters` is a small `Add(name, value)` builder, so a `Dictionary<string, object?>` never has to
appear in page source.

`SearchAsync` is a call rather than SQL because a `TEMP VIEW` cannot carry `MATCH` and the FTS5 index
holds every subject's rows — an index a page could query directly would leak the existence of another
subject's rows. It reads the one column a table declared as `Search`, filters by the subject the way
the views do, and answers rows of that table, best match first, bounded by `MaxResultRows`. The
tokenizer is the one the assistant's memory already uses: an invariant in code, because a page that
could weaken it would weaken every search on the site.

The declaration is part of the revision, in a `<%! %>` type:

```csharp
<%!
[PageTable("notes", Subject = StoreSubject.Visitor, Search = "body")]
public sealed class Note {
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset Written { get; set; }
}
%>
```

`PageTableAttribute` lives in `Sdk` with `Name`, `Subject` (`StoreSubject.Page` or
`StoreSubject.Visitor`), `Lifetime` (`Default`, `Person`, `Session` or `Browser`; a page table's
lifetime is `Page`) and `Search`. `StoreSubject` is the declaration's two values; `Site` is reserved
and not in the attribute. The columns are the properties, and the supported types are `string`,
`string?`, `long`, `double`, `bool` and `DateTimeOffset` (TEXT, round-trip format). Anything else is
`PAGE011`. The lifetime enum accepts `NextRead` because it is one enum, but a table may not use it:
`PAGE012`. A table or column count over its cap, a live column whose type disagrees with the
declaration, and a name another source already holds are `PAGE012` at the declaration too.

The compiler extracts the declaration from the syntax trees it already holds, so it is part of the
revision, judged on save and needs no second parse. A `PageSchema` value in `Core` travels on
`CompileResult` beside `Kind` and `Libraries`. A save never refused for correctness is the rule the
site already follows for a template that does not compile (`pages-what-a-diagnostic-is`,
`pages-a-warning-never-stops-a-page`): a declaration the host cannot apply is a diagnostic stored with
the revision, it applies **nothing**, and the page answers "Handler failed to compile." until it is
fixed. A revision that does not compile applies no DDL at all — not the additions and not the drops.

**The declaration is the table.** A save creates a table it has not made, adds a column it has not
made, and drops a column or a table the source no longer names, with every visitor's rows in it. That
is the rule the owner chose; the editor's diff is the warning, the delete page says what a delete
takes, and the host never drops anything on its own initiative. A `DeclaredTables` catalogue holds what
was last applied — the source key, the table name, the subject, the lifetime, the columns, the search
column and a random 16-byte real table name — so the save knows what to add and what is gone, a move
re-keys the catalogue without renaming a table, and a scrub can find what an older build left behind.

### The record sandbox

The sandbox is a fence for one statement, not a claim of safety; `pages-there-is-no-sandbox` still
holds and the item does not change it.

- **One unpooled connection per handle** (`Pooling = False`, so `temp` is unambiguous), opened when the
  handle is used and closed when the render, the job or the transaction ends.
- **The owner filter is structural.** Each declared table exists once in `main` under a random,
  unguessable name. When a transaction begins, the host records the subject in `temp.StoreSubject` and
  creates in `temp` one view per declared table the subject has, under the name the page wrote:

  ```sql
  CREATE TEMP VIEW "notes" AS
  SELECT "Id", "Title", "Body", "Written" FROM "Declared_…"
  WHERE "Owner" = (SELECT "Subject" FROM temp.StoreSubject);
  ```

  with an `INSTEAD OF` trigger for `INSERT`, `UPDATE` and `DELETE` writing the owner back. A page table
  has no `Owner` column and its view has no predicate: the declaring key is the subject. A page's SQL
  runs against the views, so a join or an aggregate sees only its own subject's rows and the real table
  cannot be named.
- **SQL is allowed, bounded.** `SELECT`, `WITH`, `INSERT`, `UPDATE` and `DELETE` are the verbs; joins,
  sub-queries, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT` and aggregates are in scope, because the owner
  answered that they are. `ATTACH`, `DETACH`, `PRAGMA`, `CREATE`, `DROP`, `ALTER`, `VACUUM`, `REINDEX`,
  `ANALYZE`, `BEGIN`, `COMMIT`, `ROLLBACK`, `SAVEPOINT`, `RELEASE`, `LOAD_EXTENSION` and `EXPLAIN` are
  refused before anything runs; a second statement is refused; a token that names `sqlite_master`,
  `sqlite_schema`, `sqlite_temp_master` or a `pragma_` function is refused after quotes and brackets
  are stripped. The scan is a fence: a determined author can still do what page code can do, which is
  `pages-there-is-no-sandbox`.
- **SQLite's own limits, set once on the connection**: `SQLITE_DBCONFIG_DEFENSIVE`,
  `trusted_schema = OFF`, `SQLITE_LIMIT_ATTACHED = 0`, loadable extensions off, and
  `SQLITE_LIMIT_LENGTH`/`SQLITE_LIMIT_SQL_LENGTH` at the configuration's noise level.
- **The result caps refuse rather than truncate.** `MaxResultRows` and `MaxResultChars` are read into a
  result and a row past the cap throws, naming the setting and the cap, as
  `Pages:Read:MaxSourceCharsPerRender` refuses rather than truncating; `StatementMaxSeconds` interrupts
  the statement and throws the same way, and `TransactionMaxSeconds` closes a transaction that is
  merely slow.
- **The caps are enforced inside the statement that writes**, the way `SqlitePageStateStore` does
  today: the trigger checks the subject's row and byte totals before it inserts and answers
  `RAISE(ABORT, …)` naming the cap. A concurrent writer therefore either fits or is refused, and the
  refusal is `Full` on the result rather than an exception.
- **A write the store cannot take answers `Busy`**, as `PageWriteOutcome.Busy` does, after
  `Pages:Stores:BusyTimeoutSeconds` rather than being retried on the page's behalf.

### Messages

```csharp
public interface IMessageStore {
    Task<PageMessagePostResult> PostAsync(string toPageKey, string body, StoreLifetime? lifetime = null);
    Task<PageMessagePostResult> PostAsync(PageTopic topic, string body, StoreLifetime? lifetime = null);
    Task<PageMessageRead> TakeAsync();
    Task<PageMessageRead> TakeAsync(PageTopic topic);
    Task<PageMessageRead> PeekAsync();
    Task<PageMessageRead> PeekAsync(PageTopic topic);
}
```

A message is durable, one row, addressed to a page or a topic, and taken once. `PageTopic.Named(name)`
names a queue with no page behind it; a malformed name throws when the page constructs it, before a
store is touched. There is no `Flash` topic: the item's model makes the flash a `NextRead` value.

`PageMessageRead` is `(Refusal, Message)` and both calls answer it: `Take` removes the row, `Peek`
leaves it. One value rather than two, because the only difference between the calls is whether the row
is still there afterwards, and the caller already knows which one it called.

- The post checks a page address before it writes: the path is normalised, a path that cannot name a
  page is `NotAddressable`, a reserved key is `OwnedBySite`, no live source is `UnknownPage`, and a
  library or a content-only page is `NotASink`. A topic address skips the check because the name is the
  whole address.
- The take is one `DELETE … WHERE "Id" = (SELECT … ORDER BY "Id" LIMIT 1) RETURNING …` filtered by the
  reader's subject and the address, so two concurrent takes answer one with a message and one `Empty`.
- The peek is the same statement with `SELECT` instead of `DELETE`, so it answers the one row a take
  would answer right now, deletes nothing, and needs no transaction because it changes nothing. It is a
  **preview, not a claim**: another render may take that row between the peek and the take, so a page
  acts on the message `Take` returns and uses `Peek` only to decide whether to act at all. The pattern
  it makes possible is the one the take could not: look at a job's result, do the work that depends on
  it, and take only once the work succeeded, so a failed render leaves the message for the retry.
- A peek is a read: a `HEAD` may peek and a layout may peek, where both are refused a post and a take.
  It does not re-arm a job's silence clock, because a worker looking is not a worker working.
- There is no `Count`. A page that needs a number takes, or peeks and says that something is waiting;
  the reasons are under [considered and rejected](#considered-and-rejected).
- A message belongs to the reader's subject, and the reader is always its owner, so the value a page
  sees carries `From`, `Body` and `SentAt` and no owner. `From` is stamped by the host from the layer's
  key, the library's key or the worker's key, never accepted from the page.
- The lifetime is the subject's default `Person`, or `Session`, `Browser`, or any of those with an
  expiry (`Until`); adoption re-keys a `Person` message, the rotation deletes a `Session` one, and a
  browser message follows the `b` cookie. A peek does not extend it. Expired rows are swept with the
  values.
- Caps: one body, the messages one subject holds and the bytes they total, all per subject.
- Deleting a page deletes the messages addressed to it in the tombstone's transaction; a topic is not a
  page and is untouched.

### Objects

```csharp
public interface IObjectStore {
    Task<PageObjectRef> PutAsync(byte[] content, string contentType, StoreLifetime? lifetime = null);
    Task<PageObject?> OpenAsync(string sha);       // null when absent or not the reader's
    Task<bool> ExistsAsync(string sha);
}
```

- Content is addressed by lowercase hexadecimal SHA-256 and stored as a BLOB in `StoreObjects`. The same
  bytes put twice under one subject are one row: content addressing is the dedupe, and there is no
  second blob table to keep.
- `OpenAsync` answers the bytes, their length and content type, bounded by `MaxObjectBytes`. A page can
  copy, slice or re-wrap them with the BCL types the page API admits; an image re-encode needs a
  capability that does not exist yet, and the object store is what it would reach.
- `/o/{sha}` is a shell route because a page cannot stream bytes and because the cache policy is a
  response rule. It serves a page-subject object as `public, max-age=31536000, immutable`, and a
  visitor-subject object as `private, no-store`, with `ETag: "<sha>"`, `Accept-Ranges: bytes`,
  `Range`/`206`, `X-Content-Type-Options: nosniff` and `HEAD` support. A missing object, or one the
  reader does not own, is `404`, so the route cannot be used to probe another visitor's existence.
- The content type is a fixed allow-list in code, not configuration: `image/png`, `image/jpeg`,
  `image/gif`, `image/webp`, `text/plain; charset=utf-8` and `application/octet-stream`. SVG is
  refused: it is markup and a script vector, and the site already has a page kind for markup.
- Caps: one object and the bytes one subject holds, per subject.

### Jobs

A job gets three handles and no run store:

- `Page` bound to the page that started it — the caller's data.
- `Visitor` bound to the starter — the person's data. A job has no session and no browser, so
  `Session` and `Browser` lifetimes are refused; `Person` is the only one.
- `For(workerKey)` — the worker's own data, shared by every run of that worker.

`IWorkerContext` stays in memory and the journal and channel stay as they are: a run is a transport,
not a subject (`pages-a-job-is-not-durable`). A successful message post or take re-arms the silence
clock, as `Publish` and `ReceiveAsync` do; a value or record write does not, because the job already
has `Publish` for progress.

## The store

One migration adds four tables and copies the two it replaces.

### `StoreValues`

| Column | Type | Notes |
| --- | --- | --- |
| `Subject` | `TEXT NOT NULL` | `page:<key>`, `account:<name>`, `session:<id>` or `browser:<id>`, `BINARY` |
| `PageKey` | `TEXT NOT NULL` | the page that wrote it; for a Page subject, the page's own key |
| `Key` | `TEXT NOT NULL` | ordinal, `BINARY` |
| `Lifetime` | `INTEGER NOT NULL` | `Page`, `Person`, `Session`, `Browser` or `NextRead` |
| `ExpiresAt` | `TEXT NULL` | fixed-width UTC ISO-8601; `NULL` for no expiry |
| `Value` | `TEXT NOT NULL` | JSON |
| `UpdatedAt` | `TEXT NOT NULL` | fixed-width UTC ISO-8601 |

Primary key `(Subject, PageKey, Key)`. One row per key, and a write replaces the rule the previous row
carried: a key has one value and one lifetime at a time. The read resolves the subject, not a lifetime:
a signed-in visitor searches `session:<id>`, then `browser:<id>`, then `account:<name>`, so a session
value shadows a browser value and a browser value shadows the person's. `KeysAsync` and `AllKeysAsync`
read the primary key in order and need no second index; an index on `ExpiresAt` backs the sweep.

A `Person` row under `session:<id>` is the anonymous person's, and dies with the session unless
adoption moves it. `NextRead` under `session:<id>` moves the same way, so a one-shot survives sign-in
unread; `Session` rows go with the rotation; `Browser` rows are never under a session subject.

### `StoreMessages`

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | the FIFO order |
| `Subject` | `TEXT NOT NULL` | the reader's subject |
| `ToPageKey` | `TEXT NULL` | exactly one of this and `Topic` |
| `Topic` | `TEXT NULL` | a named queue with no page |
| `FromPageKey` | `TEXT NOT NULL` | stamped by the host |
| `Body` | `TEXT NOT NULL` | the string the page posted |
| `Lifetime` | `INTEGER NOT NULL` | `Person`, `Session` or `Browser`; `ExpiresAt` carries an `Until` |
| `ExpiresAt` | `TEXT NULL` | fixed-width UTC ISO-8601 |
| `SentAt` | `TEXT NOT NULL` | fixed-width UTC ISO-8601 |

One `CHECK` says what a row is: exactly one of `ToPageKey` and `Topic` is set. The subject says whether
a row is an account's, a session's or a browser's, so no second pair of owner columns is needed. Three
indexes serve the two addresses and the sweep: `(Subject, ToPageKey, Id)`, `(Subject, Topic, Id)` and
`(ExpiresAt)`.

### `StoreObjects`

| Column | Type | Notes |
| --- | --- | --- |
| `Subject` | `TEXT NOT NULL` | the owner |
| `Sha` | `TEXT NOT NULL` | 64 lowercase hex characters, `BINARY` |
| `Length` | `INTEGER NOT NULL` | bytes |
| `ContentType` | `TEXT NOT NULL` | one of the fixed allow-list |
| `Lifetime` | `INTEGER NOT NULL` | `Page`, `Person`, `Session` or `Browser` |
| `ExpiresAt` | `TEXT NULL` | fixed-width UTC ISO-8601 |
| `Content` | `BLOB NOT NULL` | the bytes |
| `CreatedAt` | `TEXT NOT NULL` | fixed-width UTC ISO-8601 |

Primary key `(Subject, Sha)`, so the same bytes under one subject are one row, and a put replaces the
lifetime the previous row carried. An index on `(Sha)` backs the route's page-subject lookup.

### `DeclaredTables`

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | the stable identity |
| `SourceKey` | `TEXT NOT NULL` | the declaring source's canonical key, `BINARY` |
| `Name` | `TEXT NOT NULL` | the name the source wrote |
| `Subject` | `INTEGER NOT NULL` | `Page` or `Visitor` |
| `Lifetime` | `INTEGER NOT NULL` | `Page`, `Person`, `Session` or `Browser` |
| `Columns` | `TEXT NOT NULL` | canonical JSON of the declaration |
| `Search` | `TEXT NULL` | the FTS5 column, or `NULL` |
| `RealName` | `TEXT NOT NULL` | `Declared_<22 base64url characters>`, generated once |

Unique `(SourceKey, Name)`. A move updates `SourceKey` and leaves `RealName` alone; a save compares the
declaration with `Columns` and applies the difference; a delete drops the row and its table; a scrub
finds rows an older build left.

A declared table's real shape is `Id INTEGER PRIMARY KEY AUTOINCREMENT`, one column per declared
property, an `Owner TEXT NOT NULL` column for a Visitor table, and, when `Search` is set, an
external-content FTS5 index with its three triggers created and dropped with the table — the shape
`AssistantMemorySearch` already has by hand. The index is never a view: a page reaches it through
`SearchAsync`, which filters by the subject before it matches.

The declaration has no index, no unique constraint and no foreign key of its own. The primary key is
the row id, uniqueness is a statement the page writes (`INSERT … SELECT … WHERE NOT EXISTS`), and a
child row is deleted by the page's transaction rather than by a cascade — which is enough at the row
cap, and is the reason the cap is a policy value.

### What the migration copies

`PageState` becomes `StoreValues` rows with `Subject = page:<path>`, `PageKey = <path>`, `Lifetime =
Page`. `PageSessionValues` becomes `StoreValues` rows with `Subject = session:<id>`, `PageKey =
<path>`, `Lifetime = Session`. `PageSessions` stays: it is the identity, the cookie and the rotation,
not a store of values. The migration copies in one transaction and drops the two old tables; its
`Down` recreates them from the `Page` and `Session` rows it can see and drops the four new tables.
Round-tripping a `Person`, `Browser`, `NextRead` or `Until` row is not promised, because before this
change there was no such row.

### Lifecycle

- **Delete** deletes the Page subject's rows, the messages addressed to the page, the page's objects,
  and every declared table and row the page owns, in the tombstone's transaction, beside the state it
  already drops. Every visitor's rows in a Visitor table go with it, because live data is not history.
  Revisions stay.
- **Move** re-keys `page:<old>` to `page:<new>`, `PageKey` and the messages addressed to `<old>` in the
  same transaction, updates the catalogue's `SourceKey`, and compiles nothing. A tombstone at the new
  key keeps the history it retained; its state belonged to the dead page and is discarded.
- **Scrub** removes the revisions and any catalogue row and real table an older build left. A scrub is
  the only operation that removes history.
- **Sign-out** deletes the `PageSessions` row and, in the same transaction, the `session:<id>` subject's
  `Session` values, messages, objects and declared-table rows, and clears the `s` cookie. It leaves the
  account's `Person` rows and the browser's `Browser` rows alone. There is no foreign key from a subject
  string to `PageSessions`; the deletes are explicit, as `PageState`'s are, and one helper does them.
- **Sign-in** runs the adoption transaction below.
- **Sweep** deletes expired values, messages and objects, and a `browser:` row whose subject has not
  been touched within `LifetimeDays`. An expired or evicted `PageSessions` row takes its subject's
  `Session` rows through the same explicit helper. The existing five-minute `PageSessionCleanupService`
  gains the calls and logs one line when it removed anything; no new timer.
- **A stale cookie** — a row whose account does not match the request — is deleted with its subject's
  rows by the resolver, exactly as the row is today.
- **A removed account** deletes every row of `account:<name>`, every owner row of a Visitor declared
  table and the session rows that name it, from `users remove`, before the `Users` row goes.

### Adoption, in one transaction

When a visitor signs in, `LoginEndpoint` replaces `PageSessionCookie.RotateAsync`'s delete-and-mint
with one transaction:

1. Re-key `StoreValues` and `StoreMessages` from `session:<old>` to `account:<name>` where the lifetime
   is `Person` or, for a value, `NextRead`; re-key `StoreObjects` where the lifetime is `Person`.
2. Re-key every Visitor declared table whose lifetime is `Person` from `session:<old>` to
   `account:<name>`; the catalogue makes the list.
3. Delete what is left under `session:<old>` — the `Session`-lifetime rows in every table, and the
   `Session` declared tables' rows.
4. Delete the old `PageSessions` row, mint the account's new session id and insert its row, and write
   the `s` cookie.

The `b` cookie and every `browser:<id>` row are untouched, and the account's existing `Person` rows are
untouched, so signing in on the laptop does not disturb the desktop.

## The identity: session and browser

The `s` cookie and the `PageSessions` table are the identity the session store already has: 128 random
bits in base64url, minted on the first write, `HttpOnly`, and honoured only for the account the row
names. The signed-in row's idle becomes the authentication ticket's lifetime, so the identity lives as
long as the sign-in it belongs to; the anonymous row stays 30 minutes. A `Session` value dies at
rotation or when the row is swept, which is the same rule seen from the other side.

The `b` cookie is new. The host mints 128 random bits in base64url on the first `Browser` write, as
`HttpOnly`, `SameSite=Lax`, `Path=/`, `Secure` in production, and re-sets its expiry on each `Browser`
write. It is not the `s` id, is never linked to an account, and dies when the visitor clears cookies or
`Pages:Stores:Visitor:Browser:LifetimeDays` passes. It is written after the render, like `s`, so a
first `Browser` write after `ctx.Response.FlushAsync()` throws for the same reason a first `Session`
write does: the cookie can no longer be sent. `ShellCookies` gains `b` as a fourth owned name, so
`Request.Cookies` never hands it to a page. `/privacy` names it, its lifetime and what it holds; today
that is the editor-mode preference and nothing else.

The reader's subject is resolved once per request, beside `PageSessionRequest`:

- signed in: `account:<name>` for `Person` and `NextRead`, `session:<id>` for `Session`;
- anonymous: `session:<id>` for everything but `Browser`;
- `Browser`: `browser:<id>` whenever the `b` cookie is presented, signed in or not.

A read by key searches in that order and answers the lifetime it found. A job has no `s` and no `b`, so
its `Visitor` handle has only `Person`.

## Configuration

One section, `Pages:Stores`, read through `ConfigurationReader` into a `PageStoresOptions` record with
a `FromConfiguration` factory, and each value added by the slice that enforces it
(`code-policy-values-are-configuration`). A missing or out-of-range value stops the site at startup
with the key, the accepted range and the recommended value, and every section's problems are reported
together.

```json
"Pages": {
  "Stores": {
    "MaxKeyChars": 200,
    "CommandTimeoutSeconds": 5,
    "BusyTimeoutSeconds": 5,
    "Page": { "MaxValueBytes": 65536, "MaxKeys": 256, "MaxTotalBytes": 262144 },
    "Visitor": {
      "Anonymous": { "MaxValueBytes": 4096, "MaxKeys": 8, "MaxTotalBytes": 16384, "IdleMinutes": 30 },
      "Browser":   { "MaxValueBytes": 4096, "MaxKeys": 8, "MaxTotalBytes": 16384, "LifetimeDays": 365 },
      "SignedIn":  { "MaxValueBytes": 16384, "MaxKeys": 32, "MaxTotalBytes": 65536 },
      "MaxLiveSessions": 2048
    },
    "Records": {
      "MaxTablesPerSource": 8,
      "MaxColumnsPerTable": 24,
      "MaxRowsPerSubject": 10000,
      "MaxBytesPerSubject": 134217728,
      "MaxResultRows": 500,
      "MaxResultChars": 262144,
      "StatementMaxSeconds": 5,
      "TransactionMaxSeconds": 10
    },
    "Messages": { "MaxMessageBytes": 16384, "MaxMessagesPerSubject": 8, "MaxSubjectBytes": 65536 },
    "Objects": { "MaxObjectBytes": 8388608, "MaxSubjectBytes": 67108864 }
  }
}
```

The caps are the shipped proposal, not a budget: the byte figure for records is where a mistake meets a
refusal, and the slice that enforces a value is the slice that chooses it. Every value is required, and
a range is a safety net wide enough that only a mistake meets it.

`Pages:Storage` and `Pages:Session` are removed. A deployment that still sets either is refused at
startup with the "no longer read; remove the key" message the site already uses for a retired key
(`Pages:ReservedPaths`), because a key that no longer decides anything is worse than a missing one.
The same message covers `Pages:Session:SignedIn:IdleMinutes`, whose number is now
`Authentication:TicketLifetimeDays`.

The content-type allow-list, the SHA-256 algorithm, the SQLite limits, the schema of the four tables
and the `b` cookie's shape stay invariants in code. They are a wire format, a schema constraint or a
security floor, not a deployment choice.

## What this replaces

| Today | New |
| --- | --- |
| `IPageStorage` | `ctx.Stores.Page.Values` |
| `IPageSession` | `ctx.Stores.Visitor.Values`, with `Session`, `Browser` or `Person` |
| planned `IPageInbox` | `ctx.Stores.Visitor.Messages`; the flash is a `NextRead` value |
| planned `IPageAccountStorage` | `ctx.Stores.Visitor.Records` |
| `PageState`, `PageSessionValues` | one `StoreValues` table with lifetime columns |
| `Pages:Storage`, `Pages:Session` | one `Pages:Stores` record with a row per subject |
| `IPageCatalog` / `IPageWrites` | unchanged: the page/source store, not a data store |
| job journal / channel | unchanged: a run is a transport, not a subject |

`IPageStorage` and `IPageSession` stay for one release as obsolete adapters over the new handles, with a
deprecation diagnostic, then go. The adapter is how a stored page that has not been edited keeps
serving while `/help` and the prompt move to `ctx.Stores`; the removal is a follow-up item,
`old-storage-spelling`, recorded at the first merge and waiting for a release, exactly as
`old-response-spelling` waits. An alias forever would be the second surface the model exists to remove,
so it is a window, not a home.

The layout, `HEAD` and embedded exclusions on the stores, `IPageWrites` and `IPageJobs` go: the
capability is offered to every layer and every request, and the verb refuses a `HEAD`, an abandoned
render or a passed deadline. A layout that writes a page or starts a job now can; it does so once per
page it wraps, the author's choice, and the response half of `pages-one-render-three-roles` and
`pages-layouts-are-markup` still refuses it the status, the content type and the headers.

## What the plan decided where the item left it open

- **The archive keeps its two tables and skips ephemeral values.** The owner's answer 6: export only
  Page-subject values with the page's own lifetime, and skip `NextRead` and `Until`, because exporting
  a one-shot would make it fire at the destination. `PageState` needs no lifetime column. Records and
  objects are not exported; the archive carries Page values and nothing else, and "the Page subject"
  grows a shape when a later item needs it.
- **The old capabilities get one release as adapters.** The removal is the follow-up item above. The
  alternative — remove them in the values slice — is smaller but breaks every stored page that has not
  been edited, and the editor is the only place that would say so.
- **The seeded `/_layout` does not take a flash on every render.** A `NextRead` take is a `DELETE` even
  when the key is empty, and every HTML render would pay it. The `/help` page shows the take in a
  layout, and an owner who wants a site-wide flash adds four lines to `/_layout`. The flash capability
  is unchanged; only the default cost is.
- **Objects live in SQLite as BLOBs, not on the filesystem.** One database means one backup, one
  restore and one volume, which is the site's existing storage rule; the per-subject byte cap is the
  bound, and a filesystem store would need a second consistency story for delete, scrub and archive.
- **The `/o/{sha}` route is shell code and a reserved key.** A page cannot stream bytes, the cache
  policy is a response rule, and the route has to answer when the page pipeline is broken — the three
  reasons `pages-page-first` keeps a route in the shell.
- **The shell's own session values stay the shell's.** `PageSessionRequest`'s unused text methods go
  with `PageSessionValues`; the shell's identity and rotation stay on `PageSessions`, and the shell is
  not offered `IPageStores`.

## The names this plan intends

Taken at each merge — bold rather than in backticks, because `make decisions` fails on a cited name the
record does not have yet:

- **pages-the-subject-is-the-binding** — the handle is bound to one subject key and a write uses it;
  reads are open across page keys, writes are bound, and `For(key)` is the one escape, for a library or
  a worker. The binding is ownership, not protection
- **pages-a-lifetime-belongs-to-the-entry** — `Page`, `Person`, `Session`, `Browser`, `NextRead` and
  `Until`, returned by a read and selectable in a listing filter; a countdown is not a lifetime
- **pages-a-browser-is-a-lifetime** — a device preference survives sign-in and sign-out through a
  stable `HttpOnly` `b` cookie, is not a subject and is never linked to an account
- **pages-adoption-re-keys-at-sign-in** — the anonymous session's `Person` and `NextRead` entries
  re-key into the account in one transaction; `Session` entries go, `Browser` entries stay, and a
  one-shot survives unread
- **pages-one-stores-section** — one `Pages:Stores` record with a row per subject, the caps chosen
  together, and the two retired sections refused at startup
- **pages-a-record-is-declared-sql** — the declaration is part of the revision, extracted on save, and
  the declaration is the table: a save adds what is new and drops what the source no longer names, and
  a save is never refused for correctness
- **pages-a-record-is-sandboxed** — one statement over `TEMP VIEW`s, an owner filter the page cannot
  name, joins and aggregates, a keyword and identifier allow-list, SQLite's limits, the result caps,
  the statement and transaction clocks, and `SearchAsync` as the one call that reaches the FTS5 index
- **pages-a-library-owns-its-schema** — a library declares its tables and its data under its own key; a
  page calls the library's methods and never queries another source's table, and `For(libraryKey)` is
  how the methods reach it
- **pages-a-message-is-taken-once** — durable, FIFO, one atomic take, at most once; a peek is a preview
  that takes nothing; no count, wait, recall or activity clock
- **pages-an-object-is-content-addressed** — SHA-256 bytes, `/o/{sha}` with `ETag`, `Range` and a cache
  policy that follows the subject, ownership checked for a private subject, a fixed content-type
  allow-list
- **pages-a-job-gets-three-scopes** — `Page` for the starting page, `Visitor` for the starter, and
  `For(workerKey)` for the worker's own data; the run itself has no store and a job gets no `Session`
  or `Browser`

The merges also edit `pages-page-storage`, `pages-the-session`, `pages-layouts-are-markup`,
`pages-one-render-three-roles`, `pages-writes-are-transactions`, `pages-an-archive-is-a-database`,
`pages-a-job-is-not-durable` and `pages-page-first`, and add the citation of the object route to
`pages-a-versioned-url-promises-immutable`. `pages-writes-are-transactions` is the entry that already
says a transaction is a frame and the writes live on the capability, so the record store's shape is
that rule extended rather than a second one. `pages-there-is-no-sandbox` is unchanged and is what the
record sandbox says of itself.

## Slices

Each slice is one branch, one step through the gate and one stop for the owner. The programme merges
phase by phase, so a regression is bisectable and its branch goes when its last slice is in. Nothing
is built until the owner says yes to this plan.

0. **The sandbox spike** (`S`, docs only). A scratch console over the real SQLite build: an unpooled
   connection, a `TEMP VIEW` per table with an `INSTEAD OF` trigger, a two-view join and an aggregate,
   a `RAISE(ABORT)` at a cap, the result caps, `SQLITE_LIMIT_ATTACHED = 0`, `trusted_schema = OFF`,
   `SQLITE_DBCONFIG_DEFENSIVE`, a statement interrupt and a `sqlite_master` read attempt. The findings
   go in `docs/spikes/store-sandbox.md` and amend this plan's sandbox section if they
   disagree with it.
   *Proves:* a join and an aggregate see only the owner's rows; `ATTACH`, `PRAGMA`, a second statement
   and `sqlite_master` are refused before anything runs; the interrupt ends a long statement; the real
   table cannot be named from the view's SQL.
1. **Values, lifetimes and identity** (`L`). `IPageStores`, `IPageSubject`, `IPageVisitorSubject`,
   `IValueStore`, `StoreLifetime`, `StoreFilter`, `StoreEntry`, `PageValue`'s lifetime and page; the
   `StoreValues` migration that copies `PageState` and `PageSessionValues`; the `PageStoresScope` and
   its internal seam; `PageChain` and `PageJobRunner`; the `b` cookie and `ShellCookies`; adoption at
   sign-in and the signed-in identity's lifetime; delete, move, scrub, `users remove` and the sweep;
   the archive's export and import over `StoreValues` (the Page rows only); the sampler's payload;
   `Pages:Stores`'s value section and the retired keys; the obsolete adapters; `/help`,
   `/editor-mode`, `_lib/editor` and the prompt.
   *Proves:* a page writes a Page value and every visitor reads it; a visitor writes a `Session` value
   and another visitor does not; a `Person` value survives sign-in and is on the account; an anonymous
   `NextRead` survives adoption unread and is gone after one take; a `Browser` value survives sign-in
   and sign-out and another browser does not see it; a cross-page read works; a write to another page's
   key is impossible; a `HEAD` refuses every write; the caps refuse at the boundary; the migration
   copies both old tables and its `Down` restores them; an exported archive has no `NextRead` or
   `Until` value; `Pages:Storage` stops the host at startup.
2. **Records and the sandbox** (`M`). `PageTableAttribute`, `PageSchema` and extraction, `PAGE011` and
   `PAGE012`, `DeclaredTables`, the DDL lifecycle in the save's transaction, `IRecordStore`,
   `IPageRecordTransaction` as the frame, `PageRecordResult`, `PageRow`, `PageParameters`, `SearchAsync`
   and the FTS5 index, the `TEMP VIEW` and triggers, the allow-list, the limits, the result caps and
   the clocks; the library and worker declarations; `/help`.
   *Proves:* a save creates exactly the declared tables and catalogue rows; an added column alters and
   changes nothing else; a column or table the source no longer names is dropped with every row in it;
   a revision that does not compile applies no DDL; a declaration the host cannot apply stores the
   revision, reports `PAGE012` at the property and leaves the schema and rows untouched; a query
   returns only my subject's rows; a two-table join and an aggregate do; `ATTACH`, `PRAGMA`, `CREATE`
   and a second statement are refused; a result over its cap throws naming the setting; a write with no
   transaction open answers `Applied` and is durable, one inside a transaction answers `Staged` and is
   gone after a rollback or a dispose; a refused write inside a transaction leaves the earlier rows to
   the page's choice; a second `BeginTransaction` throws; a search answers my subject's rows and never
   another's; a library's table is reached only through `For(libraryKey)`; a worker's own table is
   shared by its runs.
3. **Messages** (`M`). `IMessageStore`, `PageTopic`, `PageMessageRead`, `StoreMessages`, the address
   check, the atomic take, the peek, the caps, the lifetimes and the sweep; the flash example and the
   peek-that-protects-a-retry in `/help`; `/privacy`.
   *Proves:* a page posts to another page and the other takes it; a second take is `Empty`; two
   concurrent takes answer one with a message and one `Empty`; FIFO; a peek answers the message a take
   would and the take still answers it; a peek twice answers the same message; a take after another
   render's take is `Empty`; a `HEAD` peeks but is refused a take; a layout peeks; a peek does not
   re-arm a job's clock; a reserved key, an unknown key, a library and a content-only page are values; a
   signed-in message is on the account and another account does not see it; an anonymous one is adopted
   at sign-in; a `Session` one dies at rotation; a topic is a separate queue; a page delete takes its
   messages.
4. **Objects** (`M`). `IObjectStore`, `StoreObjects`, `/o/{sha}` and its reserved key, the content-type
   allow-list, `ETag`, `Range`, the cache policy, ownership and the caps; `/help`, `/privacy`,
   `docs/shell.md`.
   *Proves:* bytes round-trip; the same bytes under one subject are one row; another subject cannot
   open them; a page-subject object is public and immutable, a visitor-subject one is private; `Range`
   answers `206`; `HEAD` answers headers and no body; a missing or foreign sha is `404`; a refused
   content type never stores; the caps refuse at the boundary.
5. **The ends** (`S`). `users remove` extended to every declared table; the archive's import and export
   tests for what it refuses to carry; the sampler and `/status`; `/privacy`'s last paragraph;
   `/help`'s index; `docs/storage.md`, `docs/pages.md`, `docs/architecture.md`, `docs/glossary.md`,
   `docs/shell.md`; the two superseded plans.
   *Proves:* removing an account leaves no row of it anywhere; an exported archive has only Page values
   and no one-shot; an import writes them back under the page's lifetime; the storage figure moves when
   any shape writes; the status page lists the totals and the declared tables; every doc and decision
   names what landed.
6. **The window closes** (a follow-up release, not a branch of this programme). `IPageStorage` and
   `IPageSession`, their adapters and their `PublicAPI.Shipped.txt` entries go; `old-storage-spelling`
   closes; a stored page that used them reports a compile error until it is edited, which is the price
   of the one surface.

Slices 2, 3 and 4 are independent once slice 1 is in, and may run in that order or in parallel in
separate worktrees. Slice 5 waits for them.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/` | `IPageStores`, `IPageSubject`, `IPageVisitorSubject`, `IValueStore`, `IRecordStore`, `IPageRecordTransaction`, `IMessageStore`, `IObjectStore`, `StoreLifetime`, `StoreLifetimeKind`, `StoreFilter`, `StoreEntry`, `StoreSubject`, `PageTableAttribute`, `PageRow`, `PageRecordResult`, `PageRecordOutcome`, `PageParameters`, `PageObject`, `PageObjectRef`, `PageTopic`, `PageMessage`, `PageMessageRead`, `PageMessagePostResult`, `PageMessageRefusal`, `PageValue`'s two new properties, the obsolete marks on the two old interfaces; `PublicAPI.Unshipped.txt` |
| `src/uk.osric.asp.Core/` | `PageSchema` and its validation, the SQL allow-list and identifier scan, the content-type allow-list, the SHA-256 rule |
| `src/uk.osric.asp.Runtime/` | Declaration extraction on the compile, `CompileResult.Schema`, `PAGE011` and `PAGE012` |
| `src/uk.osric.asp.Web/Pages/` | `PageStoresScope`, `PageStoresOptions`, `PageStoresCaps`, the internal store seams, `PageChain.CreateLayer`, `PageStorageOptions`'s retirement, `PageStorageScope` and `PageSessionScope` as adapters, `PageSessionResolver` (the reader's subject), `PageSessionRequest`, `PageSessionOptions`'s retirement, the sweep |
| `src/uk.osric.asp.Web/Storage/` | `StoreValues`, `StoreMessages`, `StoreObjects`, `DeclaredTables`, the migration, the DDL and view generation, the sampler |
| `src/uk.osric.asp.Web/Authentication/` | The `b` cookie, `ShellCookies`, `PageSessionCookie.RotateAsync`'s adoption transaction, `LoginEndpoint` |
| `src/uk.osric.asp.Web/Work/` | `PageJobRunner`'s three handles |
| `src/uk.osric.asp.Web/Admin/` | The archive reader and writer over `StoreValues`, `users remove` |
| `src/uk.osric.asp.Web/Shell/` | The `/o/{sha}` route and its reserved key |
| `src/uk.osric.asp.Web/Pages/Seeds/` | `help`, `privacy`, `editor-mode`, `_lib/editor`; `_layout` unchanged |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The stores section and `Version` to `page-author-v55` |
| `src/uk.osric.asp.Web/appsettings.json` | `Pages:Stores`; `Pages:Storage` and `Pages:Session` removed |
| `docs/` | `storage.md`, `pages.md`, `architecture.md`, `shell.md`, `glossary.md`, `decisions.md` at each merge, the plan and the backlog |

## Tests

- **`Core.Tests`** for the lifetime values and their composition, the filter, the SQL allow-list and
  identifier scan, the schema value and its validation, and the content-type rule: values, no database.
- **`Runtime.Tests`** for the declaration extraction and its diagnostics, beside the shape check: a
  page declaration, a library declaration, a worker declaration, an unsupported type, a `NextRead`
  table, a column over its cap.
- **`Web.Tests`** against a throwaway database: the value lifecycle (page, visitor, cross-page, write
  binding, every lifetime, the take, the listing filter, the caps, the sweep), adoption, the `b`
  cookie, the DDL lifecycle, the sandbox refusals, the join and aggregate, the record transaction (an
  autocommitted write is durable when it answers, a staged one is not until the commit, a rollback and
  a dispose discard it, a read sees the staged write, a refused write leaves the staged rows, a second
  `BeginTransaction` throws), the search answering only the reader's rows, the message lifecycle
  (post, take, peek, the peek-then-take race, the `HEAD` rule, the caps), the
  object lifecycle, the migration up/down/up,
  and the configuration's shipped, override and refusal cases.
- **`Web.IntegrationTests`** for the pipeline: a page that writes and reads every shape, a second
  visitor who sees none of the first's, a sign-in that adopts, a `HEAD` that refuses, a layout that
  writes, a job's three scopes, `/o/{sha}` with `ETag` and `Range`, an archive that skips a one-shot,
  and a `users remove` that leaves nothing.
- **Nothing in a browser test**: none of this is markup, and the editor-mode toggle is an integration
  test over the seeded pages.
- **`make check`** is the gate for every slice; **`make decisions`** and **`make backlog`** are what
  prove the names and the record at each merge.

## Docs

- **`/help` in the same change** for everything a page author gains — the two subjects, the four
  shapes, the lifetimes, `For`, the declaration, the SQL and its refusals, the message take and peek,
  the object
  route and its content types, the caps, the job's three scopes and the `Browser` preference
  (`product-manual-is-a-page`). Three of those are the ones a page with its own users gets wrong, so
  they are worked examples and not asides: **the row's owner is in the `WHERE`**, because the store
  bounds a namespace and not a row; **uniqueness is `INSERT … SELECT … WHERE NOT EXISTS`**, because a
  declaration has no unique constraint; and **a login is a `Browser` value**, because the page cannot
  set a cookie and the site's sign-in is not the page's.
- **`/privacy`** for the visitor store, the `b` cookie, adoption at sign-in, what sign-out clears, what
  a page delete and a scrub take, the object bytes, and what the archive carries.
- **`PageAuthorPrompt` and its `Version`**, or the assistant keeps reaching for `IPageStorage` and a
  shared row.
- **[pages](../pages.md)** counts one capability instead of two and gains its section;
  **[storage](../storage.md)** gains the four tables, the lifetimes, adoption, the DDL and catalogue,
  the object route and the archive rule; **[architecture](../architecture.md)** lists the new policy
  values and the invariants; **[shell](../shell.md)** gains `/o/{sha}` and the `b` cookie;
  **[the glossary](../glossary.md)** defines **store**, **subject**, **lifetime**, **value**,
  **record**, **message**, **object**, **browser** and **adoption**, and rewrites **session**.
- **[the record](../decisions.md)** at each merge: the eleven names above, the eight edited entries and
  the citation the object route adds.
- **The backlog** at the first merge: this item is already `planned` with this plan; the two plans it
  supersedes are archived with a note, and `no-account-store` and `message-does-not-survive-a-redirect`
  point at this plan with the slice that delivers them; `old-storage-spelling` is added for the window's
  close.

## Risks

- **A page can hold the site's one writer.** One database means one writer, so a page that opens a
  record transaction and streams stalls the editor, the session store and every save behind it.
  `StatementMaxSeconds`, `TransactionMaxSeconds`, the render's deadline and the deferred transaction are
  the bounds; every commit releases the writer.
- **The owner filter is one generated statement from a leak.** A view that forgets `Owner`, a trigger
  that writes a null, or a real name a page can guess all break the guarantee. The real names are
  random and stored, the generation is one function with one test, the identifier scan refuses
  `sqlite_master`, and slice 2's integration test asks for another subject's row.
- **A page edit can destroy a column of every visitor's data.** That is the rule the owner chose: the
  declaration is the table. The editor's diff is the warning, and the assistant's edit safety is the
  guard on the author most likely to tidy a declaration. A restore of an older revision is an ordinary
  save and drops what the older source does not name.
- **A page delete destroys every visitor's rows in its tables.** The delete page says what it takes
  beside what it keeps, and `/privacy` says the same thing to a reader who never opens the editor.
- **Adoption is a write on the sign-in path.** Re-keying a full anonymous session is bounded by its
  caps but still one transaction with a per-table statement. A sign-in that cannot take the writer
  answers after `BusyTimeoutSeconds`; the visitor is signed in and the session row is the fallback.
- **The `b` cookie is a new identifier.** It is not linked to the account and never leaves the site, but
  it is a device id a visitor can clear and a shared device can share. `/privacy` names it, its
  lifetime and its one use, and a visitor who clears it loses only a preference.
- **The old capabilities keep the old asymmetry for one release.** The adapters are a shim, the prompt
  and `/help` steer away, and the deprecation diagnostic is what finds the pages that still use them.
- **A `NextRead` take is a write even when empty.** The seeded layout does not do it; a page that opts
  in pays one small `DELETE` per render, which is the price of no check-then-take race.
- **A peek is not a reservation.** Two renders can peek one message and both act on it, so a page that
  must act once takes first and acts on what the take answered — the peek is for deciding *whether* to
  act, and `/help` says so. The job-result pattern it enables (peek, work, then take) is safe only
  because the work is idempotent or the page's own code serialises it.
- **Objects grow the database.** A per-subject byte cap bounds the growth but not the backup size; the
  sampler's figure is the operator's warning, and the cap is where a mistake meets a refusal.
- **A layout that writes runs once per page it wraps.** That is the owner's rule; the one-action rule
  and the response exclusions still bound it, and `/help` says so.
- **The retired keys stop the host at startup.** An operator must remove `Pages:Storage` and
  `Pages:Session`; the message names both and the recommended replacement.
- **The store bounds a namespace, not a row.** Every render of one page writes that page's tables, so
  a page with its own users — a forum is the example — must put the row's owner in the `WHERE` of every
  `UPDATE` and `DELETE` itself. The manual says so plainly; a page that forgets it lets one visitor
  change another's row, and nothing in the store will notice.
- **A declaration has no index, unique constraint or foreign key.** Uniqueness is
  `INSERT … SELECT … WHERE NOT EXISTS` and a parent's children are deleted by the page's transaction,
  which is correct at the row cap and a surprise for an author who expects a database to do it.
- **The archive loses a Page one-shot or an expiry.** The owner's answer 6; `/help` says a value that
  must travel is a Page value with the page's own lifetime.
- **A contract change recompiles every stored image.** The page contract moves, so every image
  recompiles on its next request; that is the ordinary cost of a change here and recoverable in the
  editor and from `/admin/seeds`.

## Considered and rejected

- **A sixth store beside the patchwork.** The item's premise, and the reason this is one model.
- **One table per subject rather than subject columns.** A table per account per page needs a catalogue
  and a migration per new account; the subject column and one owner-filtered view keep one schema.
- **localStorage for per-browser state.** NoScript visitors are first class; the server has to read it.
- **A `Site` subject now.** Reserved until many pages must write one shared cell, and then only with a
  governance rule.
- **Four verbs over one declared table, without joins or aggregates.** The owner's answer 1: joins and
  aggregates are in scope, over the subject's own tables.
- **A typed query API instead of SQL.** Safe, and no longer SQL — which is the one thing a page can
  already write, and the owner chose it.
- **The verbs on the transaction as well as the store.** Two spellings of one call is the copy
  `pages-writes-are-transactions` exists to prevent: `IPageWrites` carries the writes and
  `IPageTransaction` carries one commit, so a page never chooses between two identical methods. The
  store knowing whether a transaction is open is how one spelling keeps both meanings.
- **`MATCH` in the page's own SQL.** A `TEMP VIEW` cannot carry it, and the FTS5 index holds every
  subject's rows, so a page that could name the index would learn that another subject's row exists.
  `SearchAsync` is the one call that reaches the index, and it filters by the subject first.
- **Staging statements and running them at the commit**, as the page store stages writes. A page has to
  read what it just wrote, and the atomic counter is `UPDATE … RETURNING`, so the statement has to run
  when it is called; deferring it would make every `QueryAsync` inside a transaction answer nothing.
- **Savepoints for a nested transaction.** One handle has one transaction and a second `BeginTransaction`
  throws; savepoints would buy a partial rollback that no caller has asked for, and the host already
  refuses `SAVEPOINT` from page SQL.
- **A lifetime in the primary key.** One row per key, with the subject as the resolution rule, is
  simpler and leaves no ambiguous write: a key with two lifetimes would be two answers to "what is this
  value".
- **A 30-day inbox activity clock.** A lifetime is a rule the store can enforce without knowing what
  the page is doing; a message is `Person` or an explicit `Until`, and an activity clock was the fifth
  answer to the same question.
- **A `Flash` topic.** The item's model makes the flash a `NextRead` value, and a topic is only for a
  durable message.
- **A `Count`, and a `HasMessages` beside it.** A count is a single indexed `SELECT COUNT(*)`, and the
  wish it serves is real: a page could show "three waiting" rather than "something is waiting". What
  it costs is a number that is wrong by the time it is rendered — the same staleness `Peek` has — and
  a call every page would be tempted to make on every render, which is the per-request work the
  activity clock was rejected for. The cap is eight messages per subject, so a page that must know
  takes in a loop bounded by the render's deadline and has the messages as well as the number, and one
  that must show a badge without consuming peeks once and says "a message is waiting". `HasMessages` is
  the same question as `Count > 0` and would be a second way to ask it. If a caller ever needs the
  number itself — a conversation view with a backlog is the candidate — `Task<int> CountAsync()` and
  its topic twin are the shape, and adding them later costs no more than adding them now.
- **A wait on the inbox.** A page render is bounded and answers a visitor, so a wait would hold the
  request; a worker already has `ReceiveAsync` as its wait, and a page polls.
- **A filesystem object store.** It needs a second consistency story for delete, scrub, archive and
  backup; the BLOB is the site's existing storage rule.
- **A stream or `System.IO` for objects.** The page API denies `System.IO`, so bytes cross as `byte[]`
  and the route, not a page, does the streaming.
- **An `Until`-only lifetime.** It composes with the others; alone it would make every value a clock
  the store has to sweep and the page has to think about.
- **An alias forever.** One release is a window; a permanent second surface is what the model removes.

## A forum, as an exercise

The shapes were put to the test of an ordinary forum page: three tables — its own users with hashed
passwords, its topics and its comments — all `StoreSubject.Page`, because a forum's posts are shared by
every visitor and its user list is one list rather than one per browser. Nothing was built; this is
what the model says would happen, and where it pinches.

**It fits, and here is the shape:**

- Three of the eight tables a source may declare, and `long`, `string` and `DateTimeOffset` cover an
  id, a name, a salt, a hash, a title, a body and a timestamp.
- The login is the page's, not the site's. `ctx.Request.Form` reads the POST, `HMACSHA256` derives the
  hash with a per-row salt, `CryptographicOperations.FixedTimeEquals` compares it, and *being logged
  in* is a `Browser` value — a device that stays logged in across signs-in, sign-outs and restarts. A
  `Session` value would log the reader out after thirty idle minutes, which is what that lifetime
  means; a `Person` value follows the site's identity — the account when signed in, the session
  otherwise — so the forum's login would belong to the site's sign-in or die with the session.
- Registering is one statement,
  `INSERT INTO "forum_user" (…) SELECT … WHERE NOT EXISTS (SELECT 1 FROM "forum_user" WHERE "Name" = @name)`,
  so two claims on one name cannot both land: it is a transaction of one statement and SQLite has one
  writer.
- Posting a comment and counting it on the topic is one record transaction — the insert beside
  `UPDATE "forum_topic" SET "Replies" = "Replies" + 1 WHERE "Id" = @topic` — and the counter is not
  racy.
- The topic list is a join with an aggregate over two of the page's own tables, which is the case the
  owner's first answer put in scope.
- Avatars are `Objects.PutAsync(bytes, "image/png")` and `<img src="/o/{sha}">`, deduped by content and
  served by the shell with an `ETag`; a "posted" confirmation is a `NextRead` value taken by the page
  the redirect lands on.

**Where it pinches:**

1. **Row authorisation is the page's.** The binding is one writer per namespace, not per row: every
   render of `/forum` writes the forum's tables, so the store cannot stop a visitor editing another
   visitor's comment. The page does it in the `WHERE` — `UPDATE "forum_comment" SET "Body" = @body
   WHERE "Id" = @id AND "Author" = @me` — and `/help` has to say so, because the model promises that
   nobody else can *reach* your namespace, not that nobody else can change your row.
2. **No declared index, unique constraint or foreign key.** Uniqueness is the `WHERE NOT EXISTS`
   above; a topic's comments are deleted by the page's transaction, child first. That is enough at the
   row cap and it is why the cap exists — but a page author must be told, or the forum's registration
   will be a check followed by an insert.
3. **A shared table's cap is the whole forum's.** Ten thousand rows across topics and comments is a
   small forum; the shard is another page — `/forum/2026-11` with the same declaration and its own
   namespace — and the cap being a policy value is what lets an operator raise it.
4. **A password KDF is not in the page API.** `System.Security.Cryptography` admits the SHA-2 hashes,
   the matching HMACs, `RandomNumberGenerator` and `CryptographicOperations`, and denies
   `Rfc2898DeriveBytes`, so a page that hashes passwords writes its own salted iteration. That is the
   first real gap the exercise found: either `/help` teaches the loop, or the policy admits PBKDF2 and
   moves its version, and the owner chooses when a page actually needs it.
5. **The archive would not carry it.** Page *values* are exported and Page *records* are not, so a
   forum's users and posts would not leave with an archive. This is the first caller that would want
   them, and it is the reason "the Page subject grows a shape" is a phrase rather than a promise.
6. **No rate limit for a page's own auth.** The site's login has one; a page's form login has the
   render budget, the caps and nothing else, so a page that must throttle keeps a `Browser` value with
   a timestamp and says so in `/help`.

The exercise changes one thing in the plan and it is not the forum: `Search` was declared with no way
to query it, so `IRecordStore.SearchAsync` is now the one call that reaches the FTS5 index.

## What this does not close

- **Assistant memory.** `AssistantMemories`, its versions, pins and FTS5 stay the assistant's
  (`assistant-memory-belongs-to-the-assistant`); `conversation-is-not-a-page` is the customer that
  writes into `Visitor.Records`.
- **Cross-account reads, sharing and roles.** One owner per row remains the rule.
- **A page reading another source's declared table.** A shared schema is the library's and is reached
  through its methods.
- **A `Site` subject.** Reserved.
- **N-read lifetimes.** A countdown is page state; a record makes the decrement atomic.
- **An image re-encode.** The object store holds the bytes and the route serves them; a transform
  capability is a later idea.
- **A password KDF in the page API.** `System.Security.Cryptography` still denies
  `Rfc2898DeriveBytes`, so a page that hashes passwords writes its own salted iteration; admitting
  PBKDF2 is a policy change the owner takes when a page needs it.
- **An object private to a page's own user model.** `/o/{sha}` checks the site's identity, so a page
  with its own logins can have public objects or per-device private ones, not per-forum-user ones.
- **A shared table in an archive.** Page values travel and Page records do not; the first caller that
  needs a shared table to leave the site is what makes the archive grow a shape.
- **Rate limiting for page code.** The site's own login has a limiter; a page's form login has the
  render budget and its caps, and a page that must throttle keeps its own clock in a value.
- **Migrating another site's records or objects.** The archive carries Page values and nothing else.
- **The shell's own flash and its identity.** `PageSessions` and the shell's rotation stay; the shell is
  not offered `IPageStores`.
- **A live message stream.** A worker's journal and channel are unchanged.

## The two waiting plans

[account-store](account-store.md) and [page-inbox](page-inbox.md) were written before the model and are
superseded in shape. Their answers that survive — the declaration is part of the revision, the owner
filter is structural, the take is one atomic statement, the caps are inside the statement that writes,
a save is never refused for correctness — are folded into the slices above. Their answers that do not —
a per-account namespace with no Page subject, a fixed 30-day inbox clock, a `flash` topic, an
eight-hour signed-in session — are replaced.

Neither plan should be approved. At this plan's merge, both are archived with a superseded note, and
[`no-account-store`](../backlog/no-account-store.md) and
[`message-does-not-survive-a-redirect`](../backlog/message-does-not-survive-a-redirect.md) point at
this plan with the slice that delivers them: records for the first, messages for the second.
