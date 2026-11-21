<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A listing answers the store's tree

| | |
| --- | --- |
| Status | Proposed 2026-11-21 — waits for the owner. Nothing below is built |
| Size | Moderate: one enum and a reshaped row on the page contract, one request member, one pure fold in `Core`, one predicate and one narrow read in `Web`, the capability, one seed, the manual, the prompt and the documents. No migration, no configuration value, no write path |
| First customer | The shell's `ls`, `find -type d` and `stat`, and the IDE's T8 tree — both page work, and both after [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md). The first caller that has to *not* see the new rows is the editor's own page list, which moves in the same step |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-21: `IPageCatalog`, `PageEntry`, `PageListing`, `PageListingRequest`, `PageCatalogScope`, `IPageReadStore`, `SqlitePageReadStore`, `PageReadOptions`, `PageTree`, `PageStoreReader`, `ReservedPaths`, `ShellCatalogue`, the `/pages` and `/help` seeds, `PageAuthorPrompt`, and the suites named under Tests |
| Decisions | Three names are stated here and taken at the merge (`product-plan-before-building`); one entry is edited in place because it stops being true, and two gain a clause |
| Backlog | [`a-page-cannot-list-folders`](../backlog/a-page-cannot-list-folders.md) (this job); it is one of the three blockers of [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) |

## The short version

`IPageCatalog.ListAsync` answers the heads under a prefix: pages, tombstones, seeded assets and the
site's reserved keys. It never answers a folder, because a folder is not a row — a folder exists only
because a key lives below it. So a page that wants a tree gets a flat stream of keys and folds them
itself, with its own answer to "what is a folder", "which folders are above this key" and "is this the
same key twice".

The host already knows that answer once. `Core.PageTree` is "below", "directly below" and "the folder
above", and `PageStoreReader.TreeAsync` uses it to build the tree the shell's `ls`, `find` and `ls -R`
walk. A page has neither, which is why the shell is blocked on this item
(**assistant-a-command-uses-page-capabilities**): a capability that cannot answer a tree makes the
shell either give up or reach around it.

So: **a listing answers the tree.** One ordered stream of keys, each saying what it is — a stored page,
a tombstone, a route the site answers, a folder — with the head's facts when there is a head, and
folders derived by the host from the keys it read. Five things follow, and they are the shape of the
change:

- **One stream, one cursor.** A folder row sits among the keys in ordinal order, so the cursor is still
  "the last key you were given" and a folder costs no second read: the slice that first reaches a key
  below a folder is the slice that emits it, and no earlier or later slice does.
- **The kind combines.** A key can be a page and a folder at once — `/blog` may be stored and
  `/blog/post` may live below it — so the kind is a set of flags, not one exclusive word. A route may
  be a folder for the same reason.
- **A folder is computed, never stored.** No table, no column, no write path to keep it in step. The
  rule is `PageTree`'s and the fold is one pure function in `Core`, so the shell, the IDE's tree and a
  site map cannot disagree about what a folder is.
- **The site's routes are rows too.** The owner's answer: a page's tree should match the shell's `ls`,
  which lists `/login` and `/admin` beside the stored keys. The list is `ReservedPaths`, already in the
  capability's hand, and a route row carries `Route` and no source.
- **Descending is a request.** `PageListingRequest` gains `Recursive`, true unless asked: true is the
  whole subtree and today's meaning, false is the prefix and its own children — a tree that descends
  one folder per request, for a store too big to walk in one go.

The cost is a contract change every page that reads a listing inherits, which is why the seed, the
manual and the prompt move with it and are not a follow-up.

## What the owner answered

Put to them on 2026-11-21, before this plan was written, in the item's own words and with the
recommendation each time:

1. **A `Kind` on the row, as the sketch says, with the kinds combinable.** Not a row that carries the
   key and an optional head, and not `IsFolder` bolted onto a `PageEntry` whose hash, saved time and
   state would then be empty strings. A page that is also a folder is one row.
2. **Folder rows are always in the answer.** No opt-in flag: a walk has one shape, and a page that
   wants heads only filters on the kind it already has.
