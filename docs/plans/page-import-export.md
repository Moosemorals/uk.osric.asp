<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Import and export: the page archive

[IDEA-009](../jobs/IDEA-009.md) has been open since page storage landed: pages cannot leave the site,
and a page that carries state — a guestbook, a counter — has no way back in. This plan settles the
archive format, the two views, and what an import is allowed to do to stored pages.

The archive is a **SQLite database**. The site already runs on SQLite, so the format needs no new
dependency, and a database of rows removes a whole class of archive problems: there are no entry
names, so there is no traversal, no duplicate-name trick and no `__MACOSX` noise to special-case.
The SQLite provider already underneath `Microsoft.Data.Sqlite` can serialize an in-memory database to
bytes and deserialize an uploaded one back, so neither direction needs a temporary file.

**Status:** planned.

## The short version

1. Export and import use one archive shape: a SQLite file with `Pages(Path, Source)` and
   `PageState(PagePath, Key, Value)` tables, identified by `PRAGMA application_id` and
   `PRAGMA user_version = 1`.
2. Both live under `/admin/import` and `/admin/export` behind the existing `Editor` policy. `/admin`
   goes back on the reserved list.
3. Export is a checkbox list of live pages and a `POST` that streams the `.sqlite` file back as an
   attachment.
4. Import is a two-step flow: upload and validate, then review and approve. Validation is all or
   nothing — one bad row rejects the whole archive. The review lists every page, tags each **new**,
   **overwrite** or **restore** (a tombstoned page), and ticks every box by default.
5. Import never invents a storage path. Each approved page becomes a revision through the same
   factory the editor uses, committed in one transaction, so an overwrite is an ordinary new revision
   and the old source stays in history.
6. Approved pages are compiled one at a time by a background worker, then written in one SQLite
   transaction. The approval answers immediately with a progress page that refreshes while the
   worker runs. A page that does not compile is a stored revision with diagnostics, not a reason to
   stop; a host crash or a failed write leaves **nothing** applied.
7. The archive carries source and state. It does **not** carry revision history, tombstones, users or
   configuration. History belongs to the receiving site.
8. Nothing touches disk: the export database is built in memory and serialized straight to the
   response, and an upload is deserialized into a throwaway in-memory connection.

## What travels

| Thing | In the archive? | Why |
| --- | --- | --- |
| Page source | Yes | The point of the feature. |
| `PageState` keys and values | Yes | [IDEA-009](../jobs/IDEA-009.md) requires it; a page archive that loses a guestbook is not a round trip. |
| Revision history | No | The import is a save, so the receiving site grows its own history. Shipping hashes and runtime ids across sites would import code nobody built. |
| Tombstones (deleted pages) | No | A deleted page is not part of the site. Import never deletes anything, and a tombstoned page comes back only when its key is in the archive and chosen. |
| Users, tickets, conversations, config | No | Different jobs, different threat models. |
| Shell assets (`/site.css`, `/editor.js`, …) | Yes, as ordinary pages | They *are* stored pages. A site may export its edited copies and import them elsewhere. |

Import is additive: pages in the archive are saved, pages absent from it are untouched, and nothing
is deleted.

## The archive

The export is a small SQLite database with two tables:

```sql
PRAGMA application_id = 0x756F6173;   -- "uoas"
PRAGMA user_version = 1;
PRAGMA encoding = 'UTF-8';

CREATE TABLE Pages (
    Path   TEXT NOT NULL PRIMARY KEY,
    Source TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE PageState (
    PagePath TEXT NOT NULL REFERENCES Pages (Path) ON DELETE CASCADE,
    Key      TEXT NOT NULL,
    Value    TEXT NOT NULL,
    PRIMARY KEY (PagePath, Key)
) WITHOUT ROWID;
```

`Pages.Path` is the canonical page key, root included (`/`), and `Pages.Source` is the template
text exactly as stored. `PageState` has the same shape as the site's own state table: `Key` is
1–200 characters with no control characters, and `Value` is the stored JSON text, so a stored `null`
is the four characters `null`. A page with no state simply has no rows.

`PRAGMA application_id` is the format identity and `PRAGMA user_version` the format version. Import
requires `0x756F6173` and version `1`; anything else is "not a page archive" or "written by another
version of the site", before a row is read. The schema is exactly the two tables and their columns;
an extra table, view, trigger or column is a validation failure, not something to ignore.

The archive is deliberately **not** the site's storage schema. `PageRevisions` carries images,
symbols, hashes and runtime ids that belong to the machine that built them; the archive is only what
a receiving site needs to build its own revisions.

### Why SQLite rather than a zip

