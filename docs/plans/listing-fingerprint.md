<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A listing carries the fingerprint of the list it is a slice of

| | |
| --- | --- |
| Status | Proposed 2026-11-20 — waits for the owner. Nothing below is built |
| Size | Small: one pure function in `Core`, one read in `Web`, two members on the page contract, one tool argument and the documents. No migration, no configuration value, no write path |
| First customer | The assistant's `list_pages`, which already pages with `after`. The second is [`cannot-search-the-store`](../backlog/cannot-search-the-store.md), blocked by this item, whose cursor takes the same token |
| Spike | None. Every claim about what exists was read off the tree on 2026-11-20: `IPageReadStore`, `SqlitePageReadStore`, `PageCatalogScope`, `PageListing`, `PageListingRequest`, `PageStoreReader`, `IPageStoreReader`, `PageSeedInstaller`, `AdoptSeedAsync`, `PageContentHashRepair`, `SqliteJournalMode`, `PageAuthorPrompt` |
| Decisions | None recorded yet. Two are named at the merge (`product-plan-before-building`); this plan states both |
| Backlog | [`listing-does-not-date-itself`](../backlog/listing-does-not-date-itself.md) (this job), [`cannot-search-the-store`](../backlog/cannot-search-the-store.md) (the second consumer) |

## The short version

A listing resumes from a key (`pages-a-listing-resumes-from-a-key`), so a caller can walk a store of
any size one slice at a time. What it cannot do is tell whether the thing it is walking changed
between two slices: a page saved under the cursor is skipped, a page saved above it is seen twice, a
delete removes a row the caller was about to reach, and every one of those is silent.

The item's option 2 answers that **per prefix**: a walk of `/blog` is not invalidated by an edit under
`/assistant`, which is the common case on this site — the assistant writes while the owner reads. The
owner chose that semantics on 2026-11-20 and, when the mechanism was put to them the same day, chose
to **derive the answer from the heads rather than keep a change sequence beside them**.

So: **a slice answers a fingerprint of every head under its prefix, computed when it is read, and a
request carries the fingerprint the caller started from.** Four things follow, and they are the
reason this is small:

- **There is nothing to forget.** No column, no counter, no trigger and no bump: the fingerprint is a
  function of rows that are already stored. The item's own fear — "a write that forgets is worse than
  no token, because it is a token that says safe" — cannot happen, because no write participates.
- **It counts the two writes a kept sequence would most likely have missed.** `AdoptSeedAsync` writes
  only `SeedHash` and `PageContentHashRepair` only `ContentHash`; both change what a listing row says
  and neither looks like a save. A derived fingerprint covers them without being told.
- **It is exact rather than merely safe.** A page edited and edited back leaves the fingerprint where it
  was, and the caller's list really is still the list. A monotone tick would cry wolf.
- **The caller decides.** The host answers `Changed` and never refuses; the token is opaque and is
  compared with `==` and nothing else.

The cost is one read of the prefix's heads per slice — measured at about 1.15 ms for 300 heads against
310 µs for a 50-row slice, and dominated by reading the rows rather than by hashing them. §8 has the
figures, the comparison against keeping a sequence on write, and the measured threshold at which the
shape would stop being right.

## What the owner answered

Put to them on 2026-11-20, before this plan was written, and answered as the recommendation:

1. **A fingerprint derived from the heads**, not a stored per-head change sequence and not per-prefix
   aggregate rows. The token stays opaque, so a later step can swap the derivation for a stored
   aggregate without the page contract moving.
2. **Head rows only.** What a row says about its compiled image — `State` and `Libraries`, which come
   from the revision's image columns — is outside the token (§4).
3. **A warning, never a refusal.** `PageListing.Changed` says the fact; a walk may carry on, restart
   or ignore it, and the host does not decide.
4. **An opaque string on the contract.** No number, no order and no meaning beyond equality.

