<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Editor tools and retained page revisions

Implementation plan for ARCH-06, ARCH-10 and ARCH-11, covering the completed editor listing,
deletion and generated-source work plus the retained-revision storage foundation for future
immutable asset URLs.
The history storage and browsing view also deliver part of [ARCH-07](../../jobs/ARCH-07.md); revision
restore remains later work. Asset serving and PageLayout adoption belong to
IDEA-010.

## Status

Steps 1-8 are implemented and verified. Documentation in step 9 is complete; the repository gate is
still pending. ARCH-06, ARCH-10 and ARCH-11 are closed. ARCH-07 remains open for revision restore;
IDEA-010 remains open for immutable asset serving and runtime-change binary cleanup.
The decision sections explain the contracts; the checklist records the work and remaining gate.

## The short version

Every save inserts a new revision row. Older revisions and their source stay forever. Keep and
reuse their compiled images and debug symbols while valid for the running runtime; cleanup of
obsolete compilation data on runtime changes is future work. A small head row says which revision
is current at each canonical path and whether the page is deleted. Deleting a page marks that head as a tombstone;
saving at the same path again continues the same history. Only an explicit scrub removes history.

Each revision gets an indexed, unique hash based on source, the runtime id at creation and a unique
save identity. Future `/asset/{hash}` requests will look up that exact retained revision and run it.
This plan builds the storage for that lookup, not the asset endpoint or immutable HTTP policy.

The editor gains a listing, generated C# inspection, delete confirmation and scrub confirmation.
Commands use `cmd` in the query string. Existing authentication, antiforgery, buffered rendering,
host-generated ETags and POST/303/GET stay in place.

## Decisions

### 1. Keep editor operations outside the page-key namespace

Use the existing `/edit/{**path}` route with a small, explicit command selector:

| Request | Meaning |
| --- | --- |
| `GET /edit` | Page listing |
| `GET /edit/` | Edit the root page `/` |
| `GET /edit/<path>` | Edit or create that page |
| `GET /edit/<path>?cmd=generated` | Generated source for a saved page |
| `GET /edit/<path>?cmd=delete` | Deletion confirmation |
| `GET /edit/<path>?cmd=scrub` | Permanent history-removal confirmation |
| `POST /edit/<path>` | Existing save operation |
| `POST /edit/<path>?cmd=delete` | Confirm deletion |
| `POST /edit/<path>?cmd=scrub` | Permanently remove the path and all its history |
| `POST /edit?cmd=new` | Validate a typed key, then 303 to its editor |

Do not add `/edit/<path>/generated` or `/delete`: both can already be genuine page keys, and a
catch-all parameter cannot simply be followed by another route segment. Dispatch the bare listing
before the current canonical editor redirect. Preserve `/edit/` as the root editor and pin the
distinction through the real host, including canonical redirects. Unknown, empty or repeated
selectors return 400; absent selectors retain ordinary editing, except bare `GET /edit`.
Reject unsupported method/command combinations without mutation.

All these views and actions require the Editor policy. Every editor response, including redirects,
missing pages and invalid input, sends `Cache-Control: no-store`; set it before dispatch. Every
POST validates antiforgery before reading the form or accessing storage. Keep query selectors on
canonical GET redirects, and use existing URL construction helpers plus HTML attribute encoding
for links and form actions. Query selectors belong to the editor only, not to stored keys.

### 2. Store revisions separately from the current head

Use two tables. Names below describe the intended model rather than prescribing EF type names.

| Table | Data |
| --- | --- |
| `PageHeads` | Canonical `Path` primary key, current revision id, `IsDeleted` |
| `PageRevisions` | Unique revision id, indexed `Path`, unique indexed `Hash`, `SavedAt`, source, creation runtime id, optional compilation result |

The optional compilation result contains the source fingerprint, compilation runtime id, image,
debug symbols and diagnostics currently stored together. A failed compilation retains source and
diagnostics without an image; a compiler fault still preserves the submitted source before rethrowing.
Every accepted save inserts a new revision, even if its source equals the previous revision.

