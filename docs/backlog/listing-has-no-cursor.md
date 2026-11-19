<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot ask a listing for the next page

| | |
| --- | --- |
| Difficulty | eager-beaver |
| Utility | medium |
| Status | planned |
| Blocked by | — |
| Plan | [ide-host](../plans/ide-host.md) |
| Area | Sdk, Web (pages), docs |
| Location | `src/uk.osric.asp.Sdk/IPageCatalog.cs`, `src/uk.osric.asp.Sdk/PageListing.cs` (new), `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

`IPageCatalog.ListAsync(prefix)` returns at most `Pages:Read:MaxRowsPerList` rows and **refuses** rather
than truncating when there are more (`pages-a-listing-carries-facts`). Refusing is the right answer to
"here is a listing I cannot finish"; it is the wrong answer to "I only wanted the first screen" and it
is no answer at all to "give me the rest". A store that outgrows 500 keys cannot be listed by any page,
which makes a 501st page an outage of the tree rather than an addition to the site.

The host already has the answer and does not offer it:

- **`PageStoreReader.TreeAsync`** reads the whole store with no cap, and `list_pages` walks it with a
  `limit` and an `after` cursor — a string comparison against the last key returned
  (`PageTools.cs`). The assistant has paginated the store since the store reader landed.
- **`PageCatalogScope.ListAsync`** asks the store for `MaxRowsPerList + 1` and throws when the extra row
  arrives. The store call underneath already takes a limit and a prefix, so a cursor is a parameter
  rather than a design.

The wider class is every bounded read: a listing, a page's history, a search's matches
([`cannot-search-the-store`](cannot-search-the-store.md)) and a folder's children all have the same three parts — a bound, a way to
resume, and an honest "there is more". Writing that once as a value keeps every later read from
inventing its own.

## Sketch

1. **One request, one result.** `ListAsync(PageListingRequest)` — prefix, `after`, `limit` — answers a
   `PageListing` with the rows and `Truncated`. No prefix means the whole store; `after` is exclusive and
   takes the last key the caller was given; `limit` is bounded by configuration and defaults to a
   configured default. The words are `list_pages`'s, so the page and the tool cannot describe one slice
   two ways.

2. **A keyset cursor, not an offset.** The store's order is by key, so `after` is a key rather than a
   count: an offset skips or repeats a row whenever a page is saved between two calls, and a key cannot.
   `PagePath.Normalize` canonicalises the cursor exactly as it canonicalises a prefix, so `/blog/` and
   `/blog` resume in the same place.

3. **`RevisionsAsync` follows.** A history is unbounded and has the same shape, with (saved time, hash)
   as its order. Whether the first version pages it as well is an open question below; the value is
   shared, and the cursor for a history is a hash rather than a key.

4. **The cap stops being a refusal.** `Pages:Read:MaxRowsPerList` becomes the largest `limit` a caller
   may ask for, not a wall. A caller that asks for more is refused with the range, the rule
   `code-policy-values-are-configuration` already gives every configured bound.

5. **Folders need no request.** The tree is derived from keys by splitting on `/`, as `/pages` does
   today; a folder is not a row and is not worth a listing of its own. What a folder needs is its
   children, which is a prefix and a limit — this request.

6. **The documentation** is a `/help` line for the cursor, a `PageAuthorPrompt` paragraph with its
   `Version` bumped (`list_pages` already explains an `after` cursor to the assistant, and the page
   contract should agree with it), and the listing section of [pages](../pages.md).

## Open questions

1. **Whether the default is all rows or one screen.** A listing that defaults to every row keeps the
   current behaviour for the small site and reads the whole store; one screen makes the tree one request
   per screen. The tree wants the whole thing once and the filter wants what is on screen. Defaulting to
   a screen and letting the caller ask for the cap is the honest version; check what `/pages` and
   `/recent` want first.
2. **Whether a listing says its total.** A count is a second query at any size, and "there is more" is
   enough for a tree. Decline unless a caller needs it.
3. **Whether `RevisionsAsync` pages in the first version.** It is unbounded today and nothing has hit it;
   a page's history grows with every save, and a bulk import can make a hundred. The cost is one more
   value and one more test, so the answer is probably yes, in the same change.
4. **What `after` does at the end.** An empty page with `Truncated` false is a complete answer, and a
   cursor past the last key is not an error. Say so in the manual rather than leaving a caller to
   discover it.

## Not in scope

- **A lazy folder listing.** A prefix and a limit already answer "what is in this folder", and a
  per-folder endpoint would be a second way to walk one tree (`pages-the-store-is-a-tree`).
- **An ordered listing that is not by key.** A tree in saved-time order is a different view with a
  different cursor; nothing wants it yet.
- **A search cursor.** That is [`cannot-search-the-store`](cannot-search-the-store.md)'s, because a match is ordered by key *and* line.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan observes that `ListAsync(prefix)` "refuses
rather than truncating" and asks for "the cursor the command line already has"; `list_pages` has had
that cursor since `pages-a-listing-carries-facts` was recorded.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 1 there, and the
shape both the search and the tree are written against.