- **No entry names.** Path traversal, backslashes, duplicate entries and metadata files are
  properties of an archive that has names. A row has a `Path` value, and the only validation is the
  validation a page already needs.
- **Already in the stack.** `Microsoft.Data.Sqlite` is the site's storage provider, and
  `SQLitePCL.raw` is already underneath it. It exposes `sqlite3_serialize` and `sqlite3_deserialize`,
  so an in-memory database becomes bytes and back without a temporary file or a second parser.
- **Typed rows.** Text is text, primary keys are unique, and a state value is a column rather than a
  JSON object with raw JSON embedded in it.
- **Versioned by the format itself.** `application_id` and `user_version` are the manifest, and a
  future version is a version number the reader refuses with a precise message.
- **No compression.** A zip needs a second, uncompressed size cap to survive a bomb; a SQLite file
  is its content, so the upload cap is the content cap.

What it gives up: the file is not readable by eye, though
`sqlite3 pages-export.sqlite '.dump'` is one command away, and it is a little larger than a
compressed archive. It also means an untrusted file reaches SQLite's parser. That is a real boundary,
and it is handled explicitly: the upload is deserialized **read-only** into an in-memory connection,
`query_only` and `trusted_schema = OFF` are set before any query, the two tables must be ordinary
tables, `quick_check` runs once, and every row is validated before it is staged.

### Row rules

Import treats the database as hostile input, and validation is **all or nothing**: every rule below
must hold for every row, and the first failure rejects the whole archive. Nothing is staged, skipped,
renamed, repaired or partially accepted. Problems are still collected — up to ten — so the error page
can list several at once, but collecting is reporting only; each one is independently fatal.

| Rule | Rejected example | Why |
| --- | --- | --- |
| The schema is exactly the two tables | an extra table, view, trigger or column | The writer emits one shape; anything else is tampering or a version this reader does not know. |
| `Pages` and `PageState` are ordinary tables | a view or virtual table with either name | A view computes rows, and a virtual table brings code the archive has no business running. |
| `Path` is already canonical | `/About`, `//about`, `/about/`, `/cafe\u0301` | It must equal `PagePath.Normalize(Path).Key`; import does not quietly rename a page. |
| `Path` is addressable | `/a?b` | Keys containing `?` or `#` are already refused by `PagePath.IsAddressable`. |
| `Path` is not reserved | `/admin` | `/admin` and every route prefix stays the shell's. Checked with `ReservedPaths`. |
| Paths are unique | two `/about` rows | The reader tracks the keys it has seen, so a crafted index cannot slip a duplicate past; `quick_check` catches general corruption. |
| Values are TEXT | an integer or blob in `Source` | SQLite columns have affinity, not a type; `typeof(...) = 'text'` is checked on every row. |
| Text is valid UTF-8 | a crafted non-UTF-8 value | The text columns are read as bytes and decoded strictly, so a replacement character cannot silently become page source. |
| State belongs to a page | `PageState('/ghost', …)` with no `Pages('/ghost')` | An orphan would be applied to nothing, or to a page the archive never named. |
| State keys and values pass the existing caps | a 10,000-key page | The same `Pages:Storage` limits the store already enforces. |
| State values are JSON | `visits` = `not json` | The page's own reads expect JSON; the store never writes anything else. |
| At least one page | an empty database | An export with no pages is a mistake, not a no-op. |

SQLite has no line-ending translation and no BOM handling: the `Source` text is the stored text,
line endings included. The editor normalises CR and CRLF the next time a person saves, as it already
does.

### Limits

All caps are configuration with safe defaults. They bound one hostile upload; they are not a policy
on the site's own pages.

| Setting | Default | Reason |
| --- | --- | --- |
| `Pages:Archive:MaxUploadBytes` | 16 MiB | The `.sqlite` file, enforced by Kestrel before the form is read and again while copying. There is no compression, so this also bounds the content. A site whose export is larger raises the cap; export itself has no cap. |
| `Pages:Archive:MaxPages` | 500 | Bounds the review list and the worker's work. |
| `Pages:Archive:MaxCompiledBytes` | 128 MiB | The compiled images held until the batch commits. Exceeding it aborts before anything is written; selecting fewer pages is the way through. |
| `Pages:Archive:StageTtlMinutes` | 30 | How long an unapproved review survives. |
| `Pages:Archive:MaxStagedImports` | 3 | At most three reviewed archives held in memory; the oldest is evicted. |