The save transaction inserts the revision and advances its path's head, clearing `IsDeleted`.
Concurrent writes serialize at SQLite's write boundary: the last committed head update wins and
both revision rows survive. Read current pages through the head; never select the newest row by
timestamp, since clocks need not be unique or monotonic. Do not copy source or images into heads.

Define the revision hash as SHA-256 over a versioned, unambiguously framed tuple of source UTF-8
bytes, creation runtime id, UTC save timestamp and a fresh random revision id. Timestamp alone
cannot distinguish identical concurrent saves. Put a unique index on the hash and retry with a new
revision id on an identity collision. This is a revision address, not a rendered-body ETag and not
the nullable compile-cache `ContentHash`. Compute identity through a pure Core helper; supply time
and randomness from the shell. Hashes are public identifiers, not authorization tokens.

The revision's source, path, save time, creation runtime id and hash never change. Optional compile
cache fields can be filled or refreshed on that exact revision without changing its identity.
Conditional compilation writes use the revision id and must not move or recreate a head. They
must fail harmlessly if scrub removed the revision; save/delete races cannot overwrite another
revision's compilation. Keep creation runtime id distinct from the runtime id of a refreshed image.
When a stored image's compilation runtime id matches the running runtime and its source fingerprint
is valid, load and run that image rather than recompiling the source. This applies to historical
revisions too when the future asset endpoint executes them.

Runtime mismatch handling is future work, including whether to recompile and when to discard
assemblies and debug symbols that will no longer be used. Do not retain obsolete binary data merely
because source history is permanent. Any such cleanup must preserve source, revision identity,
creation runtime id and hash; this plan does not introduce a runtime-change cleanup mechanism.

Add the indexed hash-lookup operation to `IPageStore` now, returning a retained revision even when
it is no longer current or its path is tombstoned. It is an internal storage operation, not a new
public endpoint. It reads one indexed row, with no scan, compilation or execution.

Migrate every existing `Files` row into one revision and one live head, preserving its source,
image, symbols and diagnostics. Use the existing compilation runtime id when present; for an
uncompiled legacy row use a documented `legacy:unknown` creation-runtime marker. Give each migrated
row its own revision identity and migration timestamp. Drop the old duplicated source storage only
after the migration has preserved the data. No pruning, deduplication or retention limit.

### 3. List paths, including tombstones

Add a lightweight list query through `IPageStore` and `PageEditor` returning canonical path and
deleted status from heads only. Do not load revision bodies, images, symbols or diagnostics. Sort
ordinally in memory; no pagination or search is needed for the current small collection.

Render the empty state, new-page form, live pages and a distinct deleted section. Live pages link
to the public URL and editor, including pages that do not compile. Tombstones have no public-page
link; offer recreation, history inspection and scrub without loading revision bodies into the listing.
Label unexpected reserved or unaddressable legacy keys unavailable rather than emitting actionable
links. A missing public root stays 404; this is not a public catalogue.

The new-page POST accepts exactly one path, normalizes it with `PagePath`, rejects unaddressable
and reserved keys, and redirects to the canonical editor without creating a row. Existing live
keys open normally. A tombstoned key opens an empty editor, marked as previously deleted; saving
creates a new revision in that path's existing history, not a new independent page identity.
Add listing links to the editor and command views; reuse the existing session flash messages.

### 4. Delete hides; scrub destroys

`DeletePageAsync` atomically tags the head as deleted and returns whether a live page changed.
Keep its current revision pointer and every historical row. Deleting an already-deleted or unknown
path is an already-missing result, not a second deletion record. Ordinary reads and public requests
ignore deleted heads; a subsequent request returns 404, although an in-flight render can finish.
A later save at the same path clears the tombstone and appends to the same history.

