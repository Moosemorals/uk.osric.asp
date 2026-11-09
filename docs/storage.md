<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Storage

The SQLite database behind the site: pages and their revisions, page state, and the archive that
moves pages between sites. [Pages](pages.md#compiled-pages) says what a stored image is and when it
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

`PageRevisions` holds every save as a separate row, including its source, revision hash, creation
runtime id and optional compilation result. A save inserts the revision and advances its
`PageHeads` row in one transaction. The head is the authoritative current-page pointer; reads do not
guess the current row from timestamps. The source identity and hash stay fixed while conditional
compilation updates may fill or refresh only the compilation fields of that revision. Reusing an
image requires both a matching source fingerprint and the current runtime id, so matching images
are reused and stale ones are recompiled lazily.

Revision hashes address retained code but are not authorization tokens or rendered-body ETags. An
indexed hash lookup exists for future immutable asset serving; it is an internal storage operation,
not a public endpoint, and the asset endpoint is not implemented. Ordinary page reads follow the
head and ignore deleted pages.

Deletion marks a head as a tombstone without removing its revision pointer or history. Saving again
at the same path appends a revision and clears the tombstone. Startup seeding checks whether a head
exists, so a deleted seeded asset stays deleted across restarts. Explicit scrub removes the head and
all revisions for one path, but only after deletion; live pages must be deleted first. Scrub then
performs SQLite secure-delete and WAL checkpoint cleanup where applicable. This is best-effort
cleanup, not forensic erasure: it cannot revoke running code or erase browser/CDN copies, exported
archives, backups or filesystem snapshots. Rotate exposed secrets and clean external copies
separately. Runtime-change cleanup of obsolete images and symbols is future work; source and
revision identity are retained unless explicitly scrubbed.

A stored page may read the same store through `ctx.Features.GetRequired<IPageCatalog>()`
(DEC-168–DEC-175). The read is a capability of its own rather than `IPageStore` because the latter
is scoped to the request's context and a page's task can outlive the request: `IPageReadStore` is a
read-only singleton over the context factory, it answers with small `Web` records and never with a
`PagePath`, and `PageCatalogScope` maps them to the three `Sdk` values. A listing returns every head
the store holds — live, tombstoned, seeded and reserved — marked `IsDeleted`, `IsReserved` and
`IsHtml`, and a prefix selects a subtree rather than a string prefix. `Pages:Read:MaxRowsPerList`
(500 default) is enforced in the statement and refuses one row past the cap,
`Pages:Read:MaxSourceCharsPerRender` (512 KiB default) is one `ReadBudget` shared by every layer of
the chain and refuses rather than truncating, and both the deadline and abandonment stop a read. The
read is as public as the page that calls it: the host adds no gate, a page that wants privacy writes
the ordinary `ctx.User` guard, and a page that reads source publishes it to whoever can reach that
page.

A page may save its own template through `ctx.Features.GetRequired<IPageWriter>()` (DEC-189–DEC-196).
The write is the shell's save reached from inside a render: `PageSaver` compiles the template and
`IPageRevisionStore` opens its own context for the insert, because a page's task can outlive the
request, and both call the same `StoreRevisionAsync` the editor and the import batch use, so there is
still one writer. Unlike the read, the write requires a signed-in visitor and refuses an anonymous
render before anything is compiled or stored; a save that has started finishes even if the render
times out, because losing the author's edit is the worse failure. Delete, scrub, restore a seed,
import and export stay the shell's alone (DEC-175).

### Import and export

A site's pages can leave it and come back. `/admin/export` lists every live page that is addressable
and not reserved, and a `POST` returns a `.sqlite` file as a download; `/admin/import` takes that
same file back behind the `Editor` policy. The archive is a SQLite database — `Pages(Path, Source)`
and `PageState(PagePath, Key, Value)`, identified by `PRAGMA application_id` and `user_version` —
built in memory with `sqlite3_serialize` and read by deserializing an upload read-only with
`sqlite3_deserialize`, so neither direction needs a temporary file (DEC-139).

The archive carries source and state and nothing else: revision history, tombstones, users and
configuration belong to the site that made them, and shipping hashes and runtime ids across sites
would import code nobody built. Import is additive — pages absent from the archive are untouched and
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
stylesheets and SVG are pure source with no directive of their own; the seeded `/rocket.svg` sets
`image/svg+xml` explicitly as well, which is the same answer the map gives. `/site.css` holds the
site's look, `/layout.css` the rules the document shell's markup needs (see [the document
shell](shell.md#the-document-shell)), while `/status.css` holds dashboard styles and is linked only
by `/status`. Editing a seed file changes the initial content for
a never-created page, not an existing database page. The authenticated restore action applies the
bundled source to an existing or tombstoned page as a new revision. `/edit` lists every page, with the
seeded ones under their own heading below the owner's; a seeded row carries a restore button that is
disabled while the live template matches its seed and stays enabled for a deleted seed; a page that
already matches its seed is left alone.