3. **The depth is a request flag.** Not "every folder at every depth" and not "the prefix's own
   children only". Read here as `Recursive`, true by default: true is the whole subtree, which is what
   a listing means today and what a whole-store walk needs; false is the prefix's own row and its
   direct children, which is what a tree that descends folder by folder needs. A flag whose default
   changed a listing from a subtree to one level would silently empty every page that pages today, so
   true is the only default that can be.
4. **The shell's routes are rows.** A page's tree matches the shell's `ls`, so the route table is part
   of the answer rather than a second list a page has to join. `/admin` is a row, and a folder because
   `/admin/import` is below it.
5. **This step adds and proves the rows.** The shell's `ls`, `find` and the assistant's `list_pages`
   keep folding keys themselves until
   [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) moves them onto the
   capability, which is the plan that was waiting for this one.

Two corollaries are the plan's reading rather than the owner's words, stated so they can be corrected
at review rather than discovered in the code:

- **A folder exists while any key lives below it, a tombstone included.** A listing answers tombstones,
  so a folder over one is still a folder: it is how a restored page becomes visible again. The shell's
  own tree hides a folder whose only content is a tombstone because it lists live keys;
  `PageStoreReader` keeps that behaviour and the capability does not.
- **`Recursive = false` still answers the prefix's own row.** The store's predicate has always included
  the prefix itself (`Path == prefix OR Path LIKE prefix/%`), and the editor's per-page lookup depends
  on it. Dropping it in the one-level mode would make `ListAsync(new(key))` stop answering the page it
  names.

## The names this plan intends

Taken at the merge — bold rather than in backticks, because `make decisions` fails on a cited name the
record does not have yet:

- **pages-a-folder-is-a-key-not-a-row** — a folder exists because at least one key lives below it, is
  computed by the host from the keys a read answers, is never stored and has no write path to keep it
  in step; a row says what it is with a kind that combines, so a page that is also a folder is one row
  and the folder is not a second row for the same key.
- **pages-a-listing-answers-the-site-tree** — a listing answers the store's heads, the folders above
  them and the keys the shell's own routes answer, so a page draws the tree the shell's `ls` draws
  without owning the route table, the reserved-path rule or the fold.
- **pages-a-listing-descends-unless-asked** — a listing answers the whole subtree under its prefix
  unless the request asks for one level; either way the cursor is a key, so a walk visits every key
  once, in ordinal order, and a folder above a key is answered once rather than once per slice.

They are three rules rather than one because each can be broken without the others: a later step may
keep the tree and replace the fold, or keep the fold and drop the routes, and must not do it silently.

## What exists today

- **The seam a page reads.** `IPageCatalog.ListAsync(PageListingRequest)` asks for a prefix, a cursor
  and a limit and answers `PageListing(Entries, Truncated)`. `PageCatalogScope` normalises the three
  arguments, reads one row past the limit so `Truncated` needs no second query, and maps the store's
  `PageReadHead` rows onto the SDK's `PageEntry`. It already holds `ReservedPaths`, which is the route
  table, and `PageContentTypes`, which decides `IsHtml`.
- **The row.** `PageEntry` is a `readonly record struct` with a six-parameter constructor — key, saved
  time, hash, `IsDeleted`, `IsReserved`, `IsHtml` — and six `init` members: `IsAsset`, `IsLibrary`,
  `IsWorker`, `State`, `Libraries` and `Seed`. It is in `PublicAPI.Unshipped.txt`, as are
  `PageListing` and `PageListingRequest`, so the shape moves without a deprecation.