Delete's GET confirmation names the encoded key and states that history is retained. It has Cancel
and a token-bearing POST; GET never mutates. POST redirects to `/edit` with deleted or already-missing
feedback. Reject reserved and unaddressable targets. Unknown or deleted confirmation targets return
404. Lazy compilation completing after deletion must never clear the tombstone.

Change startup seeding to distinguish never-created keys from tombstones. Seed `/site.css` and
`/rocket.svg` only when no head exists. A deliberately deleted seeded asset stays deleted across
restart; saving at its old path continues its history. Do not reseed merely because a normal read
returns null. After a scrub there is no head, so a later startup can seed a fresh default again.

Add `ScrubPageAsync` to remove the head and every revision at one canonical path in a single
transaction. It applies to live and deleted pages and returns whether anything existed. The
`cmd=scrub` confirmation must explicitly say that it removes the live page too, cannot be undone,
and is intended for cases such as committed secrets. Require the exact canonical path typed into
the confirmation form as well as a valid antiforgery token. POST redirects to the listing with a
scrubbed or already-missing result. Keep it behind the Editor policy, with no source or hashes in
logs, session messages or metrics. Scrubbing deletes the path's whole history, not one revision.

A plain SQL DELETE is not enough for secret cleanup in SQLite. Enable `secure_delete` on every
connection that can scrub, erase the rows, then checkpoint/truncate the WAL (when enabled) and
verify completion before reporting cleanup complete. This also applies to legacy storage removed
by migration; test the supported journal modes. On an incomplete checkpoint or cleanup error,
report failure and allow a safe retry even when the logical rows have already gone. Coordinate
cleanup with active database operations rather than claiming success while an old reader pins
the WAL. Do not promise forensic erasure from SSDs, filesystem snapshots or external copies.

The confirmation and operational docs must say that scrub does not revoke already-running page
code, browser/CDN copies, exported archives, database backups or filesystem snapshots. Rotate any
exposed secret and clean those copies separately. Concurrent saves after scrub are new writes and
may recreate a page; this is not an edit lock.

### 5. Generate from stored content, not a stored assembly

Read through `PageEditor.GetAsync` and call `TemplateSourceGenerator.Generate(page.Content)`.
Render successful source as an HTML-encoded, read-only `<pre>` using the existing layout. Preserve
newlines and `#line` directives; allow horizontal scrolling without widening the page. A stored
Roslyn failure does not prevent generation when the template parses.

On a parse failure, display the returned `TEMPLATE001` diagnostic using the existing diagnostic
markup and no partial source. A missing page returns 404. Do not call the compiler, renderer or
save path; an inspection request must not execute page code or repair a missing compiled image.
Label this as source generated from the saved template by the current runtime, not necessarily
the exact historical text used for an image compiled by an older runtime.

### 6. Keep everything; measure storage growth

Keep revisions and source indefinitely unless explicitly scrubbed. This does not require keeping
obsolete compiled images or debug symbols forever; runtime-change cleanup is a separate future
decision. Do not add automatic history expiry, pruning, retention settings or a cleanup scheduler
in this work. Disk space is not currently a constraint; reconsider
retention only when page storage is consuming more than half of the database volume's capacity.

The bundled SQLite build does not provide `dbstat`, and the supported APIs cannot attribute
allocated pages, indexes or BLOB overflow pages to one table without adding a dependency. Do not
label an estimate as table allocation. Expose these explicit byte gauges on the existing
`uk.osric.asp` meter, with no path or revision labels:

- `page.storage.payload.size`: logical payload bytes, calculated as UTF-8 bytes for every
  `PageRevisions` TEXT column plus the `Image` and `Symbols` BLOB bytes. It excludes indexes and
  SQLite page/record overhead; it is not allocated table size.
- `page.storage.database.size`: `page_count * page_size` for the whole database, including indexes
  and freelist pages, but excluding the WAL file.
- `page.storage.volume.capacity`: total capacity of the filesystem volume containing the database,
  selected by the longest matching `DriveInfo` root.