Two of the item's own open questions were answered by the shape rather than by the owner, and are
recorded here so they are not reopened: a **delete is a change** — a tombstone is a head row whose
`IsDeleted` flips, and a scrub removes the row, so both move the fingerprint; and the token **is not a
security claim** — it names no key and no content, only that the set of rows under a prefix differs
from the one the caller saw, and a listing is already as public as the page that asks for it
(`pages-reading-the-store-is-public`).

## The names this plan intends

Taken at the merge — bold rather than in backticks, because `make decisions` fails on a cited name the
record does not have yet:

- **pages-a-listing-carries-a-fingerprint** — a slice answers an opaque token that fingerprints every
  head under its prefix, read from the same snapshot as its rows; a request carries the token the
  caller started from and the answer says whether the two differ. A warning, never a refusal, and the
  token is compared for equality and nothing else.
- **pages-a-fingerprint-is-derived-not-kept** — the token is computed from the heads, so no column, no
  counter and no write path maintains it, and a head write nobody remembered to annotate still moves
  it.

They are two rules rather than one because they can be broken separately: a later step may keep the
first and replace the second with a stored aggregate, and must not silently break "a warning" while
doing it. If the merge reads them as one subject, they fold into one entry and the plan cites it once.

## What exists today

- **The seam.** `IPageCatalog.ListAsync(PageListingRequest)` asks for a prefix, a cursor and a limit and
  answers `PageListing(Entries, Truncated)`. `PageCatalogScope` normalises the three arguments, reads
  one row past the limit so `Truncated` needs no second query, and maps the store's small records onto
  the SDK's values. `Pages:Read:MaxRowsPerList` (500 shipped) is enforced in the statement.