- **The store read.** `IPageReadStore.ListAsync(prefix, after, limit)` joins `PageHeads` to
  `PageRevisions`, filters a subtree with `Path == prefix OR Path LIKE prefix/%`, escaping `%`, `_` and
  `\` through a private `Escape`, orders by key and takes the limit. Each call opens its own
  `StorageDbContext`, because a page's task outlives the request. `Pages:Read:MaxRowsPerList` (500
  shipped, 10,000 the largest a site may run with) is enforced in the statement.
- **The rule.** `Core.PageTree` is `IsBelow`, `IsDirectlyBelow`, `FolderOf` and `FoldersAbove`; its
  remark says the rule lives there rather than in either of the two front-ends that had it.
- **The tree a tool builds.** `PageStoreReader.TreeAsync` reads every head through
  `PageEditor.ListAsync`, drops the reserved and (unless asked) the deleted, adds every reserved key,
  folds `FoldersAbove` into a `SortedSet`, and marks each node `IsFolder` by asking whether any other
  node `IsBelow` it. That is the answer a page is missing, written once for the shell.
- **The route table.** `ReservedPaths` is constructed in `Program.Main` from `ReservedPaths.Default`,
  which is `ShellCatalogue.Reserved`: twenty-one literal keys, `/login`, `/assistant`, `/admin`,
  `/admin/import` … `/metrics`, none of which a page may be stored at. `IsReserved` matches by prefix.
- **The readers.** `/pages` lists with `ListAsync(new())` and groups the rows into live and deleted;
  it also looks one key up with `ListAsync(new(key))` to decide whether an editor row is a library.
  `/help` has a listing sample and a paragraph, the assistant's prompt has the same sample and the
  paragraph in `page-author-v54`, and six test pages and fixtures read a whole-store listing and count
  or `Single()` it.
- **What the fold already agrees with.** `PageScope` and `PageTree` are `Core`'s, so the fold this plan
  adds is the same rule the assistant reads through — not a second opinion.

## Design

### 1. The row

`PageEntry` becomes a key with a kind and the head's facts when there is a head:

```csharp
[Flags]
public enum PageEntryKind {
    None = 0,
    Page = 1,        // a head is stored at the key
    Tombstone = 2,   // that head is deleted; only ever with Page
    Route = 4,       // the site's own routes answer the key
    Folder = 8,      // at least one key lives below it
}

public readonly record struct PageEntry(string Key, PageEntryKind Kind) {
    public DateTimeOffset? SavedAt { get; init; }
    public string? Hash { get; init; }
    public bool IsHtml { get; init; }
    public bool IsAsset { get; init; }
    public bool IsLibrary { get; init; }
    public bool IsWorker { get; init; }
    public string State { get; init; } = string.Empty;
    public IReadOnlyList<string> Libraries { get; init; } = [];
    public PageSeedSummary? Seed { get; init; }

    /// <summary>Whether the stored head is deleted. Reads <see cref="Kind"/>.</summary>
    public bool IsDeleted => Kind.HasFlag(PageEntryKind.Tombstone);

    /// <summary>Whether the site's routes answer the key. Reads <see cref="Kind"/>.</summary>
    public bool IsReserved => Kind.HasFlag(PageEntryKind.Route);
}
```

- **`Kind` is the fact; the two booleans are the question.** `IsDeleted` and `IsReserved` stay, and
  become views of `Kind`, because the editor's page list and the manual ask exactly those two questions
  and `Kind.HasFlag(...)` at each call site is the same rule spelled a second time. `PageEntry` is
  unshipped, so the constructor and the nullable members need no deprecation.
- **A folder row is a key and `Folder`.** It has no saved time, no hash, no state, no libraries and no
  seed, and the nullable type says so. `IsHtml` is false on a row that is only a folder, because
  nothing is served there; it is the content-type table's answer for a page, a tombstone and a route.
- **Everything else is the head's.** `IsAsset`, `IsLibrary`, `IsWorker`, `State`, `Libraries` and
  `Seed` keep their current meanings and are empty or null on a row with no head, exactly as they are
  on a route today.
- **`Tombstone` never stands alone.** A deleted page is `Page | Tombstone`, so `HasFlag(Page)` is the
  one question "is a head stored here" for both a live page and a tombstone, and `Tombstone` says
  which. That is what keeps `PageCatalogScope`'s existing judgements — the seed verdict, the state, the
  libraries — a single `if` rather than two cases.

### 2. The request

```csharp
public sealed record PageListingRequest(
    string Prefix = "", string? After = null, int? Limit = null, bool Recursive = true);
```

- **`Recursive` is true unless asked**, for the reason in *What the owner answered* 3: a listing's
  meaning today is the subtree, and the flag exists to offer the narrower answer, not to change the
  wider one.
- **The limit counts rows of every kind.** A slice of 500 may hold fewer heads than it used to, because
  folders and routes are rows. That is the honest reading of a cap on what a listing answers, and the
  doc comment says it.
- **The cursor is unchanged.** `After` is the last key the caller was given, a folder or a route
  included, normalised as a page key; `Truncated` says a row stands after the last one rather than a
  head.
- `PublicAPI.Unshipped.txt` gains the enum and the new two-argument constructor and loses the old
  members; the analyzer writes the lines and the plan does not guess them.

### 3. The fold

`src/uk.osric.asp.Core/Pages/PageListingFold.cs` — a pure function over keys, beside `PageTree`, with
`PageTree`'s rules used inside it rather than restated:

```csharp
public readonly record struct PageListingFact(string Key, bool IsFolder);