Refresh with `PageStorageSampler`: take an initial sample, then sample about every 60 seconds and
coalesce explicit scrub refresh signals. Sampling uses a short-lived database scope and bounded
SQLite operations; observable callbacks return only the cached snapshot and do no I/O during scrape.
On sample failure, log the failure and retain the last values rather than reporting zero. The
payload-to-volume comparison is an operational estimate, not allocated database use; use the
whole-database gauge when assessing actual database growth. Review retention manually when payload
storage exceeds half the volume capacity. Alert delivery and automatic retention remain out of
scope; old revisions are not deleted automatically.

### 7. Asset execution and layout changes come later

IDEA-010 will add something like `/asset/{hash}`: an indexed lookup of a
retained revision followed by normal page execution. It will decide runtime-id mismatch handling,
which pages can promise immutable output, what page code sees as its path, and whether a tombstone
also withdraws historical asset URLs. A revision hash fixes code, not necessarily its output.

This work does not introduce that endpoint, a `v` query protocol, `Link` discovery, new immutable
response headers or changes to `PageLayout`. Existing ETags and unversioned asset behavior remain.

## Implementation Checklist

Each step is a reviewable implementation unit, not an instruction to create a branch or commit.
Use the existing Makefile targets; do not add another task runner. Update these checkboxes as work
lands. New files need the repository's SPDX headers.

### Step 1 - Define revision identity and storage values

- [x] Add a pure revision-hash helper in `Core.Pages`. Pin the format version, length framing,
  UTF-8 encoding, UTC timestamp encoding, revision-id encoding and output casing in tests.
- [x] Add shell values for a retained revision and a lightweight head summary. Keep immutable
  source identity separate from optional `PageCompilation`; a head summary needs no source/BLOBs.
- [x] Supply timestamp and unique save id in the shell, not inside Core. Capture creation runtime
  id even when compilation fails. Preserve the existing source-only `ContentFingerprint` meaning.

**Done when:** `make test-core` proves deterministic hashing for fixed inputs, input framing,
different runtime/source/save identities, and distinct hashes for identical simultaneous saves.

### Step 2 - Migrate storage and make saves append-only

- [x] Add revision/head EF entities and mappings in `Web/Storage`: revision primary key, unique
  hash index, path index, and a head reference constrained to a revision of the same path.
- [x] Generate the migration with `make migration NAME=RetainedPageRevisions`. Backfill every
  `Files` row, including uncompiled and failed pages, before removing the old table. If hash
  backfill needs a SQLite function registered by the shell, wire it into every migration entry
  point, including tests and CLI startup; do not replace the identity algorithm with SQL concatenation.
- [x] Change `IPageStore` and `SqlitePageStore` to append one revision and upsert its head in one
  transaction. Return/read persisted revision identity; add single-row indexed hash lookup.
- [x] Update `StoredPage`, `PageEditor` and every test store/fake affected by the new contract.
  Keep compile-before-save behavior: store exactly one revision with a result, or one source-only
  revision on a compiler fault before rethrowing. Do not append again to attach compilation.
- [x] Add migration and storage tests in `SqlitePageStoreTests`; test two identical saves and
  concurrent writers, and enforce the head/revision path invariant at the database boundary.

**Done when:** `make test-web` proves migration preserves all fields, both concurrent revisions
survive, the head names one committed revision, and older hashes still resolve after later saves.
Run `make test-integration` to prove migration and startup work against the real host.

### Step 3 - Bind lazy compilation to the revision read

- [x] Replace `TrySetCompilationAsync(path, expectedContent, ...)` with a revision-addressed
  conditional update. It may change compile-cache fields only, never the head or revision hash.
- [x] Update `PageRenderer.LoadAsync`: reuse matching-runtime images and cached compile failures;
  retain current lazy-recompile behavior for ordinary pages with missing/stale compilation.
  Historical asset runtime policy remains deferred, not a change to ordinary serving today.
