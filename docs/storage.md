<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Storage

The SQLite database behind the site: pages and their revisions, page state, a visitor's session, and
the archive that moves pages between sites. [Pages](pages.md#compiled-pages) says what a stored image is and when it
is stale; [running it](../deploy/operations.md#storage-and-migrations) is the volume, the migrations
and the backups.

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is template storage: `EditorEndpoint` writes a page's template, `PageEndpoint` reads it by route
path and runs its compiled image. Pure rules still receive the values loaded by the shell rather
than being given `IPageStore`.

`PageState` holds a page's key/value store: one row per page and key, with the value as JSON and a
primary key of the whole access path, so no separate index is needed. There is no foreign key — the
deletes that keep state from outliving its page are explicit, and SQLite's enforcement depends on the
connection. State is per path, not per revision: an edit or a restore keeps it, and deleting the page
clears it in the same transaction as the tombstone.

### Page sessions

A visitor's session is two tables in the same database, added by one migration. `PageSessions` is one
row per visitor — the id the `s` cookie carries, the account it belongs to or `NULL`, and a created
and a sliding timestamp, both UTC ISO-8601 text so a range delete and an eviction order can run
inside one statement. `PageSessionValues` is one row per value keyed by session, page path and key,
with the value as JSON and a cascade delete from the session row. The page path is required on every
row — the shell is a session owner with a reserved path, exactly like a page — so there is no null or
empty-string area and SQLite's NULL-is-distinct rule never reaches the unique key. Two indexes back
the sweep and the eviction order: one on `(Account, UpdatedAt)` and one on `UpdatedAt`.

The caps live in `Pages:Session`: one value's bytes, one session's key count and byte total, and the
idle lifetime, for an anonymous visitor and for a signed-in one, plus the live-session cap and a
command timeout. Every value is required: a missing or out-of-range one stops the site at startup
with a message naming the key, its range and the shipped value
([configuration](architecture.md#configuration)). The key length is `Pages:Storage:MaxKeyChars`
because a session key is the same kind of key. Each write is
one statement that refuses at the cap, the way `SqlitePageStateStore.UpsertAsync` does: the session
must still exist, the key count must stay inside its cap and the byte total inside its cap. The caps
are per visitor, not per page, so one page's keys can use the room another would have had.

Expiry is a sliding window per row; the sweep is `PageSessionCleanupService`, a five-minute
`PeriodicTimer` that deletes expired rows and trims an overfull table, logging one line with the
counts by kind when it removed anything. A mint pays for housekeeping only when it would otherwise
have evicted: it deletes expired rows first, then evicts the most idle anonymous session, and only
reaches a signed-in session when no anonymous one is left. That order is what keeps a flood of new
anonymous visitors from pushing the owner's own session out.

`PageRevisions` holds every save as a separate row, including its source, revision hash, creation
runtime id, optional compilation result and a nullable `TemplateKind` verdict. The verdict is
written by the generation that judged the source: `0` is a template with no code, so there is no
image and every request serves the stored source; `1` is a page, and the image fields are whatever
the compile reached; `2` is a library, which is checked and stores no assembly; `NULL` means the
revision has never been judged.

`TemplateLibraries` is the fingerprint of the libraries an image was compiled against: one
`key@revisionHash` per library, sorted and joined, and `NULL` or empty when the page named none. It is
opaque, compared with the live heads rather than read as data, and it is what makes a library edit
recompile exactly its dependents. A lazy compile fills a
`NULL` verdict on the revision it compiled. Nothing backfills a row that already exists — including
the seeded assets — so a revision keeps `NULL` until it is next saved, restored or first generated.

A save inserts the revision and advances its `PageHeads` row in one transaction. The head is the
authoritative current-page pointer; reads do not guess the current row from timestamps. The source
identity and hash stay fixed while conditional
compilation updates may fill or refresh only the compilation fields of that revision. A diff is not
a stored value: it is computed from two retained templates when it is asked for, and the only copy
in the database is the assistant's ordinary tool result in the transcript. Reusing an image
requires both a matching source fingerprint and the current runtime id, so matching images are
reused and stale ones are recompiled lazily.

Revision hashes address retained revisions, including historical ones and those of a tombstone. The
same 64-character lowercase hexadecimal value is the public `?v=` value: `RevisionHash.IsCanonical`
accepts exactly that spelling, and the hash lookup never substitutes the head. The hash is not an
authorization token; a versioned URL is public. Ordinary page reads follow the head and ignore
deleted pages; a versioned read deliberately does not.

Deletion marks a head as a tombstone without removing its revision pointer or history. Saving again
at the same path appends a revision and clears the tombstone. Startup seeding checks whether a head
exists, so a deleted seeded asset stays deleted across restarts. Explicit scrub removes the head and
all revisions for one path, but only after deletion; live pages must be deleted first. Scrub then
performs SQLite secure-delete and WAL checkpoint cleanup where applicable. This is best-effort
cleanup, not forensic erasure: it cannot revoke running code or erase browser/CDN copies, exported
archives, backups or filesystem snapshots. Rotate exposed secrets and clean external copies
separately. Runtime-change cleanup of obsolete images and symbols is future work; source and
revision identity are retained unless explicitly scrubbed.

A stored page may read the same store through `ctx.Capabilities.GetRequired<IPageCatalog>()`
(DEC-168–DEC-175). The read is a capability of its own rather than `IPageStore` because the latter
is scoped to the request's context and a page's task can outlive the request: `IPageReadStore` is a
read-only singleton over the context factory, it answers with small `Web` records and never with a
`PagePath`, and `PageCatalogScope` maps them to the three `Sdk` values. A listing returns every head
the store holds — live, tombstoned, seeded and reserved — marked `IsDeleted`, `IsReserved` and
`IsHtml`, and a prefix selects a subtree rather than a string prefix. `Pages:Read:MaxRowsPerList`
(500 shipped) is enforced in the statement and refuses one row past the cap,
`Pages:Read:MaxSourceCharsPerRender` (512 KiB shipped) is one `ReadBudget` shared by every layer of
the chain and refuses rather than truncating, and both the deadline and abandonment stop a read. The
read is as public as the page that calls it: the host adds no gate, a page that wants privacy writes
the ordinary `ctx.User` guard, and a page that reads source publishes it to whoever can reach that
page.

A page may save its own template through `ctx.Capabilities.GetRequired<IPageWriter>()` (DEC-189–DEC-196).
The write is the shell's save reached from inside a render: `PageSaver` compiles the template and
`IPageRevisionStore` opens its own context for the insert, because a page's task can outlive the
request, and both call the same `StoreRevisionAsync` the editor and the import batch use, so there is
still one writer. Unlike the read, the write requires a signed-in visitor and refuses an anonymous
render before anything is compiled or stored; a save that has started finishes even if the render
times out, because losing the author's edit is the worse failure. Delete, scrub, install a seed,
import and export stay the shell's alone (DEC-175).

### Assistant memory

The assistant's memory is three tables in the same database, added by one migration (DEC-184–DEC-187).
`AssistantMemories` is one row per memory — the owner, one of four kinds, the content, a version, the
owner's `Pinned` flag, the conversation and run that wrote it, and the usage columns recall updates.
`AssistantMemoryEdits` is the history: the key is the memory and the version that was replaced, and
its foreign key to the head cascades, so forgetting a memory takes its history with it.
`AssistantMemorySearch` is an FTS5 virtual table over the heads — external content, so it stores no
copy of the text and reads a memory by its rowid, with `Owner` and `Kind` as unindexed columns and the
porter tokenizer. Three triggers keep it: an insert, a delete, and an update that names only
`Content`, `Owner` and `Kind`, so the per-recall touch of `LastRecalledAt`, `LastRecalledConversationId`
and `RecallCount` does not reindex the row.

EF Core models the two ordinary tables and the migration writes the virtual table and its triggers as
raw SQL, because EF cannot model a virtual table: the model snapshot does not mention them, so a later
migration will not drop them, and the hand-written `Down` drops the triggers and the table before the
two tables. `AssistantMemoryMigrationTests` applies the migration up, down and up again, and checks
the schema names either side. The index over `("Owner", "UpdatedAt")` backs the owner's listing and
the pinned tier.

Provenance is not a foreign key, because a memory outlives the conversation that wrote it: deleting a
conversation nulls `SourceConversationId` and `SourceRunId` in the same transaction as the delete, and
`users remove` deletes the account's memories before the account. A forget is a delete — no tombstone,
because a memory is personal data rather than a page revision — and the index row goes with it through
the delete trigger. As with a page scrub, deleting the row is not forensic erasure: WAL frames,
backups and snapshots are the operator's.

`EditAsync` is the owner's write, reached from `/assistant/memory` and from nothing else: it
normalises and checks the content exactly as the tools do, appends the version it replaces to
`AssistantMemoryEdits`, bumps the head and — the one thing no tool can do — sets `Pinned`. It keeps the
head's provenance, because an owner's edit does not make the conversation that wrote the memory
untrue, and a stale `expectedVersion` is refused with the content that is stored now (DEC-206,
DEC-207).

Nothing in `Sdk`, `Runtime` or the page contract reaches memory: a stored page is public to whoever
can reach it, and a memory belongs to one owner (DEC-184).

### Import and export

A site's pages can leave it and come back. `/admin/export` lists every live page that is addressable
and not reserved, and a `POST` returns a `.sqlite` file as a download; `/admin/import` takes that
same file back behind the `Editor` policy. The archive is a SQLite database — `Pages(Path, Source)`
and `PageState(PagePath, Key, Value)`, identified by `PRAGMA application_id` and `user_version` —
built in memory with `sqlite3_serialize` and read by deserializing an upload read-only with
`sqlite3_deserialize`, so neither direction needs a temporary file (DEC-139).

The archive carries source and state and nothing else: revision history, tombstones, users, page
sessions and configuration belong to the site that made them — a session is a visitor's scratch
space rather than content — and shipping hashes and runtime ids across sites would import code
nobody built. Import is additive — pages absent from the archive are untouched and
nothing is deleted — and it never invents a storage path: each approved page becomes a revision
through the same factory an editor save uses, so an overwrite is an ordinary new revision and the old
source stays in history.

Validation treats the file as hostile and is all or nothing: the schema must be exactly the two
tables and their columns, every path must already be canonical, addressable and unreserved, every
value must be text that decodes as strict UTF-8, every state key and value must pass the storage caps
the store enforces, and one bad row rejects the whole archive before anything is staged. Problems are
collected up to ten so the error page can list several at once, but each is independently fatal.

An approved archive is compiled one page at a time with nothing written, then committed in one SQLite
transaction: revisions, then heads, then state. A page that does not compile — or a compiler fault —
is stored with its diagnostics or with no image, exactly as a save would store it, and the rest of the
batch still commits. A batch whose merged state would exceed a page's caps is refused before any row
is written, and a failed write or a crash rolls the whole transaction back, so readers see the old
pages until the commit and the whole new set afterwards. There is no undo: the batch either commits
or does not, and the way back is another import.

Built-in page templates live in `src/uk.osric.asp.Web/Pages/Seeds`, included as content in build and
publish output. Each file is a complete page template and its filename becomes the page path. The
response type follows that name's extension through `Pages:ContentTypes`, so the scripts,
stylesheets and SVG are pure source with no directive of their own. The seeded `/rocket.svg` and
`/site.css` have no code, so they are stored as source and never compiled. `/site.css` holds the
site's look and the rules the document shell's markup needs (see [the document
shell](shell.md#the-document-shell)), while `/status.css` holds dashboard styles and is linked only
by `/status`. Editing a seed file changes the initial content for
a never-created page, not an existing database page. The authenticated install action applies the
bundled source to an existing or tombstoned page as a new revision, and its confirmation refuses the
install when the live template already matches the seed. The editor page lists every page, with the
seeded ones under their own heading below the owner's; a seeded row carries an install link only
while the live template differs from its seed. Restoring a retained revision is
the same write: it saves that revision's source as a new revision under the running runtime, clears
a tombstone and refuses a revision whose source the live page already holds.