public sealed record PageListingNodes(IReadOnlyList<PageListingFact> Nodes, bool Truncated);

public static class PageListingFold {
    public static PageListingNodes Of(
        IReadOnlyList<PageListingFact> facts,
        string prefix,
        string? after,
        bool recursive,
        int limit);
}
```

The caller gives it every key the slice can see — the heads the store answered as `IsFolder: false`, the
folders a one-level read named as `IsFolder: true`, and the route keys as `IsFolder: false` — and it
answers the keys of the listing, in ordinal order, each marked as a folder or not.

The algorithm is four steps over one rule:

1. Every key in `facts` is a *node*, and every key in `facts` marked `IsFolder`, together with every
   folder in `PageTree.FoldersAbove(key)` for every key in `facts`, is a *folder*.
2. A node is in the answer when it is the prefix or `IsBelow` the prefix, is ordinally greater than
   `after`, and — when `recursive` is false — is the prefix or `IsDirectlyBelow` the prefix.
3. The prefix is a folder when something is below it, so a head stored at the prefix says `Page` and
   `Folder`; it is not its own child and never appears as a folder-only row.
4. Sort ordinally, take `limit`, and `Truncated = nodes.Count > limit`.

Three properties are worth writing down because the cursor depends on them, and each has a test:

- **A folder is emitted by the slice that first reaches a key below it.** The fold only sees a folder
  through a key under it; the key has to be past the cursor, and the folder is past the cursor exactly
  when that key is. So no slice emits a folder an earlier one already did, and no slice fails to emit
  one whose key it reached.
- **An extra head can only answer a folder early.** The store answers `limit + 1` rows so `Truncated`
  needs no second query. The ancestors of the extra row sort before it and may sort before rows the
  limit emitted — `/a` before `/a!`, though `/a/b` is the row past the cap — so a folder above a key
  past the cap is answered in the slice that read it. That is the right order, and it costs nothing:
  the folder is past the cursor, so no later slice answers it again, and every row the limit did not
  emit is still past the new cursor.
- **`Truncated` is exact.** Fold only adds keys, so `nodes.Count` is at least the number of rows read;
  more than `limit` rows read is more than `limit` nodes, and fewer means the whole subtree (or the
  prefix's whole level) was read.

The folder set is a `SortedSet<string>` with `StringComparer.Ordinal` and the node set a second one —
`n` is small, and the sort is what makes the answer a cursor rather than a bag (`code-n-is-small`).

This is where the item's sentence "the rule is the host's" becomes code: the page never folds keys,
never needs `PagePath` and never decides what a folder is. The shell will ask the capability exactly
what `PageStoreReader.TreeAsync` asks `PageTree`, so the two trees cannot drift.

### 4. The read

`IPageReadStore.ListAsync` gains the depth, and a second narrow read answers the folders a one-level
listing cannot see:

```csharp
Task<IReadOnlyList<PageReadHead>> ListAsync(
    string prefix, string? after, int limit, bool recursive, CancellationToken cancellationToken = default);

Task<IReadOnlyList<string>> ListFoldersAsync(
    string prefix, string? after, int limit, CancellationToken cancellationToken = default);