- [x] After a lazy compile is stored, recheck the live head before choosing what to execute if it
  changed during compilation. Preserve bounded retries; never execute the old fallback after
  observing deletion or scrub. An invocation already started may finish.
- [x] Extend `PageRendererTests` and `PageEditorTests` for same-source saves with distinct revision
  ids and a head changing mid-compile. Prove matching-runtime reuse with a counting compiler.

**Done when:** `make test-web` and `make test-integration` pass existing compile-cache coverage,
and a late compile updates only its own retained row without replacing newer content.

### Step 4 - Add the editor command dispatcher and listing

- [x] Dispatch the exact bare `/edit` listing before canonical root-editor handling. Parse `cmd`
  once; reject empty, repeated, unknown and unsupported method/command combinations with 400.
- [x] Apply `no-store` before editor dispatch. Keep Editor authorization on all routes and
  antiforgery validation before every POST reads a form or calls the store.
- [x] Add the head-summary query and `PageEditor` forwarding, ordinal listing, empty/deleted
  sections, listing navigation, and the `cmd=new` form/303 action. No row is saved by navigation.
- [x] Extend endpoint unit tests for encoded keys, invalid input and command parsing. Extend
  integration `EditorEndpointTests` for `/edit` versus `/edit/`, canonical query preservation,
  root commands and actual page keys ending in `generated`, `delete` or `scrub`.

**Done when:** `make test-web` and `make test-integration` prove listing/new-page behavior,
real authorization/antiforgery rejection, and no regression to root-page editing or ordinary saves.

### Step 5 - Tombstone deletion and preserve path history

- [x] Add store/editor operations for tombstoning and distinguishing absent, live and deleted
  heads. Ordinary reads hide tombstones; hash lookup still returns retained revisions.
- [x] Add `cmd=delete` confirmation and POST/303 feedback. Mark the recreation editor as deleted
  without loading old source into its textarea. Saving appends a revision and clears the tombstone.
- [x] Change `PageAssetSeeder` to check head existence rather than ordinary page reads, so deleted
  assets stay deleted. Test never-created seeding, preserved edits and deletion across restart.
- [x] Add storage, endpoint and real-host tests for delete/recreate and late compilation after
  deletion. Assert old hashes survive and no conditional compile can clear a tombstone.

**Done when:** `make test-web` and `make test-integration` prove public 404 after deletion,
retained history, shared history on recreation, and no seeded-asset resurrection on restart.

### Step 6 - Add explicit scrub and verify SQLite cleanup

- [x] Implement transactional removal of the head and all path revisions. Enable secure deletion
  on relevant connections and implement bounded WAL checkpoint/truncate with checked results.
  Run required post-commit cleanup on retries even when the path is already logically absent.
- [x] Add tests against the bundled SQLite in supported journal modes, using a distinct secret
  marker in source and binary fields. After successful cleanup and closing connections, assert
  the marker is absent from DB/journal/WAL files; test a pinned reader causes failure, not success.
- [x] Add `cmd=scrub` confirmation and POST with exact-path confirmation, Editor authorization,
  antiforgery, no-store and clear success/failure messages. Never log the removed content.
- [x] Test live/deleted/missing scrub targets, rollback, cleanup retry, hash lookup after scrub,
  late compilation and seed behavior. Document secret rotation and external-copy limitations.

**Done when:** `make test-web` and `make test-integration` prove full logical removal,
successful supported SQLite cleanup and honest reporting of incomplete cleanup. Scrub must not
claim backup, cache or forensic media erasure.

### Step 7 - Add the generated C# viewer

- [x] Link the saved page editor to `cmd=generated`. Read current saved source and call
  `TemplateSourceGenerator.Generate`; HTML-encode the result in a horizontally scrollable `<pre>`.
- [x] Show parse diagnostics without partial output; return 404 for absent/deleted pages.
  Label the view as current-runtime generation, excluding unsaved edits.
