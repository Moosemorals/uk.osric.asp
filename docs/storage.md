<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Storage

The SQLite database behind the site: pages and their revisions, the values a page keeps, a
visitor's session and browser, and the archive that moves pages between sites. [Pages](pages.md#compiled-pages) says what a stored image is and when it
is stale; [running it](../deploy/operations.md#storage-and-migrations) is the volume, the migrations
and the backups.

## Source storage

The Web layer owns EF Core SQLite source storage and applies migrations asynchronously during
startup, before serving requests. `ConnectionStrings:Storage` defaults to
`Data Source=data/storage.db`; deployment overrides it to `Data Source=/data/storage.db` on a durable
rootless Podman named volume. Database access and migration lifecycle stay in the shell.

This is template storage: `PageEditor` writes a page's template, `PageEndpoint` reads it by route
path and runs its compiled image. Pure rules still receive the values loaded by the shell rather
than being given `IPageStore`.

### Values

`StoreValues` holds every value a page keeps, whoever holds it. One row is the subject, the page key
that wrote it, the key, the lifetime and the expiry that lifetime carries, the value as JSON and when
it was last written; the primary key is `(Subject, PageKey, Key)`, so a key is unique per page within
a subject and the whole access path is the key. There is no foreign key to `PageHeads` — the deletes
that keep a value from outliving its page are explicit, and SQLite's enforcement depends on the
connection. A value belongs to a page key, not to a revision: an edit or a restore keeps it, and
deleting the page removes its Page subject's values in the same transaction as the tombstone.

A **subject** is whose the values are, spelled as one text key so that a subject is a range:

- `page:<key>` — the page itself, shared by every visitor.
- `account:<name>` — the person, once they have signed in.
- `session:<id>` — the visitor the `s` cookie names, before they have signed in.
- `browser:<id>` — the browser the `b` cookie names, which outlives a sign-in and a sign-out.

A page never sees a subject key, a session id or an account name: the shell resolves one reader
(`PageReaderSubject`) per request and hands the page `ctx.Stores.Page` and `ctx.Stores.Visitor`.

A **lifetime** is the rule that kills a value, and it is on the row so that a read can report it:

| Lifetime | Dies when | Who may hold it |
| --- | --- | --- |
| `Page` | the page goes | a Page subject |
| `Person` | the account goes; anonymously, when the session does | a Visitor |
| `Session` | the session rotates, is signed out, or is swept | a Visitor |
| `Browser` | the browser cookie expires or is cleared | a Visitor |
| `NextRead` | the first successful take of that key | either |

A write without a lifetime takes the subject's own — `Page` on a Page subject, `Person` on a Visitor
— and a lifetime the subject cannot carry is a page bug that throws rather than a value quietly kept
somewhere else. `Until` adds an expiry to the subject's default and `WithExpiry` adds one to a named
lifetime; an expired row is never answered and the sweep deletes it
(`pages-a-lifetime-belongs-to-the-entry`).

A Value read searches the reader's subjects in order — `session:`, then `browser:`, then `account:`
— so a page's own preference shadows nothing, a browser's preference shadows the account's, and the
closest owner wins (`pages-a-browser-is-a-lifetime`). A read of another page's value names that page's
key; a write cannot, because the handle is bound to the layer's own key and there is no argument that
names another writer (`pages-the-subject-is-the-binding`).

### Page sessions and the browser

A visitor's identity is one table, `PageSessions`, added by its own migration: one row per visitor —
the id the `s` cookie carries, the account it belongs to or `NULL`, and a created and a sliding
timestamp, both UTC ISO-8601 text so a range delete and an eviction order can run inside one
statement. Two indexes back the sweep and the eviction order: one on `(Account, UpdatedAt)` and one
on `UpdatedAt`. The values that used to sit beside it are rows in `StoreValues` now, so a session's
delete removes them in the same transaction rather than through a cascade.

The caps live in `Pages:Stores`: the longest key, the command and busy timeouts, and then one set of
caps per subject instance — a value's bytes, the key count and the byte total — for a page, an
anonymous visitor, a browser and a signed-in visitor, with the anonymous idle window, the browser's
lifetime in days and the live-session cap beside them. Every value is required: a missing or
out-of-range one stops the site at startup with a message naming the key, its range and the shipped
value ([configuration](architecture.md#configuration), `pages-one-stores-section`). Each write is one
statement that refuses at the cap: a Page write also needs a live head, the key count must stay inside its cap and the byte
total inside its cap, all proved in the statement that writes, so a concurrent writer either fits or
is refused. The caps are per subject instance, not per page, so one page's keys can use the room
another would have had.

A visitor's expiry is the sliding window on their `PageSessions` row; a value's expiry is its own
column. The sweep is `PageSessionCleanupService`, a five-minute `PeriodicTimer` that deletes expired
sessions, expired values and browser rows nobody has written for the browser's lifetime, and trims an
overfull session table, logging one line with the counts by kind when it removed anything. A mint pays
for housekeeping only when it would otherwise have evicted: it deletes expired rows first, then
evicts the most idle anonymous session, and only reaches a signed-in session when no anonymous one is
left. That order is what keeps a flood of new anonymous visitors from pushing the owner's own session
out.

Signing in is where a subject changes. One transaction re-keys the leaving session's `Person` and
`NextRead` rows into `account:<name>`, deletes whatever is left under `session:<old>` — a `Session`
value dies with the session that held it — and inserts the account's session. A one-shot therefore
survives sign-in unread, and the value a page left for a person is still there afterwards. A
`Browser` value is not touched at all: it never belonged to the session
(`pages-adoption-re-keys-at-sign-in`).

The migration that built `StoreValues` copied both tables it replaced — `PageState` as
`page:<key>` rows under the `Page` lifetime, `PageSessionValues` as `session:<id>` rows under
`Session` — and then dropped them. Its `Down` restores the two tables from the rows that can still be
expressed in them, so a `Person`, `Browser`, `NextRead` or expiring row does not survive a rollback.

`PageRevisions` holds every save as a separate row, including its source, revision hash, creation
runtime id, optional compilation result and a nullable `TemplateKind` verdict. The verdict is
written by the generation that judged the source: `0` is a template with no code, so there is no
image and every request serves the stored source; `1` is a page, and the image fields are whatever
the compile reached; `2` is a library, which is checked and stores no assembly; `3` is a worker, which
is emitted like a page; `NULL` means the revision has never been judged.

`TemplateLibraries` is the fingerprint of the libraries an image was compiled against: one
`key@revisionHash` per library, sorted and joined, and `NULL` or empty when the page named none. It is
opaque, compared with the live heads rather than read as data, and it is what makes a library edit
recompile exactly its dependents. A lazy compile fills a
`NULL` verdict on the revision it compiled. Nothing backfills a row that already exists — including
the seeded assets — so a revision keeps `NULL` until it is next saved, restored or first generated.

A transaction is the store's own SQLite transaction, taken with `BEGIN IMMEDIATE` and therefore
holding the one writer from its first statement, so the heads it re-proves its expectations against
cannot move under it. Everything slow happens outside it: a staged save compiles first, and the commit
is statements only. The store runs in WAL, which is set and verified at startup — a reader never blocks
a commit and a commit never blocks a reader — and a commit that cannot take the writer answers that it
is busy after `Pages:Stores:BusyTimeoutSeconds` rather than waiting the client's default out. A
refusal writes nothing: the operations are applied in the order the page staged them, each proving its
own condition against the rows the ones before it wrote — so a create, a move and a delete of one page
in one transaction are a sequence that succeeds — and a refusal rolls the whole transaction back and
the report says which operations were applied and undone and which were never reached. A transaction
nested inside another is not a second store transaction: it merges its operations into the outer one
and is decided by its commit, so the store still has exactly one transaction per commit
(`pages-writes-are-transactions`).

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
exists, so a deleted seeded asset stays deleted across restarts; the startup step that follows it
writes a seed whose page still holds the one it was installed from, which is the one seed update the
store permits without the owner's tick. Explicit scrub removes the head and all revisions for one
path, but only after deletion; live pages must be deleted first. The one exception is a stored path at
a key the shell answers: no page surface can delete or scrub it, so `/admin/seeds` proves the key is
reserved and scrubs its head, revisions and state without a tombstone. Scrub then performs SQLite
secure-delete and WAL checkpoint cleanup where applicable. This is best-effort cleanup, not forensic
erasure: it cannot revoke running code or erase browser/CDN
copies, exported archives, backups or filesystem snapshots. Rotate exposed secrets and clean external
copies separately. Runtime-change cleanup of obsolete images and symbols is future work; source and
revision identity are retained unless explicitly scrubbed.

A move re-files a page in one transaction: the head row at the old key is deleted, every revision's
`Path` is updated to the new key, the page's values are re-keyed from `page:<old>` to `page:<new>`,
and a head is inserted at the new key with the revision the page already had and the anchor and
content hash it already carried. The head
goes first and comes back last because `PageHeads(RevisionId, Path)` points at a revision of the head's
own path, so the foreign key would refuse any other order; nothing is recompiled. Every hash is kept,
so the history is one history under the new name, and nothing is left at the old key. A tombstone at
the new key is deleted first and keeps the history it retained; its state belonged to the dead page and
is discarded. The deletion of the old head is conditional on the revision the move read, so a save that
lands in between makes the transaction roll back and the caller is answered Stale.

A stored page may read the same store through `ctx.Capabilities.GetRequired<IPageCatalog>()`
(`pages-the-store-is-read-through-one-seam`, `pages-reading-the-store-is-public`, `pages-a-listing-carries-facts`, `product-manual-is-a-page`, `pages-delete-install-and-restore`, `pages-page-first`). The read is a capability of its own rather than `IPageStore` because the latter
is scoped to the request's context and a page's task can outlive the request: `IPageReadStore` is a
read-only singleton over the context factory, it answers with small `Web` records and never with a
`PagePath`, and `PageCatalogScope` maps them to the `Sdk` values. A listing answers the tree: the
heads the store holds — live, tombstoned and seeded — with the folders above them and the keys the
shell's routes answer, each a key with a kind and the head's facts where a head is stored, and a
prefix selects a subtree rather than a string prefix. A folder is derived, never stored
(`pages-a-folder-is-a-key-not-a-row`): `Core`'s `PageListingFold` adds the folders above the keys a read
answered, so no column, table or write path maintains one and there is no migration. A listing that
answers one level also asks `ListFoldersAsync`, the seam's second raw statement, because a child whose
only content is deeper is not a head a one-level read can see; it cuts each key to its segment with
SQLite's `length()` and `substr`, which count characters where a `string.Length` computed in C# counts
UTF-16 units. Because a folder is derived from the head rows, its token moves exactly when the token of
the heads under it does, and the routes sit outside it because the route table is the build's.
`Pages:Read:MaxRowsPerList`
(500 shipped) is enforced in the statement and refuses one row past the cap,
`Pages:Read:MaxSourceCharsPerRender` (512 KiB shipped) is one `ReadBudget` shared by every layer of
the chain and refuses rather than truncating, and both the deadline and abandonment stop a read. The
read is as public as the page that calls it: the host adds no gate, a page that wants privacy writes
the ordinary `ctx.User` guard, and a page that reads source publishes it to whoever can reach that
page. A listing's `PageListing.Version` is derived rather than stored: the read fingerprints the head
rows under the prefix in one deferred read transaction with the slice, so no column, counter or write
path maintains it and there is no migration. The two statements share the snapshot the token names,
and nothing is pinned beyond them (`pages-a-listing-carries-a-fingerprint`,
`pages-a-fingerprint-is-derived-not-kept`).

A page searches the same store through `IPageCatalog.SearchAsync`, the fifth read, and the read it stands
on is one stepped statement: `IPageReadStore.ScanAsync` selects the live heads under a prefix, joined to
their revision for the template, in key order and at or after the key a cursor names — inclusive, because a
cursor resumes *inside* a source rather than after it. The enumeration holds the statement's snapshot for
as long as it runs and pins nothing beyond it, so a source is read only when the scan reaches it and a scan
stopped by its allowance leaves nothing open. Its sources are the live pages and the shell's route records;
a tombstone is never one, and a route takes the place of a head it shadows. `Pages:Search:MaxDurationMs`
bounds the whole call — the prefix's fingerprint, the reads and the matching — while
`Pages:Search:MaxMatches` and `Pages:Search:MaxContextChars` bound what an answer may hold. None of them
charges `Pages:Read:MaxSourceCharsPerRender`: a search answers snippets, not sources, so the render's
source allowance is not its currency.

A page may save its own template through `ctx.Capabilities.GetRequired<IPageWrites>().SaveAsync(template)`, and any page the shell does not route through the same capability's named save (`pages-a-page-may-save-itself`, `pages-any-page-may-write-the-store`, `pages-layouts-are-markup`, `pages-scratch-saves-itself`).
The write is the shell's save reached from inside a render: `PageSaver` compiles the template and
`IPageRevisionStore` opens its own context for the insert, because a page's task can outlive the
request, and both call the same `StoreRevisionAsync` the editor and the import batch use, so there is
still one writer. Unlike the read, the write requires a signed-in visitor and refuses an anonymous
render before anything is compiled or stored; a save that has started finishes even if the render
times out, because losing the author's edit is the worse failure. Delete, scrub, install a seed,
import and export stay the shell's alone (`pages-page-first`).

### Assistant memory

The assistant's memory is three tables in the same database, added by one migration (`assistant-memory-is-the-owners`, `assistant-memory-recall-is-fts5`).
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
untrue, and a stale `expectedVersion` is refused with the content that is stored now (`assistant-memory-page`,
`assistant-memory-page`).

Nothing in `Sdk`, `Runtime` or the page contract reaches memory: a stored page is public to whoever
can reach it, and a memory belongs to one owner (`assistant-memory-is-the-owners`).

### Import and export

A site's pages can leave it and come back. `/admin/export` lists every live page that is addressable
and not reserved, and a `POST` returns a `.sqlite` file as a download; `/admin/import` takes that
same file back behind the `Editor` policy. The archive is a SQLite database — `Pages(Path, Source)`
and `PageState(PagePath, Key, Value)`, identified by `PRAGMA application_id` and `user_version` —
built in memory with `sqlite3_serialize` and read by deserializing an upload read-only with
`sqlite3_deserialize`, so neither direction needs a temporary file (`pages-an-archive-is-a-database`).
The second table keeps the name and the shape it had before `StoreValues` replaced it, so an archive
written by an earlier release still imports.

The archive carries source and the page's own values and nothing else. Only a `Page` lifetime row
with no expiry under `page:<key>` goes into `PageState`; a `Person`, `Session`, `Browser` or
`NextRead` row is somebody's rather than the page's, and an expiring row is not content. Revision
history, tombstones, users, page sessions and configuration belong to the site that made them — a
session is a visitor's scratch space rather than content — and shipping hashes and runtime ids across
sites would import code nobody built. An import writes each value back under the `Page` lifetime, so
a page arrives with the same shared data it left with. Import is additive — pages absent from the archive are untouched and
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
response type follows that name's extension through `Pages:ContentTypes`, so the scripts and
stylesheets are pure source with no directive of their own. The seeded `/site.css` has no code, so it
is stored as source and never compiled, and it holds the site's look and the rules the document
shell's markup needs (see [the document shell](shell.md#the-document-shell)). The files only the
shell's own views load — `/admin.css`, `/admin.js`, `/status.css`, `/reference.css` — and the icon
at `/rocket.svg` are in the project's `wwwroot` instead: they are served by the static file handler
and are not pages at all. Editing a seed file changes the initial content for
a never-created page, not an existing database page. A start applies the bundled source to every page
that still holds the seed it was installed from, as a new revision with the shipped hash recorded as
its anchor — the one seed write the store permits without the owner's tick. The authenticated install
action applies the same write to a page a start did not reach, or to one the owner chooses, and its
confirmation refuses the install when the live template already matches the seed. The editor page lists
every page, with the seeded ones under their own heading below the owner's; a seeded row carries an
install link only while the live template differs from its seed. Restoring a retained revision is
the same write: it saves that revision's source as a new revision under the running runtime, clears
a tombstone and refuses a revision whose source the live page already holds. A seed write, a restore
and an adopt all move a listing's token because each changes a head row, while the seed verdict stays
the three hashes: a fingerprint is a fact about a set of rows, and a seed verdict is one about the
content identity of a row.