```

- **`recursive: true` is today's statement** with one clause added; **`recursive: false`** adds
  `AND NOT Path LIKE deeper`, where `deeper` is `Escape(prefix) + "/%/%"` — or `/%/%` at the root, as
  the subtree pattern already special-cases it — so a direct child is exactly one segment below the
  prefix and a deeper key is not answered. The prefix itself stays in both, because the editor looks a
  page up that way.
- **`ListFoldersAsync` answers the direct children that hold something below them**: one key per child,
  ordinally ordered and limited, and the same cursor. It is called only when `Recursive` is false,
  because in the recursive mode the slice already carries the keys a folder is derived from and a
  second read would be the cost the item rejects.
- **It is one statement, and it is raw SQL.** The segment of a key is the text up to the next slash,
  which EF Core cannot project: the only exact and bounded query is
  `SELECT DISTINCT substr("Path", 1, instr(substr("Path", length(@prefix) + 2), '/') + length(@prefix)) …`,
  with the `LIKE` escape, the cursor, the cursor's own subtree excluded and `LIMIT @limit`. `length()`
  rather than a length computed in C# because SQLite counts characters and a key may hold an astral
  character, where `string.Length` counts UTF-16 units and the segment would be cut in the wrong place.
  The repository already runs statements this way (`SqliteConversationStore` builds a `FromSql` query
  and `SqlitePageStore` runs its conditional saves with `ExecuteSqlAsync`), and the statement sits
  beside the listing statement it completes.
- **The escape stays one helper.** `Escape` is already private to `SqlitePageReadStore`; the new
  predicate and the new statement use it rather than a second spelling of `%`, `_` and `\`, and the
  test that proves `/100%/x` is a subtree and not a pattern covers both.
- **A tombstone is a key.** `PageHeads` holds it, so `ListFoldersAsync` counts it, and the folder over
  a deleted page survives until the row is scrubbed. That is the corollary from *What the owner
  answered*, and it is a test.

### 5. The capability

`PageCatalogScope.ListAsync` stops being a map and becomes three steps: read, fold, map.

1. **Normalise** the prefix, the cursor and the limit as it does now, so `""` and `"/"` are the whole
   store and an unusable key is an `ArgumentException`.
2. **Read**: the heads with `recursive`, and — only when `Recursive` is false — the folders.
3. **Fold**: `PageListingFold.Of` over the heads, the folders and `reserved.Keys`, with the prefix, the
   cursor, `recursive` and the limit.
4. **Map** each node to a `PageEntry`: the head's facts when the key is a head in the slice, `Route`
   when it is one of `reserved.Keys`, otherwise `Folder`; `Folder` is added when the node is a folder,
   on top of whatever else it is. `State`, `Seed`, `Libraries`, `IsAsset`, `IsLibrary` and `IsWorker`
   are the existing judgements and are asked only for a key with a head.

The reserved keys are in the capability's hand already, so a route row costs no read; the route table
is bounded by the build and cannot grow with the store. A route that is also a folder — `/admin` — is
`Route | Folder` in one row, and a route whose ancestors are not routes gets its folders from the fold
like any other key.

`ReservedPaths` matches by prefix, which is the right rule for the *policy* (`/admin/scrub` shadows
everything beneath it) and the wrong rule for a *listing*: a key is a route when the route table names
it, not when a route is an ancestor of it. The route rows therefore come from
`reserved.Keys`, the canonical list, and `IsReserved` on a row keeps its current meaning — "the site's
routes answer this key" — because a stored head can only be at a reserved key by historical accident
and the fold never invents one.

### 6. The two properties a walk depends on

A caller pages with `After = last key` and loops while `Truncated`. Two behaviours make that safe once
rows have kinds:

- **One row per key.** The fold merges the head, folder and route facts by key, so `/blog` is one row
  saying `Page | Folder`, not two rows a caller would have to join. This is why the kind combines.
- **A folder is not re-answered.** A slice that starts inside a folder answers the folder's children,
  not the folder again, because the fold only sees a folder through a key under it and the cursor is
  past the last key the caller has. The corollary is that a walk started at a cursor that is itself a
  folder never sees that folder — it asked to resume after it.

Neither property changes the store's keyset guarantee (`pages-a-listing-resumes-from-a-key`): a page
saved between two calls still cannot make a key skip or repeat, and a folder saved into existence by a
first save under it is emitted by the next slice that reaches it.

### 7. Cost

The recursive answer costs what it costs today plus the fold: one statement, one row past the limit,
and a sort of the slice and its ancestors — a few hundred keys at most, on a store of a few hundred
pages (`code-n-is-small`). Nothing is stored, so nothing writes and nothing can forget.

The one-level answer costs one statement for its rows and one narrow statement for its folders, both
bounded by `limit + 1`. The second is what makes `find -type d` true: a folder whose only content is
deeper is invisible to a query that reads one level, and a listing that omitted it would be a tree
missing a branch.

The whole-store listing grows by its folders and the twenty-one routes: on the shipped site that is
eighteen seeds, their folders and twenty-one routes, where it was eighteen heads. That is the price
of the owner's answer 4, and it is why `Recursive` is a request rather than a second capability.

### 8. The readers that must not see the new rows

The editor's own page list is the first caller of `ListAsync` and it wants heads, not a tree. Two
places in `/pages` filter on the row's kind instead of assuming every row is a page:

- **The list** keeps `entries.Where(entry => entry.Kind.HasFlag(PageEntryKind.Page))` before it groups
  live and deleted, so the two tables say what they said and a folder or a route is not offered an
  Edit link.
- **The per-page lookup** — `ListAsync(new(key)).Entries.FirstOrDefault(...)` — keeps only a row with a
  head, so a key that is only a folder no longer looks like a page to the editor's own menu.

`/help` and the assistant's prompt teach the new shape in the same step: a row is a key and a kind,
`SavedAt` and `Hash` are null when there is no head, folder and route rows are part of the answer, and
`Recursive` picks the depth. The prompt's sample lists the kind rather than a live/deleted word, and
`PageAuthorPrompt.Version` moves one on from `page-author-v54` — `v55` unless
[`listing-fingerprint`](listing-fingerprint.md) has landed first, in which case it moves past whatever
that step left.

## Steps

**One step**, because the contract, the seed and the prompt cannot land apart: the moment a listing
carries folders and routes, `/pages` shows them and the manual describes a shape that is not there. The
step is one branch, one gate and one stop for the owner.

1. `PageEntryKind` and the reshaped `PageEntry`; `PageListingRequest.Recursive`; the doc comments that
   say what a row is and what a folder is not.
2. `PageListingFold` in `Core`, with its tests, and the two `PublicAPI` files.
3. `IPageReadStore.ListAsync`'s depth, `ListFoldersAsync` and the shared escape in `SqlitePageReadStore`.
4. `PageCatalogScope`: read, fold, map.
5. `/pages` filters to heads; `/help` and the prompt teach the shape; the test pages that read a whole
   store narrow to the rows they are about.
6. The documents, the item's `Status` and `Plan`, and the README row.

The backlog item moves to `planned`, pointing at this plan, in this step. It closes when the work
merges, and the merge is also what takes the three names.

## Tests

Every test is NUnit and names the behaviour; anything a unit test can prove does not go to a browser.

`test/uk.osric.asp.Core.Tests/PageListingFoldTests.cs`

- A folder above a key is answered once, before the key.
- A key that is both a head and a folder is one node and it says `IsFolder`; a key that is only a key
  is not a folder.
- The prefix's own key is answered and is a folder when something is below it, and is not answered as
  a folder when nothing is.
- A key not below the prefix, and a folder above the prefix, are not answered.
- The cursor is exclusive: a slice after a key does not repeat it, and a slice after a folder answers
  the folder's children and not the folder.
- A one-level fold answers the prefix and its direct children and no deeper key; a recursive fold
  answers every key below the prefix.
- `Truncated` is true when a row stands after the last one and false when the fold answered the whole
  set.
- A folder above a key past the limit is answered before the rows the limit emitted, and the walk
  still answers every key once — `/a` before `/a!` while `/a/b` stands past the cap.

`test/uk.osric.asp.Web.Tests/SqlitePageReadStoreTests.cs`

- A one-level read answers the prefix's own head and its direct children and not a grandchild.
- A recursive read still answers the grandchild, and the existing subtree and escaping tests still
  pass.
- `ListFoldersAsync` answers one key per direct child that holds something below it, ordered and
  limited, and names no direct child that holds nothing.
- A tombstone below a child makes that child a folder until it is scrubbed.
- A prefix containing `%`, `_` or `\` still selects a subtree rather than a pattern, in both reads.
- A key holding a non-BMP character is folded at the right slash, which is the `length()` trap above.

`test/uk.osric.asp.Web.Tests/PageCatalogScopeTests.cs`, over `FakePageReadStore`

- A recursive listing of `/` answers a folder, the page in it and a page that is also a folder, in key
  order, one row per key.
- A listing of a route answers the route and says `Route`; a route with routes below says
  `Route | Folder`; a route has no saved time, no hash and no state.
- A one-level listing answers the prefix's row and its direct children, and a child with only deeper
  content is a folder row.
- The existing tests that list the whole store narrow to heads, and the fixture's `Scope` takes the
  route list so a test can choose one that is not the shipped twenty-one.
- A page saved between two slices is neither skipped nor repeated, and a folder is answered once.
- The limit counts folder and route rows, and a limit past the cap is still refused with the setting's
  name.

`test/uk.osric.asp.Web.IntegrationTests/PageCatalogTests.cs`

- A stored page walks the whole store with the cursor, and every key is answered once, folders
  included.
- A folder row carries no saved time and the page's own row carries its own.
- The public listing page filters to rows with a head before it formats a saved time, because the
  member is nullable now; the same fixture's paged page keeps its `Truncated` assertions. The listing
  page in `PageLibraryTests` narrows the same way.
- A route the shell answers is in the listing, and says `Route`.
- The paged test page still ends with `Truncated` false at the last key.

`test/uk.osric.asp.Web.Tests/PageAuthorPromptTests.cs`

- The catalog section names the kinds, the two null members and `Recursive`, and the version moved.

## Documentation

- **The manual**, `/help` (`product-manual-is-a-page`): the listing section gains the row shape, the
  folder rule, the route rows, `Recursive` and the sample's kind column; the cursor paragraph keeps
  its promise and says a folder row is a key like any other.
- **[pages](../pages.md)**: the `IPageCatalog` paragraph — the row is a key and a kind, a folder is
  computed and never stored, routes are rows, and the request's depth.
- **[storage](../storage.md)**: the read-seam paragraph — one predicate with a depth, one narrow
  statement for a one-level listing's folders, nothing stored and no migration.
- **[architecture](../architecture.md)**: the "Where does code go" row for a tree a page reads:
  `Core.PageListingFold` with `PageTree`'s rules, and `Web`'s `PageCatalogScope` mapping the rows.
- **[glossary](../glossary.md)**: a **Folder** entry — a key with at least one key below it, computed
  from the keys a read answers, never stored; not a page, though a page may also be a folder.
- **[decisions](../decisions.md)** at the merge: the three names above, each with its "Recorded in"
  line; `pages-a-listing-carries-facts` edited in place because "returns every head" stops being true;
  `pages-the-store-is-a-tree` edited in place to say the capability answers the tree; and
  `pages-a-listing-resumes-from-a-key` given the depth in its list of what a request carries.
- **The author prompt**: the catalog section and the sample, with `PageAuthorPrompt.Version` moved.
- **The backlog**: this item's `Status` and `Plan`, and its row in the order; it unblocks one of
  [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md)'s three.

## Risks

1. **Every page that reads a listing inherits the change.** Folders and routes are new rows, and
   `SavedAt` and `Hash` are now nullable. The seed, the manual, the prompt and the test pages move in
   the same step; a stored page outside the repository that iterates a listing and formats
   `entry.SavedAt` stops compiling on its next save, which is the honest cost of a nullable member and
   the compiler is what says so. `IsDeleted` and `IsReserved` stay so the common filters keep reading.
2. **A whole-store listing is bigger.** Eighteen heads become eighteen heads plus their folders and
   the twenty-one routes on the shipped site; a page that pages with a small limit needs more slices.
   The cap and the cursor are unchanged, and §7 states the size.
3. **A folder that a slice fails to derive is a missing branch.** The fold derives every folder from a
   key the walk will reach, and the property has its own test; the failure to watch for is a filter
   applied to a key before its ancestors are folded, which is why the fold takes facts and not rows.
4. **The one-level answer needs its second read.** Without it a child whose only content is deeper
   disappears, which is a wrong tree rather than a slow one. §4 and the store tests are the guard.
5. **The raw statement is the one place SQLite's character counting matters.** `length(@prefix)` and
   `substr` count characters, `string.Length` counts UTF-16 units; a key with an astral character cuts
   the segment in the wrong place if the index is computed in C#. The test is named for the trap.
6. **A listing now names keys that are not in the store.** A page that treats the answer as "what is
   stored" must filter on `Kind`, exactly as the editor does. The manual says it; the host does not
   hide it, because a route is part of the site a page draws.
7. **Two plans touch the same seam.** [`listing-fingerprint`](listing-fingerprint.md) changes
   `IPageReadStore.ListAsync`'s return to carry a version; this plan adds a parameter and a second
   read. Whichever lands second rebases its signature, and neither changes its contract to do it.
8. **Keeping `IsDeleted` and `IsReserved` is a deliberate second spelling.** They are views of `Kind`,
   not stored facts; if a later step removes them, `docs/plans/assistant-shell-tool.md`'s "`CanWrite`
   read from `!IsReserved`" moves with them, and the merge that removes them says so.

## Considered and rejected

1. **A folders table, maintained on write.** A folder would be a row, with a count or a marker, written
   by every save, move, delete, scrub, import and seed install. It is the shape the fingerprint plan
   measured and rejected for a token, for the same reason here: a write path that forgets is worse than
   no fact, and every write path is a place to forget.
2. **`IsFolder` on `PageEntry`, with an empty hash and saved time.** The sketch's own rejection: a
   folder row would be a page with nothing in it, and the type would not say which fields mean
   anything. Nullable members on a row with a kind say it.
3. **One exclusive `Kind`.** Page, tombstone, route or folder — a key that is both a page and a folder
   would have to be two rows, which breaks one row per key and makes the cursor answer two rows at one
   key. The owner chose combinable flags.
4. **A second method, `ListFoldersAsync`, on `IPageCatalog`.** Two calls cannot share a cursor or a
   snapshot, so a tree would page twice and race itself; one ordered answer is what the item is for.
5. **Folders only when the request asks.** A walk would have two shapes and a page that forgot the flag
   would silently lose its tree; the owner chose always.
6. **The store's heads only, no routes.** A page's tree would not match the shell's `ls`, and every
   page that wanted the site's own keys would need `ReservedPaths` — which is the shell's rule, not a
   page's. The owner chose the routes.
7. **`Recursive` false by default.** Every page that pages today would silently start answering one
   level instead of a subtree. A flag exists to offer a narrower answer, not to change the wider one.
8. **Deriving folders from the recursive read even in the one-level mode.** A one-level answer would
   have to read the whole subtree per slice and could still miss a child whose only content is past the
   limit; the narrow statement is both exact and bounded.
9. **A folder's `CanWrite`.** The shell needs a permission column for a folder, and it reads it from
   `PagePathPolicy`; a `CanWrite` on the row would be a second answer to a question the write
   capability already owns. `IsReserved` is the fact the shell turns into mode `000`
   ([assistant-shell-tool](assistant-shell-tool.md)).
10. **Sorting, filtering or a `Kind` argument on the request.** The answer is ordered by key and the
    caller filters, which is what every other listing does; a predicate on the wire is a smaller
    language to learn and to keep in step with `PageScope`.

## What this does not close

- **The shell still folds keys itself.** `PageStoreReader.TreeAsync`, `ls`, `find`, `stat` and
  `list_pages` keep their own tree until
  [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) moves them onto the capability.
  This step gives that plan the rows it was waiting for; it does not take its work.
- **The IDE's tree is page work.** T8 of [the IDE plan](ide-host.md) draws it; this is the read it
  needs, not the tree.
- **A search's scopes.** [`cannot-search-the-store`](../backlog/cannot-search-the-store.md) reads
  sources in slices and is unaffected by the row's shape.
- **Whether the store moved under a walk.** That is
  [`listing-fingerprint`](listing-fingerprint.md), which shares the seam and nothing else.
- **A folder's history.** There is none: a folder has no creation, no owner and no revision, because it
  is not a thing the store holds.

## Not in scope

- **Creating or removing a folder.** A folder exists while a key lives below it; the save that first
  writes `/blog/post` makes `/blog`, and scrubbing the last page under it removes it. `mkdir` is a
  shell command with nothing to do.
- **A permission column or a `CanWrite`.** §Considered and rejected 9.
- **A second shape for a one-level listing.** A folder row is the same row at either depth.
- **A `PageEntry` tidy.** Removing `IsDeleted` and `IsReserved` in favour of `Kind` alone is a later
  step with its own citation sweep.