- [x] Prove compiler, renderer and storage writes are not called. Cover encoded markup, line
  directives, parse failures, no-store and real authorization in the existing endpoint suites.

**Done when:** `make test-web` and `make test-integration` pass. This view generates text only;
it does not change assembly reuse during execution.

### Step 8 - Measure storage growth

- [x] Verify that the bundled SQLite does not provide `dbstat`; document supported logical payload,
  whole-database and filesystem-capacity metrics without presenting estimates as table allocation.
- [x] Add bounded background sampling with a short-lived DB scope, startup sample and coalesced
  refresh after scrub. Expose cached `page.storage.payload.size`, `page.storage.database.size` and
  `page.storage.volume.capacity` gauges in bytes, with no revision/path tags or I/O in callbacks.
- [x] Test payload accounting, scrape without DB access, failed samples and cancellation. Add
  exporter coverage in `MetricsEndpointTests`; document the greater-than-half-volume review
  threshold, with no automatic pruning or alert-delivery work.

**Done when:** `make test-web` and `make test-integration` validate samples and exported gauges.

### Step 9 - Finish documentation and run the gate

- [x] Update architecture and testing notes for revision identity, transactions, commands and
  compile-cache behavior. Update deployment notes for storage metrics, migration backup, scrub
  limitations and tombstone-aware seeding. A database backup is an operator action before migration;
  no production backup is performed as part of this work.
- [x] Close ARCH-06, ARCH-10 and ARCH-11 following the jobs index's closed-job convention. Leave
  ARCH-07 open for revision restore and IDEA-010 open for `/asset/{hash}`, runtime-change binary
  cleanup and PageLayout adoption.
- [ ] Run `make check`; fix only failures attributable to this work and report unrelated blockers.

Validation on 2026-11-03: formatting, package audit, all 670 tests (169 Core, 48 Runtime,
380 Web, 73 integration) and Release publish passed. `make check` stops at `make license-check`
because the `reuse` executable is not installed; the remaining gate targets were run separately.

**Done when:** the gate passes and the implementation has no asset endpoint, new immutable-header
policy, history UI, automatic retention or runtime-change cleanup hidden inside this scope.

## Verification and acceptance

| Layer | Checks |
| --- | --- |
| Core | Framed hash inputs cannot alias; source/runtime/save identity affect hash; identical source and timestamps with different revision ids remain distinct |
| Web storage | Migration preserves source, images, symbols and diagnostics; repeated and concurrent saves retain distinct rows; head updates are atomic; old hashes still resolve; compilation updates only their revision |
| Delete and scrub | Tombstone hides current page but retains rows; recreation shares path history; missing delete is harmless; scrub removes all revisions and hash lookups for live/deleted paths; cleanup failure and safe retry; racing compilation cannot resurrect data |
| Web endpoints | Encoded keys/source; lightweight ordinal listing with tombstones; new-key validation; `cmd` validation; read-only confirmations; scrub requires typed path; parse diagnostics without partial source or execution |
| Real host | `/edit` versus `/edit/`; page keys ending in command names remain editable; query-preserving canonical redirects; Editor policy and antiforgery cover every command; delete gives 404; recreation serves new content |
| Seed lifecycle | Never-created assets seed once; tombstoned assets stay deleted across restart; edited assets survive startup; scrub allows fresh seeding |
| Storage telemetry | Logical payload bytes include all revision text and image/symbol blobs; database bytes include indexes/freelist but exclude WAL; volume capacity is available; scrape callback does no I/O; no high-cardinality labels; sample failures retain the previous values |
| Regression | Existing compile caching, ETags, GET/HEAD/304 and ordinary query behavior remain unchanged; no asset endpoint or immutable-header changes |

Run `make test-core`, `make test-web` and `make test-integration` as their slices land; finish with
`make check`. No browser suite is needed to prove these contracts. For this documentation-only
planning change, check local Markdown links, patch whitespace and `make license-check`.