State is validated against the existing `Pages:Storage` caps, so an import cannot store more state
than a page is allowed to hold. Source and state sizes are summed in SQL with
`length(CAST(... AS BLOB))` before any string is read, so a crafted row cannot make the host allocate
a value it did not pay for in file bytes. The upload cap is deliberately generous relative to a
normal site; a large site's export can exceed it, and raising the setting is the answer, not relaxing
the validation.

`SQLitePCLRaw.core` gains a direct, pinned reference at 2.1.13 — the version
`Microsoft.EntityFrameworkCore.Sqlite` already resolves — because `sqlite3_serialize` and
`sqlite3_deserialize` are the one API used below the ADO.NET provider. It is not a new library in the
image; the pin only makes the existing dependency explicit.

## Routes and views

| Method | Route | Auth | Antiforgery | Purpose |
| --- | --- | --- | --- | --- |
| `GET` | `/admin/export` | Editor | — | List the live pages with checkboxes. |
| `POST` | `/admin/export` | Editor | Yes | Build the archive and write the `.sqlite` file. |
| `GET` | `/admin/import` | Editor | — | Upload form. |
| `POST` | `/admin/import` | Editor | Yes | Validate the upload, stage it, `303` to the review. |
| `GET` | `/admin/import/{id}` | Editor | — | Review the staged archive. |
| `POST` | `/admin/import/{id}` | Editor | Yes | Approve the selected pages, enqueue the import, `303` to progress. |
| `GET` | `/admin/import/{id}/progress` | Editor | — | Progress and results; refreshes while the worker runs. |

`{id}` is 32 hexadecimal characters from `RandomNumberGenerator`; the handler rejects anything else.

### Why `/admin` and not `/edit/import`

`/edit/{**path}` already owns everything under `/edit`, so `/edit/import` would be read as the
editor for a page called `import`. The editor's command query (`/edit/foo?cmd=history`) could carry
it, but a site-wide operation hidden in a per-page query string reads wrong, and the import needs
two more routes of its own. `/admin` is the honest prefix.

DEC-132 removed `/admin` from the reserved list when the seed-restore admin surface went away. This
plan puts the prefix back, which is a deliberate reversal: the shell now answers `/admin/*` again.
`ReservedPaths.Default` and `Pages:ReservedPaths` in `appsettings.json` both gain `/admin`; the
configured list wins, so changing only the default would do nothing in a running deployment.

### Links from `/edit`

The listing gains one navigation landmark above the page list:

```html
<nav aria-label="Page administration">
  <a href="/admin/import">Import pages</a>
  <a href="/admin/export">Export pages</a>
</nav>
```

built with `PageLayout.Nav("Page administration", …)` so its name is distinct from the editor's
`Editor navigation`. The two admin views link back to `/edit` and to each other.

## Export

`GET /admin/export` lists every live page that is addressable and not reserved — the same test the
editor uses to call a page reachable — sorted by key, each with a checkbox named `include`, checked
by default and valued with the page key. Deleted pages are not offered: their history is retained
but there is no current page to export, and a legacy page sitting on a reserved key could never be
imported back. A page with no state still gets a checkbox; it simply has no `PageState` rows.

The form is a plain list of `<li><input type="checkbox" id="…" name="include" value="…"><label …>`
entries, each label carrying the page key. A small script adds **All** and **None** buttons; without
scripts the checkboxes still work.

`POST /admin/export`:

1. Rejects a missing or wrong antiforgery token before reading the form (the framework does this).
2. Reads `include`. Each value must be a key in a fresh list of reachable live pages; a value that
   is not is a `400`, so a stale form cannot ask for a page that has since been scrubbed, reserved or
   deleted.
3. Answers `400` with a short message when nothing is selected.
4. Reads every selected source and state **before** writing the response, so a storage failure still
   becomes a readable error page rather than half a file.
5. Builds the archive database in memory and serializes it, then writes the body:

```csharp
byte[] archive = writer.Write(selected);   // in-memory SQLite, then sqlite3_serialize
context.Response.ContentType = "application/vnd.sqlite3";
context.Response.Headers.ContentDisposition =
    "attachment; filename=\"" + name + "\"";
context.Response.Headers.CacheControl = "no-store";
context.Response.ContentLength = archive.Length;
await context.Response.Body.WriteAsync(archive, context.RequestAborted);
```

with `name = $"pages-export-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}.sqlite"`. The timestamp is
ISO 8601 in UTC with the colons removed, because a colon is not legal in a Windows file name and
would be mangled by the browser. (The header that makes a browser save the body is
`Content-Disposition` with `attachment; filename=…`; there is no `Content-Filename` header.)