- **The store read.** `IPageReadStore.ListAsync(prefix, after, limit)` joins `PageHeads` to
  `PageRevisions`, filters a subtree with `Path == prefix OR Path LIKE prefix/%` (escaping `%`, `_` and
  `\`), orders by key and takes the limit. Each call opens its own `StorageDbContext`, for the reason
  the seam exists: a page's task can outlive the request. There is no transaction today, and the seam's
  remark says so.
- **What a row carries.** `PageHead` holds the key, the revision id, `IsDeleted`, `SeedHash` and
  `ContentHash`; `PageRevision` holds the hash, the saved time, the source and the image columns. A
  listing row's `Hash` is the revision's, and `RevisionHash.Of` folds the source, the runtime id, the
  saved time and the revision id into it, so `RevisionId` and `Hash` identify the same revision.
- **The judgement it already makes.** `PageCatalogScope.Describe` decides a row's seed verdict from
  `SeedHash`, the shipped seed and `ContentHash`, and `PageEntry.State` from the revision's image
  columns and the running build — the two facts §4 has to separate.
- **The assistant.** `list_pages` reads `IPageStoreReader.TreeAsync`, which walks every head through
  `PageEditor.ListAsync` and builds keys, folders and shell files in memory, then filters to the prefix
  and applies the cursor and the limit in memory. It has no token and no store read of its own.
- **WAL.** `SqliteJournalMode` sets and verifies `journal_mode=wal` at startup, so a reader never blocks
  the one writer and a reader's transaction never blocks a commit.

## Design

### 1. The fingerprint

`Core/Pages/PageSubtreeFingerprint.cs` — a pure function over immutable values, which is where
`TemplateFingerprint` already sits and for the same reasons:

```csharp
public readonly record struct PageFingerprintFact(
    string Key, string RevisionId, bool IsDeleted, string? SeedHash, string? ContentHash);

public static string Of(IEnumerable<PageFingerprintFact> facts);
```

The five fields are what a listing row says, minus its compiled image: the key orders the set,
`RevisionId` is the row's identity and fixes its `Hash` and `SavedAt`, `IsDeleted` is the tombstone, and
`SeedHash` with `ContentHash` is every input to the row's `Seed` sentence.

The canonical form is a netstring per field — a decimal UTF-8 byte count, a colon, then the bytes — with
the facts sorted by key with `StringComparer.Ordinal`, `IsDeleted` written as `1` or `0`, and an absent
hash written as an empty field. The answer is the lowercase hexadecimal SHA-256 of that byte sequence;
the empty set is the hash of no bytes, which is a stable token in its own right. Netstring rather than a
separator because a key and a hash are both text and no separator is guaranteed absent from one; the
test that proves the form is unambiguous is under Tests.

The algorithm carries no marker, following `TemplateFingerprint`: the value is compared with a freshly
computed one and never parsed. Changing the formula would make every token different, which matters only
to a walk that spans the deployment, and that walk gets a warning — the safe direction.

### 2. The read

`IPageReadStore` answers the rows and the token **together**, so the seam cannot answer one without the
other:

```csharp
internal sealed record PageReadSlice(IReadOnlyList<PageReadHead> Rows, string Version);

Task<PageReadSlice> ListAsync(string prefix, string? after, int limit, CancellationToken cancellationToken = default);
Task<string> VersionAsync(string prefix, CancellationToken cancellationToken = default);
```

The item's rule — "the version has to be read in the same transaction as the rows, or the answer names a
version its own slice did not come from" — becomes a property of the shape rather than a discipline. The
two statements run inside one deferred `BEGIN` on one connection: in WAL the snapshot is taken at the
first statement and held for the two, and nothing is pinned beyond them. A read that never writes is not
a write, so the seam stays read-only; its remark gains a sentence saying what the transaction is for
rather than losing the one that says there is none.

`VersionAsync` is the same statement without the join and the limit, for the caller that reads sources
instead of rows: the search walk of [`cannot-search-the-store`](../backlog/cannot-search-the-store.md)
reads a scope's sources in slices and needs the token before and after, never the heads.

The subtree predicate — `Path = prefix OR Path LIKE escaped(prefix) + '/%'` — is currently private in
`SqlitePageReadStore` with a comment explaining why `%` and `_` in a key must not become wildcards. It
moves to one small internal helper both statements use, and the search reuses, so the escape cannot be
written twice and drift.

### 3. The contract

Both records are young and unshipped, and neither needs a deprecation:

```csharp
public sealed record PageListing(IReadOnlyList<PageEntry> Entries, bool Truncated) {
    /// The fingerprint of every head under the prefix, read with these rows.
    public string Version { get; init; } = string.Empty;

    /// Whether the store's fingerprint differs from the one the request carried.
    public bool Changed { get; init; }
}

public sealed record PageListingRequest(
    string Prefix = "", string? After = null, int? Limit = null, string? Version = null);
```

- **`init` properties on the answer rather than positional parameters**, so the two-argument constructor
  every existing caller uses — `PageCatalogScope`, both fakes, the tests — keeps compiling and keeps
  reading. The request gains a fourth optional parameter, which does the same for `new()` and
  `new("/blog")`.
- **`Changed` is computed by the host**: `request.Version is not null` and not equal, ordinally, to the
  token just read. A first call carries none and is never "changed".
- **A token from another prefix or another walk is a warning, not an error.** There is nothing to
  validate: any string that is not the one read answers `Changed: true`, which is a false alarm and
  never a false reassurance. It is not an `ArgumentException`, because a page bug that produces a stale
  token should not become a 500.
- **The doc comments say what the token is not**: not a number, not ordered, not parsed, not a claim
  about a key, not a version in the `?v=` sense. One line, in the words a page author will read.
- `PublicAPI.Unshipped.txt` gains the two properties and the parameter; the analyzer fails the build
  otherwise.
- `PageCatalogScope.ListAsync` reads one slice, maps the rows as it does now, and returns
  `new PageListing(entries, truncated) { Version = slice.Version, Changed = ... }`. `ArgumentNullException`
  and the limit refusal are untouched.

### 4. What the token does not count

A row's `State` and `Libraries` come from the revision's image columns, and a lazy compile fills them
without writing a revision; a deploy changes the running id, so every row's state becomes stale without
a row moving. Counting them would make every walk warn about the compile cache rather than about the
list, and would make the token a second statement of a fact `pages-a-listing-carries-facts` already
settles per row.

So the token answers **one question: is this the same set of rows, saying the same things about their
content and their seed anchor**. A compile can still change what one row says about its image, and a
caller that cares re-reads the row. That is a stated limit, in the manual as well as the doc comment,
because it is the one place a caller could reasonably expect more.

### 5. What the caller does with it

The host answers and does not decide. A tree redraws and carries on, an editor refuses to continue, a
one-shot list ignores the token, and the assistant re-walks. The cursor is still a keyset, so a moved
store does not stop a walk working — it stops the walk promising completeness
(`pages-a-listing-resumes-from-a-key`), which is exactly the fact `Changed` reports. A caller may also
*rebase*: adopt the token it just read and carry on from the cursor it has. Both are reasonable and the
host does not choose between them.

### 6. The assistant

`list_pages` gains an optional `version` and answers `version` and `changed` beside `pages` and
`truncated`, so a run can notice that a list it is working through moved. It is a walk-level warning;
the per-page guard stays `edit_page`'s expected hash, which is the sharper tool for one page.

`IPageStoreReader` gains one member — the token for a prefix — implemented in `PageStoreReader` by
delegating to `IPageReadStore`, which `ToolServices` resolves from the call's scope as it resolves
everything else. The tool reads the token **before** the tree, so the rows it prints can only be newer
than the token it names, and a change that lands between the two reads cannot be one the answer calls
unchanged. The token is a store fact, so it covers tombstones whether or not `includeDeleted` was
asked for: a caller that flips that flag mid-walk must not see a warning about its own filter.

The tool's description and the author prompt's section on paging both gain a sentence, so
`PageAuthorPrompt.Version` moves from `page-author-v54`.

The transcript's changed-pages card (`AssistantChangedPagesTests`) is a different fact — what a turn
wrote, read from its own tool results — and is not touched.

### 7. Search, the second consumer

The search walk needs exactly one store fact this plan adds: `VersionAsync(scope)` read at the start of
the walk and compared at each slice, with a difference restarting the scan. So
[`cannot-search-the-store`](../backlog/cannot-search-the-store.md) is unblocked by this item without
adding store work of its own, and its sketch's point 5 — "the request carries the version token … and
the answer carries the version it read" — is satisfied here. Its scope is a prefix like a listing's, so
the per-prefix semantics is what it wanted rather than the store-wide counter it would otherwise have
had to accept.

### 8. Cost, measured

Deriving on read against keeping a sequence on write is the one argument worth measuring before writing
code, so it was measured first — this machine, the provider the store uses (`Microsoft.Data.Sqlite` in
WAL, a connection per call), a table shaped like `PageHeads` and `PageRevisions` with the same indexes.

| The read, per listing call | 300 heads under the prefix | 1,000 | 5,000 |
| --- | --- | --- | --- |
| One 50-row slice — the join it does today | 310 µs | 290 µs | 280 µs |
| The heads scan alone, no hash | 955 µs | 2.15 ms | 10.5 ms |
| The fingerprint — scan, canonical form, SHA-256 | 1.15 ms | 2.07 ms | 12.4 ms |

| The write, per save | |
| --- | --- |
| As today — one revision and one head | 383 µs |
| Plus a change row and its counter (the item's option 2) | 487 µs, +104 µs |
| Plus one row per ancestor and its counter (option 3) | 583 µs, +200 µs |

Three things follow, and none of them is the intuition:

1. **The hash is about a tenth of it.** At 300 heads it is 955 µs to read the rows and 200 µs to
   serialise and hash. The cost is SQLite row marshalling, not CPU cycles, and a stored sequence pays it
   too.
2. **Option 2's stored sequence does not read in O(1).** `MAX(ChangeSeq)` over a subtree is the same
   subtree scan over one column instead of five — maybe half the fingerprint's cost, not a tenth of it.
   A read that really is one lookup needs a row per prefix, which is option 3: +200 µs inside every
   save, a table, a counter, and an ancestor walk that no trigger can perform.
3. **The writer is the scarce side here.** WAL makes a reader free and the one writer a queue: every
   save is `BEGIN IMMEDIATE` and the store is built to keep a commit to statements
   (`pages-writes-are-transactions`). A millisecond on a read costs nothing anyone queues behind; a
   hundred microseconds inside every commit costs the one resource that has contention.

So the derived answer roughly doubles the store work of the listing it accompanies: a whole-store
listing at 300 pages goes from about 1 ms to about 2 ms. At 5,000 heads the token alone is 12 ms and
that is where the shape stops being right; at the configured maximum of 10,000 heads it is worse still.
`code-n-is-small` says not yet, and the escape hatch is written rather than built — the third
alternative under *Considered and rejected* keeps its shape and its opaque token, so taking it later is
not a contract change.

Step 1 therefore ends with the measurement repeated against the largest store the suite can build, on
the whole-store listing rather than a synthetic table: **if the token more than doubles that listing,
the step stops and option 3 is taken instead, under this plan's contract.** That is the honest form of
"optimise what a measurement shows": take the measurement where the code lives, and write the threshold
down before it can be argued about.

Two things this settles for the implementation: the read projects the head rows into the fact struct
rather than materialising `PageHead` entities (the figures above are a raw read, and entity
materialisation would add to the dominant term), and no covering index is added — a five-column index
on `(Path, RevisionId, IsDeleted, SeedHash, ContentHash)` measured *slower* than the primary-key scan it
would have replaced.

## Seeds: what does not change, and the two writes that prove it

The owner asked, when the sequence was first sketched, whether adding change sequences to everything
would change how seeds are compared and reconciled. The answer is no, and the reason is worth recording
because the temptation is real:

1. **The verdict stays three hashes.** `SeedComparison.Of(R, S, H)` decides whether a page still holds
   the seed it was installed from and whether the release has moved it. It is a question about *content
   identity*; a fingerprint is a question about *a set of rows*. Every case the seed rule exists for
   would be misread by an ordering token: a restore writes an old source as a new revision (a new tick,
   the same content), an adopt writes no content at all, a save that puts the seed's text back is a new
   revision with the same bytes, an import of identical source is new rows with the same hash. Under a
   tick each of those is "changed"; under the hashes each is exactly what it is, and the automatic write
   rule — **H = R and S ≠ R** (`pages-a-seed-write-records-its-hash`) — keeps the fact it is built on.
   The verdict is the host's and the words are still `SeedVerdictWords`'
   (`pages-a-seed-verdict-is-the-hosts`).
2. **The fingerprint counts a seed write for nothing.** Install, restore and the start applier all go
   through `StoreRevisionAsync`, so they write a revision, the head's `RevisionId` moves and the token
   for every prefix above the key moves with it. Nothing had to be annotated.
3. **And it counts the two writes a kept sequence would have to be told about.** `AdoptSeedAsync`
   updates only `SeedHash`, which is what turns a row from *unknown* — or from *store newer* — into *in
   sync*; `PageContentHashRepair` updates only `ContentHash`, which is what decides whether a legacy row
   with no anchor may be adopted at all. Neither writes a revision, neither moves a head pointer, and
   neither looks like a save from the
   outside; a change sequence maintained in the write paths would have had to name both, and the two
   that are easiest to miss are exactly the two that touch the seed anchor. Because the token is
   computed from the row, they are covered and there is no test to forget.
4. **Nothing in the seed pipeline reads the token.** The start applier plans the whole pack on every
   start and remembers nothing, by design (`pages-startup-applies-the-release-seed`); a token could only
   let it skip work if it remembered the previous start's token, which is the marker that decision
   deliberately does not have — and it would be wrong besides, because a release that moves seed *text*
   moves nothing in the store. The seed page's plan is still one head read per seed.
5. **A token cannot help a conflict say which side moved.** The store side has `SavedAt`; the release
   side has no time we trust, and the image deliberately records none. So the verdict's sentence stays
   as it is.

One thing the work made visible and does not fix: `PageSeedInstaller.InstallAsync` writes the seed with
`SaveSeedAsync`, which is unconditional, while `SaveSeedIfRevisionAsync` exists and has no caller in
`src`. A save that lands between a seed page's plan and its install is overwritten. That is a hole of
its own — the fix is the conditional save the editor already has — and it wants an item rather than a
line in this plan.

## Steps

Two steps, each its own branch and its own stop for the owner.

**Step 1 — the fingerprint and the contract.** `PageSubtreeFingerprint` in `Core` with its tests; the
slice and `VersionAsync` in `IPageReadStore` with the shared subtree helper and one read transaction;
the two SDK members and `PageCatalogScope`; `PublicAPI.Unshipped.txt`. This step is one contract change
and lands as one. It ends with §8's measurement repeated on the whole-store listing against the largest
store the suite builds: if the token more than doubles that listing, the step stops here and reports
that option 3 should be taken instead — the owner decides, and the contract does not move either way.

**Step 2 — the assistant and the manual.** `IPageStoreReader.VersionAsync`, the `list_pages` argument
and answer, the tool description, the author prompt and its version bump, the `/help` section, and the
documentation below. It depends on step 1 and on nothing else.

The backlog item moves to `planned`, pointing at this plan, in step 1's branch; it closes when this
plan's work merges, and the merge is also what takes the two names.

## Tests

Every test is NUnit and names the behaviour; anything a unit test can prove does not go to a browser.

`test/uk.osric.asp.Core.Tests/PageSubtreeFingerprintTests.cs`

- An empty set answers the token of an empty set, and answers it again.
- The same facts in a different order answer the same token — the function sorts.
- A tombstone, a changed revision id, a changed seed anchor and a changed content hash each answer a
  different token from the same set.
- Two different sets cannot produce the same byte sequence: a fact whose key runs into the next field's
  text (`/a` with hash `bc` against `/ab` with hash `c`) is a different token.

`test/uk.osric.asp.Web.Tests/SqlitePageReadStoreTests.cs`

- A slice answers the token of its prefix and the rows it read.
- The token covers the whole prefix, not the slice: a save at a key past the limit moves it.
- A save outside the prefix leaves a prefix's token where it was — the property option 2 exists for.
- A delete, a scrub, a move in or out, an adopt and a content-hash repair each move the token.
- A prefix containing `%` or `_` still selects a subtree and not a pattern.
- The existing "the store never writes" test still passes: a deferred read transaction writes nothing.

`test/uk.osric.asp.Web.Tests/PageCatalogScopeTests.cs`, over `FakePageReadStore`

- A first call answers `Changed: false` and the token it read.
- A call carrying that token answers `Changed: false` while the store has not moved, and `true` when it
  has.
- A token that was never read — another prefix's, or nonsense — is `Changed: true` rather than an
  exception.

`test/uk.osric.asp.Web.IntegrationTests/PageCatalogTests.cs`

- A page paging twice with the token sees no change; a save under the prefix turns the second answer's
  `Changed` on; a save under another prefix does not.

`test/uk.osric.asp.Web.Tests/PageToolExecutorHistoryTests.cs` and `PageToolExecutorTests.cs`

- `list_pages` answers `version` on every call and `changed: false` when the token it was given was the
  store's; `true` when a page under the prefix moved between the two calls.

## Documentation

- **The manual**, `/help` (`product-manual-is-a-page`): the listing paragraph in "Reading the page
  store" gains the token, the two members of `PageListingRequest` and `PageListing` it involves, the
  fact that it is compared and never parsed, and the stated limit of §4.
- **[pages](../pages.md)**: the `IPageCatalog` listing paragraph, beside the cursor's.
- **[storage](../storage.md)**: the read-seam paragraph — the token is derived, the two statements share
  one snapshot, nothing is stored and no migration is involved — and a line in the seed section that a
  seed write and an adopt are both visible to it while the verdict stays three hashes.
- **[decisions](../decisions.md)** at the merge: the two names above, each with its "Recorded in" line.
- **The backlog**: this item's `Plan` and `Status`, and the item that waits behind it —
  [`cannot-search-the-store`](../backlog/cannot-search-the-store.md), unblocked by the merge.
- **The author prompt**: the paging section, with `PageAuthorPrompt.Version` moved on.

## Risks

1. **Cost grows with the subtree, once per slice.** Measured at about 1.15 ms per listing for 300 heads
   and 12 ms for 5,000, mostly in reading the rows. A page that walks in tiny slices multiplies it. §8
   states the numbers, the threshold and the shape that would replace it.
2. **A canonical form that is ambiguous would collide.** One pure function, in `Core`, with the tests
   above, and no second spelling of it.
3. **A token invites being ordered or parsed.** The type is a string, the doc comments say equality,
   and the manual says it — the same three places `?v=` hashes are described.
4. **It reports "the same list", not "nothing happened".** An edit edited back is silence. That is the
   more useful answer for a pager and the wrong one for an audit, and *What this does not close* says
   why an audit is a different item.
5. **The assistant's token and its rows are two reads.** Reading the token first makes the rows the
   newer of the two, so a change can never be hidden behind the token it came from; the answer still
   says the token is the store's at the moment it was read.

## Considered and rejected

1. **A Merkle tree over the keys** — the owner's first instinct, and the right one about the aggregate.
   A tree buys what nobody here uses: a proof that one key is in the set, and an update in `log n`. The
   host computes the answer and the caller compares it, so the tree's interior nodes are cost without a
   customer — which is why the aggregate is derived in one pass rather than kept (§8 names the version
   of it that would be worth keeping).
2. **A stored `ChangeSeq` per head, and `MAX` over the subtree** — the item's sketch. It is **not the
   cheap read it sounds like**: the maximum is a scan of the same subtree, one column wide instead of
   five, so the measured saving is hundreds of microseconds, not the millisecond the fingerprint costs.
   On top of that it is a column, a global counter, a migration and a bump in every head write, costing
   about 100 µs inside every commit; it *falls* when the largest row leaves the subtree, so a move or a
   scrub would need change rows that pruning may not remove, which is at odds with what a scrub means;
   and the writes it is most likely to miss are the two under *Seeds*. Rejected on all four counts.
3. **A high-water mark per prefix (the Merkle interior nodes)** — the one that really does read in a
   lookup (about 10 µs), at about 200 µs inside every save, one row per ancestor, a counter, and an
   ancestor walk no trigger can perform. Monotone by construction and the right shape above a few
   thousand heads, which this store does not have (`code-n-is-small`). It keeps the same opaque token,
   so it is a later step behind the contract rather than a redesign.
4. **A store-wide version alone** (the item's option 1) — an edit under `/assistant` would invalidate a
   walk of `/blog`, and the site's own traffic is the counter-example. It is also still available: the
   root is a prefix like any other.
5. **A snapshot held for the life of a walk** — the item's own rejection, unchanged: it pins a reader,
   its pages and its WAL for the length of a browser tab's walk.
6. **The count, the largest `SavedAt`, or `PRAGMA data_version`** — the item's own rejections, unchanged:
   a delete changes neither of the first two, and `data_version` is per connection and moves for other
   stores.
7. **A `ChangeSeq` on `PageEntry` as well** — `PageEntry.Hash` is already the per-row answer to "which
   revision is this", and the compiler's staleness test already compares it. A second per-row fact
   would be a second spelling.

## What this does not close

- **A search cursor's behaviour.** Its own item; this one gives it the token and nothing else.
- **The seed install's race.** `PageSeedInstaller.InstallAsync` is unconditional while
  `SaveSeedIfRevisionAsync` exists unused; a save between plan and install is overwritten. Noticed
  here, belongs to an item of its own.
- **"Has anything happened since T".** An audit or a change feed is a record of the store's history, not
  an equality test of its present, and this keeps no record of when.
- **A row's `State` and `Libraries`** — outside the token by §4, and unchanged per row.

## Not in scope

- **A refusal, or a `Strict` flag** — the owner chose a warning; a caller that wants to stop can stop.
- **A per-prefix aggregate index** — §8's escape hatch, taken only if the measurement says so.
- **The token as an ETag.** It is a canonical hexadecimal string and would carry `If-None-Match` well;
  a listing is not a cacheable response today, and that is a decision about the shell's responses
  rather than about the token.