The writer opens `Data Source=:memory:`, sets the PRAGMAs, creates the two tables, inserts every row
in key order in one transaction, then calls
`raw.sqlite3_serialize(connection.Handle, "main", out long size, 0)`, copies the buffer and frees it
with `raw.sqlite3_free`. There is no temporary file and no zip: `Content-Length` is the serialized
size, so the browser gets an ordinary download. The rows are ordered, but the file layout itself is
SQLite's business and is not promised to be byte-stable between versions.

If the client disconnects mid-write the request aborts and the download is truncated; a SQLite reader
notices, and nothing was written to storage.

## Import

### 1. Upload and validate

`GET /admin/import` renders:

- a `fieldset`/`legend` describing the archive in one sentence;
- `<input type="file" name="archive" id="archive" accept=".sqlite,application/vnd.sqlite3,application/x-sqlite3" required>`,
  labelled in a `dt`/`dd` pair inside a `form class="form"`, exactly like the sign-in form;
- a submit button.

Progressive enhancement wraps the input in a drop zone. A small `/admin.js` prevents the browser's
default drop behaviour, takes `event.dataTransfer.files[0]`, assigns it to the input with a
`DataTransfer` and shows the chosen file name in the zone. The baseline form — click, choose, submit
— is the only thing required for the feature to work.

`POST /admin/import`:

1. Sets `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` to `MaxUploadBytes` plus a small
   allowance for form overhead, before reading anything, so Kestrel refuses rather than buffers.
   `FormOptions.MultipartBodyLengthLimit` and `MemoryBufferThreshold` are configured at startup from
   the same cap, so the framework's form parser keeps the upload in memory rather than spilling it to
   a temporary file.
2. `ReadFormAsync`; requires exactly one file part named `archive`.
3. Copies the upload into a bounded `byte[]` — counting bytes as it copies, not trusting
   `Content-Length` or the file's declared length.
4. Opens an in-memory connection, pins the bytes, and calls
   `raw.sqlite3_deserialize(handle, "main", pointer, length, length, SQLITE_DESERIALIZE_READONLY)`.
   The pinned buffer stays alive for the connection's lifetime and the handle is freed after it
   closes. The connection is then `query_only` and `trusted_schema = OFF` for the rest of its life.
5. Validates the whole archive. Any problem rejects it and nothing is staged; problems are collected,
   up to ten, only so the error page can show several at once.
6. Stages the result under a new id and answers `303` to `/admin/import/{id}`.

A rejected archive answers `400` (or `413` when it is over `MaxUploadBytes`) with a view that names
the problem and lists the reasons:

- **"That archive could not be imported"** as the `h1`;
- one line per problem, in the diagnostics list style already used by the editor;
- "Choose another file" back to `/admin/import`.

Nothing is written to storage at this point, and nothing is staged.

### 2. Review

`GET /admin/import/{id}` renders the staged archive:

```
Import 12 pages

☑ /about            overwrite   4.1 KiB, 3 state keys
☑ /blog/first-post   new         6.8 KiB
☑ /notes            restore     2.0 KiB
☐ /                        new  16.2 KiB

[Import 11 selected pages]   [Cancel]
```

- Every checkbox is ticked by default, named `include`, and valued with the canonical key.
- **new** means no head exists. **overwrite** means a live page will gain a revision. **restore**
  means the head is a tombstone: the save clears it and keeps the retained history, exactly as the
  editor's own save does.
- Each row shows the source size and, when present, the state key count. State is merged, so a page
  the target already holds near its `Pages:Storage` cap can make the selection unimportable; the
  progress page names it, nothing is written, and unticking it is the way through.
- The total counts and the button label follow the ticks; a tiny script keeps them in step. Without
  scripts the server still reads the ticks that were submitted.
- The review is a snapshot. If a page is edited between review and approval, the import wins; the
  site has one editor, and a conditional save would turn an ordinary race into a failed page.
- Cancel simply returns to `/admin/import`; the stage expires on its own.

### 3. Approve, queue, compile, commit

`POST /admin/import/{id}`:

1. Reads the ticked keys. Every value must be a key in the stage; a value that is not is a `400`
   with a link back to the review, and no keys is the same answer. The approval does not silently
   drop a value the review never offered.
2. Re-checks that each key is addressable and not reserved. This is defence in depth; the archive
   was already checked, and the stage cannot change.
3. Refuses with `409` and a link to the running progress page when this stage is already importing.
4. Enqueues one `ImportJob` and answers `303` to `/admin/import/{id}/progress`.

The worker has two phases, and the split is the whole design:

**Phase one — compile, write nothing.** For each selected page, in key order,
`PageImageBuilder.Build(key, source, "import", …)` runs exactly as an editor save would. Nothing
touches storage, so nothing can be half-applied. The results are held until the batch is ready:

- A page with compile errors or warnings gets its normal `PageImage` with diagnostics, or with no
  image, exactly as a save would store it. The import continues.
- A compiler fault (`InvalidOperationException`, which is our bug rather than the page's) keeps the
  source and stores a revision with no image, matching `PageEditor.SaveAsync`'s documented promise
  that an author's text is never lost. The import continues and the page is tagged "compiler fault".
- The compiled images are summed against `MaxCompiledBytes`; over the cap, the job aborts **before
  the transaction starts** and nothing is written. Ticking fewer pages is the remedy, which is what
  selective import is for.
- Any other exception fails the job before the transaction; nothing is written.
- A host crash or shutdown here loses the in-memory batch and leaves storage untouched.

**Phase two — commit, in one transaction.** Every selected page becomes a `PageTemplate` through the
same revision factory the editor uses (new revision id, timestamp and hash), then one
`IPageBatchStore.SaveBatchAsync` uses one `StorageDbContext`, begins one transaction, and:

1. inserts every `PageRevision`;
2. upserts every `PageHead` (the revision insert has to come first: the head's foreign key points at
   it);
3. upserts the archive's state keys for each page.

then commits once. A crash during the commit is rolled back by SQLite on the next start; a failed
write rolls back explicitly and is reported as a whole-import failure. There is no state in between:
either every selected page has its new revision and head, or none does. Readers see the old pages
until the commit and the whole new set after it, never half the archive. A revision-id collision —
the one failure `SavePageAsync` already retries — regenerates the batch's revision ids and retries
the whole transaction, so it is invisible to the import.

State **merges**: keys present in the archive overwrite the page's matching keys, and keys the page
already has that the archive does not mention are left alone. That is the safe reading of "an import
is a save" — the source is replaced, the page's other state is not silently destroyed. The merged
total is checked against `Pages:Storage` inside the transaction, before any row is written; if any
selected page would exceed a cap, the whole batch is refused and nothing changes. Unticking that page
is the way through. On a fresh site there is nothing to merge, so a round trip is exact.

The commit is short — inserts only, no compilation — so the SQLite write lock is held for
milliseconds, not minutes. That is why compilation happens first. Writing revisions inside a long
compile transaction would have been the alternative, and it would block every page's state writes for
the duration.

The queue is bounded by a small reservation counter, in the same shape as `RunQueue`. A fourth
pending import is refused with `409`; with one editor this is a backstop, not a feature. One job at a
time is still right: `CompileSlots` is 1, so a second import would only wait behind the first, and
serialising the compiles keeps the progress page honest.

### 4. Progress and results

`GET /admin/import/{id}/progress` shows:

- a summary while running (`Compiling 7 of 12…`, then `Saving 12 pages…`), refreshed by
  `<meta http-equiv="refresh" content="2">` and a "Refresh now" link for anyone who needs it;
- a `<ul>` of pages with state: waiting, compiled, compiled with warnings, compiled with errors,
  compiled with a compiler fault, or not compiled after a failure, each with a link to
  `/edit/{path}`;
- compile diagnostics for any page that did not compile cleanly, using `PageLayout.Diagnostics`;
- when finished, a short summary and links to `/edit`, the review (for a retry), `/admin/export` and
  `/admin/import`.

Because the commit is one transaction, "saved" is not a per-page state: the results say "committed"
or "not committed" for the batch, and a page that did not compile is still committed with its
diagnostics.

Refreshing stops when the job finishes. A stage that has expired answers `404` with a remedy link
("Upload the archive again"); nothing about the archive survives that long.

### What a failure means

The batch is all or nothing. The only failures it absorbs are compilation ones:

- **Any validation failure** rejects the archive before it is staged. No row is skipped, renamed or
  repaired.
- **A page that does not compile** is a normal outcome. It commits with its diagnostics (or with no
  image), exactly like an editor save, and the rest of the archive still commits.
- **A compiler fault** is absorbed the same way: the source is kept, with no image.
- **A page whose merged state would exceed a cap** refuses the whole batch before any row is written.
  The progress page names it, and unticking it is the way through.
- **A host crash during compilation** loses the in-memory work and leaves storage untouched.
- **A host crash during the commit** is rolled back by SQLite on restart; nothing is applied.
- **A failed write** (disk full, a constraint, a storage fault) rolls back the transaction and is
  reported as a whole-import failure; nothing is applied.

The review page says it in one sentence: "The selected pages are applied in one step. If that step
fails, none of them change."

There is one deliberate gap: an import is not a *rollback point*. Committing adds a revision per
page, and those revisions remain in history like any other save. There is no undo button; the way
back is to import an earlier export, which adds another revision.

## Components

| Where | New or changed | Responsibility |
| --- | --- | --- |
| `Web/Admin/PageArchiveFormat.cs` | new | The archive's identity and shape in one place: `ApplicationId` (`0x756F6173`), `Version` (`1`), the table names and the `CREATE TABLE` statements the writer runs and the reader checks. |
| `Web/Admin/ArchiveOptions.cs` | new | Every archive and import setting in one place: `FromConfiguration` over `Pages:Archive`, clamped like the other options. |
| `Web/Admin/PageArchiveReader.cs` | new | Deserializes an uploaded `.sqlite` into an in-memory connection, checks its identity, `quick_check`, the schema shape and every row, and returns candidates only when every rule holds; otherwise it returns the problems and no candidates. Enforces the caps and `ReservedPaths`, and validates state against `PageStorageOptions`. |
| `Web/Admin/PageArchiveWriter.cs` | new | Builds the archive in an in-memory connection from `(PagePath, string source, IReadOnlyList<PageStateValue>)`, then returns the serialized bytes. |
| `Web/Admin/PageArchiveEntry.cs` | new | One validated candidate: key, source, state, sizes. |
| `Web/Admin/ImportStage.cs` | new | A staged archive, its selection, and its job state and results, mutated under one lock. |
| `Web/Admin/ImportStageStore.cs` | new | Singleton: create, get, evict on TTL and count. Uses `TimeProvider`. |
| `Web/Admin/ImportQueue.cs` | new | One-reader bounded queue with the same reservation shape as `RunQueue`. |
| `Web/Admin/ImportWorker.cs` | new | `BackgroundService`; runs one job at a time through a scope, because `PageImageBuilder` and the batch store are scoped. Compiles the batch, then commits it once, and reports a cap refusal as the batch outcome. |
| `Web/Admin/ImportEndpoint.cs` | new | The upload, review, approve and progress views and their POST handling. |
| `Web/Admin/ExportEndpoint.cs` | new | The list view and the streaming download. |
| `Web/Pages/IPageStore.cs` | changed | Add `GetSourceAsync(PagePath)` so export does not load images. |
| `Web/Pages/SqlitePageStore.cs` | changed | Adds `GetSourceAsync` as a projection of `Template`, and makes the revision/head write an internal writer that both `SavePageAsync` and the batch store call, so the two paths cannot drift. |
| `Web/Pages/IPageStateStore.cs` | changed | Add `GetValuesAsync(PagePath)`; add `PageStateValue(string Key, string Value)`. |
| `Web/Pages/SqlitePageStateStore.cs` | changed | Adds `GetValuesAsync` in ordinal order, and makes the state upsert an internal writer taking a `StorageDbContext`, used by `SetValueAsync` and the batch store. |
| `Web/Pages/PageStateKey.cs` | new | The 1–`MaxKeyChars`, no-control-characters rule, so the archive reader and `PageStorageScope` share one copy. |
| `Web/Pages/PageTemplate.cs` | changed | A `NewRevision(path, source, image, runtimeId)` static factory, moved out of `PageEditor` so the import builds revisions through the same code. |
| `Web/Editor/PageEditor.cs` | changed | Uses the factory; its single-save behaviour and reasons are unchanged. |
| `Web/Pages/IPageBatchStore.cs` | new | `SaveBatchAsync(IReadOnlyList<PageBatchSave>)` returning `PageBatchResult`: committed, or refused with the paths whose merged state would overflow. `PageBatchSave(PageTemplate Template, IReadOnlyList<PageStateValue> State)` is the one shape that spans pages and state. |
| `Web/Pages/SqlitePageBatchStore.cs` | new | One context, one transaction: checks every page's merged state against `Pages:Storage` first and writes nothing if any would overflow; otherwise revisions, then heads, then state; commit once; request the storage refresh on success; retries a revision-id collision by regenerating the batch and trying again. |
| `Web/Pages/PageMetrics.cs` | changed | Add `import` to the compile `reason` values and a `page.import.duration` histogram. |
| `Web/Pages/ReservedPaths.cs` | changed | Add `/admin` to `Default`. |
| `Web/Program.cs` | changed | Routes, authorization, antiforgery metadata, `FormOptions` limits (body length, memory threshold, value count) tied to the archive caps, and singleton/hosted registrations. |
| `Web/appsettings.json` | changed | `Pages:ReservedPaths` gains `/admin`; new `Pages:Archive` section; `Assistant:ProtectedPaths` gains the new assets. |
| `Directory.Packages.props`, `Web.csproj` | changed | Pin `SQLitePCLRaw.core` 2.1.13 directly, for `sqlite3_serialize`/`sqlite3_deserialize`. |
| `Web/Pages/Seeds/admin.css` | new | The drop zone, tags and progress list. A new page key, so startup seeding creates it on an existing site too. |
| `Web/Pages/Seeds/admin.js` | new | Drop-zone enhancement, select-all, and the live count. |
| `Web/Editor/EditorEndpoint.cs` | changed | The "Page administration" nav on the listing. |

No migration. State rows, revisions and heads already exist and import writes through them.

## Metrics and logs

- Every imported page records `page.compile.duration` with the reason `import` rather than `save`, so
  an import spike is visible in Prometheus and the dashboard's existing `reason` label starts showing
  the third value.
- `page.import.duration` is a histogram with an `outcome` tag (`committed`, `failed`, `aborted`), so
  the whole batch is visible as one operation without inventing a per-page save time that the batch
  does not have.
- The worker logs one structured line at start and end with the stage id, selected, compiled and
  committed counts, and total duration; compile faults and commit failures are logged with the path
  and the exception.

## Tests

The suites follow [testing](../testing.md): the reader, writer, endpoints and staging in
`Web.Tests`, and the composed flow in `Web.IntegrationTests`. There is no `Core.Tests` work: the
archive is a SQLite file, so the pure rules it exercises are the page-path and state rules that
already have tests.

**`Web.Tests` — `PageArchiveWriterTests`**

- `WritingThenReadingReturnsTheSamePagesAndState`
- `APageWithoutStateWritesNoStateRows`
- `TheDatabaseCarriesTheFormatApplicationIdAndVersion`
- `SerializedBytesAreAReadableDatabase`

**`Web.Tests` — `PageArchiveReaderTests`** (real serialized SQLite bytes)

- `AValidArchiveIsReadIntoKeysSourcesAndState`
- `AFileThatIsNotASqliteDatabaseIsRejected`
- `ADatabaseWithoutTheFormatIdentityIsRejected`
- `AnotherUserVersionIsRejected`
- `APathThatIsNotNormalisedIsRejected`
- `AReservedPathIsRejected`
- `AStateRowWithoutAPageIsRejected`
- `AStateKeyOverTheCapIsRejected`
- `AStateValueOverTheCapIsRejected`
- `AStateValueThatIsNotJsonIsRejected`
- `ANonTextValueIsRejected`
- `AnInvalidUtf8ValueIsRejected` — `CAST(X'FF' AS TEXT)` in a TEXT column.
- `TooManyPagesAreRejected`
- `AViewNamedPagesIsRejected`
- `AnExtraTableIsRejected`
- `AnExtraColumnIsRejected`
- `AMissingPageStateTableIsRejected`
- `AnArchiveWithNoPagesIsRejected`
- `AnEmptyFileIsRejected`

**`Web.Tests` — `ExportEndpointTests`**

- `TheListShowsEveryReachableLivePageTicked`
- `TheListSkipsDeletedReservedAndUnaddressablePages`
- `DownloadWritesThePagesAndStateTables`
- `DownloadIncludesState`
- `DownloadSkipsUntickedPages`
- `DownloadRefusesAKeyThatIsNotALivePage`
- `DownloadSetsTheSqliteContentTypeAndAnIsoTimestamp`
- `DownloadSetsAContentLength`
- `AnEmptySelectionAnswers400`

**`Web.Tests` — `ImportEndpointTests`**

- `UploadingAnInvalidArchiveAnswers400AndStagesNothing`
- `UploadingAnArchiveWithOneBadRowStagesNothing`
- `UploadingAValidArchiveRedirectsToTheReview`
- `TheReviewTagsANewPage`
- `TheReviewTagsAnOverwrite`
- `TheReviewTagsATombstonedPageAsRestore`
- `EveryPageIsTickedByDefault`
- `ApprovingCommitsOnlyTheTickedPages`
- `AnOverwriteAddsARevisionAndKeepsTheOldOne`
- `StateIsCommittedWithThePage`
- `APageThatDoesNotCompileIsCommittedWithTheRest`
- `ACompilerFaultCommitsTheSourceWithoutAnImage`
- `ACommitFailureLeavesEverySelectedPageUntouched`
- `AMergedStateOverTheCapRefusesTheWholeBatch`
- `AnImportOverTheCompiledCapWritesNothing`
- `ASecondApprovalWhileRunningIsRefused`
- `AnExpiredStageIsNotFound`
- `TheProgressPageReportsEachOutcome`

**`Web.Tests` — `SqlitePageBatchStoreTests`** (real SQLite file)

- `EveryPageIsVisibleOnlyAfterTheCommit`
- `AFailedWriteRollsBackTheWholeBatch` — make the second page's state writer throw after the
  revisions and heads have been written, then assert no head moved and no revision or state row was
  added.
- `AMergedStateOverTheCapWritesNothing` — the refusal is reported with the offending path, and no
  revision, head or state row changes.

**`Web.IntegrationTests` — `ArchiveEndpointTests`**

- `ImportAndExportRequireSignIn`
- `ImportAndExportRequireAntiforgery`
- `ExportThenImportRoundTripsThroughTheRealHost` — upload with `MultipartFormDataContent`, approve,
  poll the progress page, then fetch the page and its state.
- `AnOversizedUploadAnswers413`
- `AnImportedReservedPathIsRefused`
- `AdminPathsAreNotPageKeys`

**`Web.BrowserTests` — `ArchiveBrowserTests`** (opt-in)

- `ADroppedFileFillsTheUploadInput` — the enhancement works, driven in Firefox.

**Accessibility and shell**

`ShellViews` gains `admin-import`, `admin-import-failed`, `admin-import-review`,
`admin-import-progress` and `admin-export`, so the eleven shared rules cover them;
[testing](../testing.md) says "seventeen shell views" and moves to twenty-two.

## Documentation and decisions

- New decision **DEC-138** recording: the archive is a SQLite database (in-memory serialize /
  deserialize, `application_id` and `user_version` as the format identity), source and state only,
  import as normal revisions through a single-worker compile-then-commit queue, one transaction for
  the whole batch, compile failures tolerated, import is additive, `/admin` reserved again.
- `docs/architecture.md`: an "Import and export" subsection under "Source storage", and the archive
  added to the sentence that lists what scrub cannot erase.
- `docs/jobs/IDEA-009.md` and the job table: the idea is implemented once every phase below is done.
- `docs/testing.md`: the view count, and one sentence naming the archive suites.

## Phases

Each phase ends with `make test` green.

1. **Format and writer.** `PageArchiveFormat` with the identity and schema, `PageArchiveWriter`, and
   the round-trip tests. No endpoint.
2. **Reader.** `ArchiveOptions`, `PageArchiveReader`, deserialization, `quick_check` and every
   validation rule, with its tests. Still no endpoint.
3. **Export.** The storage seams (`GetSourceAsync`, `PageStateValue` and `GetValuesAsync`),
   `ExportEndpoint`, the list and download tests, the `/edit` nav and `/admin` reserved. Now an
   archive can be produced and inspected by hand.
4. **Upload and review.** `ImportStage`, `ImportStageStore`, `ImportEndpoint` upload/review, the
   reader wired in, and the review tests. Nothing is written to storage yet.
5. **One transaction.** The shared revision/head/state writers, `IPageBatchStore` and
   `SqlitePageBatchStore`, and the batch tests, including the rollback proof. No worker yet, so the
   transaction can be driven directly.
6. **Compile, then commit.** `ImportQueue`, `ImportWorker`, approve and progress, the compile phase,
   the apply tests, and the integration round trip.
7. **Finish.** `admin.css`, `admin.js`, the drop-zone browser test, `ShellViews` and accessibility,
   docs and the decision.

Phases 3 and 4 can be built in either order; 5 and 6 depend on both.

## Out of scope

- **Per-page export** from the editor (`/edit/foo?cmd=export`). The archive is site-wide; a single
  page could follow later as the same writer over one row.
- **History and tombstones** in the archive. Both are deliberate, and both need their own threat
  model before hashes and images cross a site boundary.
- **An undo or roll-back action.** The batch commits or not, but a committed batch is history like
  any other save: the way back is another import.
- **Scheduling imports**, importing from a URL, or keeping the uploaded file on disk. The stage lives
  in memory and expires.
- **A restore-point feature.** Export is a copy, not a backup; the database and its volume remain the
  thing to back up.
- **Editing the archive in the browser.** Import is all-or-nothing per batch, not a merge editor.
- **Nuke and import.** Deleting every page the archive does not mention, with its history, as part of
  the import. It is technically a `DELETE` in the same transaction, but it turns a safe additive
  operation into an irreversible one and still does not make the archive a backup. Recorded as
  [IDEA-020](../jobs/IDEA-020.md).
- **A zip format.** It was the first shape, but SQLite already answers every requirement and removes
  the entry-name class of validation. A zip reader would be a second parser to keep.
