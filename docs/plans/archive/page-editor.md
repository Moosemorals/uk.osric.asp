<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page editor

| | |
| --- | --- |
| Status | Landed 2026-11-13: phases 0–5 |
| Size | Large — a new Sdk capability and its values, a read-contract extension, a scoped authority, the seeded `/pages` editor and its module, retiring `/edit`, a shell scrub route, and the tests and documents for each |
| Area | Sdk, Web (pages, editor, admin, routing, navigation), seeds, docs |
| Backlog | IDEA-017 — closed by this plan. [IDEA-028](../../jobs/README.md#closed) — the prerequisite recovery path, landed |
| Decisions | DEC-259–DEC-268, with DEC-258 (the read state) and DEC-256 (the recovery route) from the phases that landed earlier |
| Prerequisite | [IDEA-028](../../jobs/README.md#closed) landed 2026-11-13: `/admin/restore` is the way back from a broken editor (DEC-256) |

## The short version

The editor is the last large view in the shell, and it is page-shaped: a listing, a form, history,
confirmations and navigation. What keeps it in the shell is not markup but authority: it writes any
key, tombstones pages, restores revisions and installs seeds. This plan gives that authority to one
stored page, under a narrower seam than the assistant's, and lets the shell editor go.

- **The editor becomes `/pages`.** The seeded page already lists the store and shows a page's
  history and source through `IPageCatalog`; `?page=/about` keeps that read, `?edit=/about` adds the
  form. A page key stays an exact path (DEC-18), so the edited path travels as a query value the
  page validates through the host.
- **The write is a capability, and a narrow one.** A new `IPageEditor` offers a conditional save, a
  tombstone delete, a revision restore and a seed install. It is registered only for the content
  layer whose key is `/pages` and never for a layout; a `HEAD` gets the read members but no writable
  handle, and every write needs a signed-in visitor. Reserved keys are refused. It is not
  `IPageWriter` widened: a page that could name any key would turn every stored page into a
  potential handle on the whole store.
- **The editor answers in place.** A save renders its diagnostics, a stale save is a `409` carrying
  the submitted text beside the current source, and a delete, restore or install re-renders the page
  or redirects to it. No flash, no second session key and no [IDEA-027](../../jobs/IDEA-027.md).
- **The shell keeps only the destructive route.** `/edit` stops being an editor and stops being a
  reserved key: the catch-all owns it, no page is stored there, and it answers `404` until somebody
  writes a new page at that key. Permanently removing history is still a destructive act with a
  typed confirmation, so scrub moves to `/admin/scrub/{**path}` until a decision says otherwise.
- **IDEA-028 has landed.** `/admin/restore` is the brutal one-button restore of every seed to its
  bundled source, and it draws a plain document with no stylesheet or script, so a broken
  `/site.css` cannot hide it. It is what makes removing the shell editor safe, and it is the answer
  to a broken `/pages`, `/_layout`, `/editor.css` or `/editor.js` (DEC-256).
- **One writer remains.** `PageSaver` grows the conditional save on its own context, so a started
  edit is not lost when the render ends; the capability and the seed helper both use it. No second
  insert path, no second query language, no second saver.

The visible change is that `/pages` grows an editor and `/edit` is retired — a bookmark there is a
`404` until someone stores a page at that key. The plan is large because the authority seam, the
stale-save rule, the read state the listing already shows, and the recovery path all have to agree
before the shell view can go.

## Goals

1. A signed-in owner lists pages, opens one for editing, saves, views, deletes, restores a revision
   and installs a seed from `/pages`, with the diagnostics and history the shell editor shows.
2. The capability refuses an anonymous render, a write during a `HEAD`, a second write in one
   render, a reserved key and an ended or timed-out render before it compiles or stores anything; a
   layout is never offered it at all.
3. A form carries the revision hash it was rendered from; a save against a newer head is refused
   with `409`, the submitted text is not lost, and the next save is conditional on the current head.
4. A page cannot delete, restore or install a key the site's own routes answer; it also cannot
   tombstone the editor page itself by accident.
5. `/edit` and its command URLs are retired: no route, no reserved key, and a `404` until somebody
   writes a new page at that key.
6. The listing keeps the compile state the shell editor shows, because the read contract grows the
   image state the host already knows.
7. Scrub remains a shell confirmation, linked from the page but never performed by it.
8. The recovery path is live: one `POST` to `/admin/restore` brings `/pages` and every other seed
   back from a broken state, from a page that depends on none of them.
9. The page editor uses the one saver the assistant and the shell already share, with the
   conditional save moved onto it; it adds no store insert and no new archive format.
10. The author manual, the assistant prompt and the architecture documents describe `/pages` and
    `/admin/restore`, not `/edit` as the editor.

## Non-goals

- **Scrub in the page.** Permanently removing a deleted page and its history is a destructive act
  with a typed confirmation, and it is not editing. It moves to its own shell route unchanged; a
  later decision can move it once the page editor has proved itself.
- **A catch-all that hands a subtree to one page.** `/pages?edit=` keeps one page equals one key
  (DEC-18). A routing change that lets one page answer `/about` and `/about/anything` is a change to
  what a page key means for every page, and it is not needed for this view.
- **A writable prefix for the owner.** That is an assistant rule. The owner edits what the editor
  offers.
- **A second `IPageWriter`.** Widening the self-save to a named key would make every page a handle on
  every other page. The named-key authority is its own capability, offered only where the host
  chooses.
- **A compatibility redirect for `/edit`.** The old editor URLs are retired, not mapped. A bookmark
  or an open tab answers `404`; `/edit` is free for a future page. The one exception is the
  destructive scrub POST, which moves to `/admin/scrub/{**path}` rather than disappearing.
- **A page-session flash.** The editor answers the `POST` that acted, or redirects to a URL it can
  render from the store. It does not need a message that another page must read, so IDEA-027 stays
  out of this change.
- **A reset or a site archive.** Restoring seeds is not "put the site back to an archive"; that is
  [IDEA-020](../../jobs/IDEA-020.md), with its own scope, confirmation and pre-nuke export.
- **An assistant that edits through the page.** The assistant keeps `PageToolExecutor`,
  `PagePathPolicy` and `PageEditor`; this plan does not route it through the page capability.
- **A new JavaScript toolchain.** `/editor.js` stays a native ES module, loaded with
  `<script type="module">` and served as a stored asset.
- **Editing reserved keys.** `/admin/restore`, `/admin/scrub`, `/login`, `/status` and the rest stay
  shell files; the page refuses them and the assistant continues to describe them. `/edit` is no
  longer one of them.

## What exists today

| Piece | Today | Used here for |
| --- | --- | --- |
| `IPageCatalog` / `PageCatalogScope` | Four reads: list heads, read a live page, list history and read a revision; public by default; bounded by the shared read budget | The listing, the history, the source view and the diff; the page needs no new read |
| `IPageWriter` / `PageWriterScope` | A self-save bound to `ctx.Path`; signed-in, no `HEAD`, one save per render, a started save finishes (DEC-189–DEC-196) | The authority rules the named-key capability inherits, and the tests they already pin |
| `PageEditor.SaveIfRevisionAsync` | Compile-then-store only while the head hash matches; the assistant's conditional save, on the request's scoped store | The stale-save rule, moved to the singleton save path so a started save survives the render |
| `PageEditor.SaveAsync` / `PageSaver` | The one compile-then-store path; diagnostics are returned, a compiler fault still stores the source | Every write the new capability makes, including seed install |
| `EditorEndpoint` | Listing, editor form, history, revision, diff, delete, scrub, seed install and revision restore; `/edit` is behind the `Editor` policy | The views, wording, confirmations and tests to port; its scrub code moves and the rest is deleted |
| `PageAssetSeeder` | `SeedPaths`, `ReadSeedTemplateAsync`, `SeedAsync`; seeds are flat files under `Pages/Seeds` | The seed list and the source the page installs and the emergency restore uses |
| `/pages` seed | A guarded listing of `IPageCatalog`, a source/history view and a `?page=`/`?rev=` pair; links to `/edit` | The page editor grows from it; it is the one stored page with the new capability |
| `/editor.js` and `/editor.css` | A native module for the line gutter, caret and diagnostic links; a sheet for the editor and page tables | Loaded by `/pages` with `ctx.AllowScripts()` and `IPageLayout.AddStylesheet` |
| `PageEntry` / `IPageReadStore` | `PageEntry` marks deleted, reserved, HTML and asset; it has no compile state | The listing's `State` comes from the read store's image columns, without a second read |
| `ReservedPaths` / `ShellRoutes` | `/edit` is reserved and described; `/admin/restore` and `/admin/changes` are reserved too; the shell-route table is pinned to the reserved list both ways | `/edit` leaves both lists, so it is an ordinary missing page; `/admin/scrub` joins them, and the routes already there stay |
| `SiteNavigation` / `AdminNavigation` | The site menu points at `/edit`; `/admin` already carries "Restore seeds"; `/status` already links `/admin/restore` | The owner's "Pages" link moves to `/pages`; the recovery links stay as they are |
| `PageEndpoint` / `PageChain` | Builds the capabilities for each layer; the content layer gets the writer, a layout does not | Adds the scoped editor and registers `IPageEditor` only for `/pages` |
| Revision restore (ARCH-07, landed) | The shell editor confirms a revision or a seed install with a diff and saves the chosen source; the assistant has `list_revisions`, `read_revision` and `restore_revision` | The page ports the same diff-and-confirm rule and the same refusals |

## Decisions

### D1 (DEC-259) — The editor is `/pages?edit=<key>`

`/pages` is already the stored page that lists the store and shows source and history. Adding
`?edit=/about` keeps one page equals one key and makes the edited path a query value. The page
validates it through `IPageEditor.Resolve`, which normalises it with the same `PagePath` rules every
other key uses and reports whether it is reserved. `/pages?page=/about` remains the read/history
view; `?edit` is the editor.

Rejected: `/editor?path=` as a new seeded page, because it duplicates the listing that `/pages`
already is and adds a seed to restore. Rejected: a catch-all subtree for one page, because it changes
the meaning of every page key.

### D2 (DEC-260) — A new `IPageEditor` capability, not `IPageWriter` widened

`IPageWriter` is safe because the handle itself has no key: a page can only save itself (DEC-189).
Giving it a `path` parameter would turn every stored page into a handle on the whole store. The
named-key authority gets its own interface, `IPageEditor`, with its own registration rule and its own
tests.

Shape (names are the plan's, final spelling follows the Sdk style):

```csharp
public interface IPageEditor {
    public PageKeyResolution Resolve(string key);
    public Task<IReadOnlyList<string>> SeedPathsAsync();
    public Task<PageEditResult> SaveAsync(string key, string template, string expectedHash);
    public Task<PageDeleteResult> DeleteAsync(string key);
    public Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string expectedHash);
    public Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash);
}
```

`PageKeyResolution` carries the canonical key and whether a shell route owns it. `PageEditResult`
carries `Saved`, `Stale`, the hash and the diagnostics, so a matching seed can say "already current"
without a write; `PageDeleteResult` carries whether the head was live. A missing revision or a key
that is not a seed returns `null`, because that is an answer the page shows rather than an exception.

### D3 (DEC-261) — Authority is structural, then checked

`IPageEditor` is registered only for the content layer whose key is the configured editor key. A
layout never sees it, another stored page never gets it even if its code asks, and a `HEAD` gets the
read members (`Resolve`, `SeedPaths`) with every write refused, so the form still renders for a
validator.
The scope then checks what the handle cannot:

- an anonymous render is a `UserFacingException` before anything is compiled or stored (DEC-190);
- an ended or timed-out render, a write during a `HEAD` and a second action in one render are
  `InvalidOperationException`s, because they are page bugs;
- a key that cannot name a page is an `ArgumentException`; a reserved key is refused before any read
  or write;
- a delete of the editor itself is refused, so the ordinary editor cannot tombstone the editor by
  accident. A broken editor is recovered by `/admin/restore`, not by the page's own delete button.

The key is policy, so it comes from `Pages:Editor:Key` (`/pages` as shipped) rather than a constant in
the code: a capability that reaches the whole store belongs with the other values an operator sets.
The value is normalised, and one that names no addressable page falls back to the default, so a typo
cannot leave a release with no editor. `PageChain` compares against it and copies it into the scope,
which is why the self-delete guard protects whatever key is configured rather than a hard-coded one.

The assistant's `PagePathPolicy` is not reused for the owner: its writable prefix and its shell-file
rule exist to stop the assistant, and the owner edits exactly those pages.

### D4 (DEC-262) — A save is conditional, and stale text is preserved

The editor form carries the hash it was rendered from. `SaveAsync` calls a new
`PageSaver.SaveIfRevisionAsync`, which compiles before it stores only while the head still matches.
A form rendered from a page with no live source carries no hash, and `SaveAsync` takes `null` for it:
the save then applies only while the page is still missing, so the create path is conditional too and
a page that appeared under the form is a `Stale` rather than a silent overwrite.
A mismatch returns `Stale` without a write; the page answers `409` and renders the submitted text in
the textarea beside a read-only copy of the current source and its hash, so the author reconciles by
pasting rather than losing the draft. The next form carries the current hash.

The comparison every write ends in — would this leave the source exactly as it is? — is
`Core.Pages.PageSourceChange.IsNoOp`, because ARCH-07 already gave restore and install one rule and the
shell editor, the assistant's `restore_revision` and the capability each wrote it out again. The
"revision with this hash is not a revision of this page" check is `PageEditor.GetRevisionAsync`, for
the same reason: three callers, one rule, and each keeps its own words for the refusal.

The conditional save moves from the request-scoped `PageEditor`/`IPageStore` to `PageSaver` and
`IPageRevisionStore`, which own a context per write. That matters because a page's save can outlive
the request that started it: the render's deadline bounds the wait for a compile slot, not the
author's edit (DEC-191). `PageEditor.SaveIfRevisionAsync` becomes a delegation to the same method, so
the assistant and the page still share one algorithm. `SqlitePageStore.TrySavePageAsync` and the
singleton store share the conditional head update; the revision insert remains
`SqlitePageStore.WriteRevisionAsync`. This is a narrowing, not a reversal, of DEC-192: the
conditional save is offered to the one editor page, through the one capability, and the ability to
write any key is still not offered to every page.

Rejected: last-write-wins, which is what the shell editor does today and what two tabs make unsafe.
Rejected: a redirect with a flash, which loses the text and needs IDEA-027.

### D5 (DEC-263) — Delete is a tombstone, restore is a save, install is a save

- `DeleteAsync` uses `PageEditor.DeleteAsync`; the head becomes a tombstone and the history stays.
  A missing page is `Deleted = false`, not an error.
- `RestoreRevisionAsync` reads the retained revision through `PageEditor.GetRevisionTemplateByHashAsync`,
  checks it belongs to the key, and saves its source as a new revision under the current build. It
  also carries the head hash the form was rendered from, so a concurrent save is refused rather than
  silently overwritten. The page shows the diff before it asks.
- `InstallSeedAsync` reads the bundled source through the shared seed helper and saves it the same
  way. A seed that already matches is not written. A tombstoned seed comes back with its history.

A live key is always saved conditionally on the head hash the form carried. A missing or tombstoned
key has no live head to compare, so its restore or install saves unconditionally and clears the
tombstone; the ordinary save path already upserts the head. The page does not rewind a head, does
not edit a revision and does not recompile an old revision under an old runtime. Everything is a new
save, exactly as the revision-restore work says.

### D6 (DEC-258) — The read contract grows the state the listing already shows

The shell listing shows compile state; `PageEntry` does not. Dropping the column would make the page
editor a smaller view than the one it replaces. `PageReadHead` grows the image columns the store
already has (`RuntimeId`, `HasImage`, `HasDiagnostics`), `SqlitePageReadStore.ListAsync` selects
them, and `PageCatalogScope` maps them through `PageImageStatus.Describe` into one string property
on `PageEntry`, `State`, so there is still one spelling of "Compiled with warnings". The `.Class()`
half stays in `Web` for the shell's own list: the Sdk carries the fact, and a page marks it with
whatever its own markup or stylesheet wants — a `data-state` attribute needs no mapping at all.

`PageCatalogScope` needs the running runtime id for staleness; `PageEnvironment.Saver.RuntimeId` is
passed to it, as the shell editor already has it. The listing charges no source budget, because it
still reads metadata only.

Rejected: a second capability read just for state, which adds a read where the listing already knows
the answer. Rejected: a public `PageImageState` enum, which duplicates a Core/Web vocabulary in Sdk
for one property.

### D7 (DEC-264) — Scrub stays a shell route

Permanently removing history is not editing. `/edit/{**path}?cmd=scrub` becomes
`/admin/scrub/{**path}`: the same GET confirmation, the same typed key, the same POST, behind the
`Editor` policy and antiforgery. It is reserved, described in `ShellRoutes`, and linked from a
deleted row on `/pages` rather than offered as a page action. The code moves out of `EditorEndpoint`
without changing what it does, except that its POST answers with its own result and a link to
`/pages` instead of writing a `/edit` flash no view will read. A later job can decide whether the
page should own it.

### D8 (DEC-265) — `/edit` is retired, not redirected

The `/edit` and `/edit/{**path}` mappings go. `/edit` also leaves `ReservedPaths.Default` and
`ShellRoutes.All`, so it is no longer a shell file, a route policy or an assistant-protected key.
The page catch-all owns it like any other key: no page is stored there, so `GET` and `HEAD` answer
`404` through the ordinary missing-page path. A `POST` reaches the same `404` after the page
pipeline's ordinary checks, so an authenticated post without a valid antiforgery pair is still the
host's `400`, exactly as on any other page; there is no redirect shim and no `308` preserving an old
form body.

`/edit` is then an ordinary page key. If somebody stores a page at `/edit`, it serves like any other
page, with the guard that page writes for itself. The assistant may write it too; that is the same
trade every other stored page already has (ARCH-15).

The destructive scrub action is not left to a missing key: it moves to `/admin/scrub/{**path}` before
the editor maps are removed, and the deleted-page row on `/pages` links it.

### D9 (DEC-266) — The page answers in place

- A save renders the editor again with diagnostics, the new hash and the text the author submitted.
- A stale save is `409` with the editor, the submitted text and the current source.
- Save-and-view redirects to the page only when the save has no errors.
- A delete returns to the listing with the tombstone row; a restore and an install render their
  confirmations and then their answer.
- No page-session key is written, so the capability does not need a flash and the page does not need
  to read another page's session.

`PageEndpoint` already refuses an authenticated `POST` without a valid antiforgery pair before the
page runs, so the page's forms carry `ctx.AntiforgeryField` and rely on the host check.

### D10 (DEC-256) — IDEA-028 is the recovery path, and it is live

`/admin/restore` installs every seed, including `/pages`, `/editor.js`, `/editor.css` and
`/_layout`, from the bundled source. It is reserved, behind the `Editor` policy, and linked from
`/admin` and `/status`. It does not delete non-seed pages and does not scrub history. The page draws
its own document and loads no stylesheet or script, because the shell's document always links
`/site.css` and one rule there would hide the page that can repair it (DEC-256).

It landed on 2026-11-13, so Phase 4 no longer waits for it and there is no redirect fallback once
`/edit` goes.

### D11 (DEC-267) — One seed helper, one saver

`PageAssetSeeder` knows how to read a bundled seed, and `/admin/restore` already owns the rest of
the rule: `RestoreEndpoint.PlanAsync` compares every seed with the live head, so the preview can say
`current`, `changed`, `missing` or `deleted`, and its POST saves each one that differs and reports
`restored`, `saved with errors`, `failed` or `unchanged`. That comparison and both vocabularies move
into a small `PageSeedInstaller` in `Pages`; the page capability and `RestoreEndpoint` both call it,
and the restore keeps its preview and summary. There is then one rule for "does this seed differ" and
one for "what happened", not two.

There is no batch store in the emergency path and no new insert path in the capability. Sequential
saves through `PageSaver` are the correct scale for the seeds the site ships, and a partial result is
easier to report than a rolled-back recovery.

### D12 (DEC-268) — The editor page is seeded, editable and recoverable

`/pages` is a seed like `/help` and `/about`. The owner can edit the editor with the editor, which is
the point of the site; a bad save that breaks it is exactly what `/admin/restore` exists for. The
page's own source is in `Pages/Seeds/pages`, so the emergency restore can put it back without a
special case. Its stylesheet and module are seeds too.

## The capability in detail

### Registration

`PageEndpoint.HandleAsync` gains a `PageEditor` parameter from the request scope and passes it into
`PageChain`. `PageChain.CreateLayer` constructs the capability for the content layer:

```csharp
if (!isLayout && string.Equals(key.Key, PageEditorScope.EditorKey, StringComparison.Ordinal)) {
    capabilities.Add<IPageEditor>(new PageEditorScope(key, editor, seeds, _user, writable: !_isHead, Deadline));
}
```

The exact plumbing can differ, but the invariant cannot: no other key and no layout gets the
capability. `PageEditorScope` follows `PageWriterScope`'s `RenderBoundCapability` pattern for
abandonment, deadline and the one-action flag.

### Refusals

| Situation | Result |
| --- | --- |
| Anonymous render | `UserFacingException("Sign in to edit a page. Nothing was saved.")` before compile/store |
| Write during a `HEAD` | `InvalidOperationException`; no write |
| Ended or timed-out render | `InvalidOperationException`; no write |
| Second action in one render | `InvalidOperationException`; first action stands |
| Unaddressable key | `ArgumentException`; no read or write |
| Reserved key | `InvalidOperationException`; no read or write |
| Delete of `/pages` | `InvalidOperationException`; the page hides the button and points at recovery |
| Stale conditional save | `PageEditResult.Stale`, no write |
| Missing revision or non-seed | `null`, no write |
| Missing page on delete | `PageDeleteResult(Deleted: false, WasLive: false)` |

The `UserFacingException` is the visitor's state, as `PageWriterScope` already decides (DEC-190). The
rest are page bugs and are `InvalidOperationException`, not a second user-facing gate.

### The write sequence

1. Check authority and the path; normalise through the host.
2. For a save, call `PageSaver.SaveIfRevisionAsync(key, template, expectedHash)`; the method owns
   the context, so the save survives an ended render.
3. For a delete, call `PageEditor.DeleteAsync`.
4. For a restore, read the revision, check the key, then save its source with the expected head hash.
5. For an install, ask `PageSeedInstaller` for the source and comparison, then save if needed.
6. Map the diagnostics to the Sdk `PageDiagnostic`, exactly as `PageWriterScope` does, so the page
   sees the same `{code}: {text}` form as a self-save and the assistant.

The save's token is `CancellationToken.None` with the render's deadline, as `PageWriterScope` does:
the deadline bounds the wait for a compile slot, not the author's edit (DEC-191). Delete, restore
and install are quick enough to use the render's deadline as their token.

## The page

### URL shape

| URL | What it shows |
| --- | --- |
| `GET /pages` | The listing, the create/open form, the seeded/deleted groups and their actions |
| `GET /pages?edit=/about` | The editor form, with history, delete, install (if seeded) and view links |
| `GET /pages?edit=/about&cmd=delete` | The delete confirmation |
| `GET /pages?edit=/about&cmd=install` | The seed install's diff and confirmation |
| `GET /pages?page=/about` | Source and history, as `/pages` already does |
| `GET /pages?page=/about&rev=<hash>` | One retained revision's source |
| `GET /pages?page=/about&from=<hash>&to=<hash>` | The diff between two revisions |
| `GET /pages?page=/about&rev=<hash>&cmd=restore` | The revision restore's diff and confirmation |

`POST /pages` answers the form's `cmd`: `new`, `save`, `save-and-view`, `delete`, `restore` and
`install`. A missing `cmd` on the editor form is a save, as it is today.

### Markup and module

The page reproduces the shell editor's shape, because `/editor.js` is unchanged in what it expects:

```html
<div class="editor">
    <div id="gutter" class="gutter" aria-hidden="true"><span>1</span>…</div>
    <textarea name="template" id="template" rows="10" cols="50" wrap="off" spellcheck="false">…</textarea>
</div>
```

- `ctx.Capabilities.GetRequired<IPageLayout>().AddStylesheet("/editor.css")` on the editor and
  history views, so the sheet is versioned through the asset map.
- `ctx.AllowScripts()` on the editor view, and `<script type="module" src="/editor.js"></script>`.
- A visible `<label for="template">`, the page's title, and the listing's table captions follow the
  accessibility rules the shell views already meet.
- Diagnostics are a `<ul class="diagnostics">` with `#L<n>` links, the shape `editor.js` reveals.
- The page uses `ctx.AntiforgeryField` in every form and `ctx.SetStatus` for `409`/`400`.
- The page writes all page source with `ctx.Write`, never `WriteRaw`, except the static markup it
  authors. A source view is exactly the place a stored template must not run.

`editor.js`'s caret key currently uses `location.pathname`, which would make every edited page share
one caret in the tab. Change it to the `edit` query value when there is one, and keep the path
otherwise.

### Listing actions

- Every addressable live page: **Edit** (`?edit=`), **History** (`?page=`), **View** (the page URL).
- A seeded page: **Install from seed** (`?edit=&cmd=install`), disabled when the live source already
  matches.
- A tombstone: **Recreate** (`?edit=`), **History**, **Scrub history** (`/admin/scrub/{**path}`).
- The create/open form posts `cmd=new` to `/pages`; an unaddressable or reserved key is refused
  before any redirect.
- The editor page's own row still offers Edit and History; it does not offer Delete.

## Shell changes

### Routing

- `Program.cs` removes the `/edit` and `/edit/{**path}` maps. There is no replacement mapping: the
  page catch-all `{**path}` answers those URLs, and with no page stored at the key the ordinary
  missing-page path gives `404`. It adds `GET`/`POST /admin/scrub/{**path}` behind the `Editor`
  policy, with antiforgery metadata on the POST route; `/admin/restore` is already mapped by
  IDEA-028.
- `EditorEndpoint` is deleted rather than reduced to redirects. Its scrub code moves to
  `ScrubEndpoint`; its views, flash, save, history, revision and diff code go.
- `/admin/restore` is the IDEA-028 endpoint. It draws its own plain document, calls the seed helper
  and `PageEditor`, and never touches the page pipeline.

### Reserved paths and shell records

`ReservedPaths.Default` gains `/admin/scrub` and loses `/edit`; `/admin/restore` and `/admin/changes`
are already there. `ShellRoutes.All` gains the scrub record and loses the `/edit` record;
`ShellRoutesTests` keeps the table and the reserved list equivalent in both directions.
`ReservedPathsTests` grows the new prefix, proves `/edit` and `/edit/anything` are no longer
reserved, and keeps `/pages` unreserved, because it is a stored page.

### Navigation and links

- `SiteNavigation.Links` points "Pages" at `/pages` and marks it active under `/pages`.
- `AdminNavigation.Nav` points "Page list" at `/pages`; "Restore seeds" and "Changelist" are already
  there.
- `StatusEndpoint` already links `/admin/restore`; the link stays, because `/status` is the bootstrap
  view that answers when `/pages` does not.
- `LocalRedirect.Safe` falls back to `/pages`; the login cancellation link, `ErrorMessage`'s
  remedies, the import and export views, the assistant views and the memory page point at `/pages`.
- `_layout`, `/help`, `/scratch`, `/recent` and `/pages` itself replace every `/edit` link with the
  matching `/pages` URL. `PageAuthorPrompt` names `/pages` as the owner's editor and stops calling
  `/edit` a shell file.

## The emergency restore, as this plan's prerequisite

[IDEA-028](../../jobs/README.md#closed) landed on 2026-11-13 with its own tests. This plan depends on its
behaviour, and on one piece of its code:

- `GET /admin/restore` lists every seed with current/changed/missing/deleted and one POST button.
- `POST /admin/restore` installs every changed, missing or tombstoned seed through the ordinary
  saver, skips matches, leaves non-seeds untouched and reports per-key outcomes.
- It is reserved, behind `Editor`, antiforgery-checked, linked from `/admin` and `/status`, and
  draws a plain document that loads no seeded asset.
- It can repair a `/pages` that no longer compiles, a `/_layout` that was deleted, and a broken
  `/editor.js` or `/editor.css`.
- Its `PlanAsync` and its state/outcome records are the seed comparison this plan extracts into
  `PageSeedInstaller` (D11, DEC-267). Phase 2 does that extraction; Phase 4 no longer waits for anything.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageEditor.cs` | New: the named-key capability, its `Resolve`, `SeedPaths`, `SeedSourceAsync`, `SaveAsync`, `DeleteAsync`, `RestoreRevisionAsync` and `InstallSeedAsync` |
| `src/uk.osric.asp.Sdk/PageEditResult.cs` | New: `PageEditOutcome` (`Saved`, `Stale`, `Unchanged`) and `PageEditResult` (`Outcome`, `Hash`, `Diagnostics`); `PageKeyResolution` and `PageDeleteResult` in their own files |
| `src/uk.osric.asp.Sdk/PageEntry.cs` | `State`, set by the host; the positional constructor stays |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | The new types and members |
| `src/uk.osric.asp.Web/Pages/PageEditorScope.cs` | New: the authority, path rules, one action per render and `RenderBoundCapability` plumbing |
| `src/uk.osric.asp.Web/Pages/PageEditorOptions.cs` | New: `Pages:Editor:Key`, normalised with the default for a value that names no page |
| `src/uk.osric.asp.Core/Pages/PageSourceChange.cs` | New: the one "would this change anything?" comparison, used by the shell editor, the assistant and the capability |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Builds the editor scope for `/pages` only; passes the running runtime id to the catalog |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | Takes the scoped `PageEditor` and passes it to the chain |
| `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs` | Maps the read head's image columns to `PageEntry.State`; takes the runtime id |
| `src/uk.osric.asp.Web/Pages/IPageReadStore.cs`, `SqlitePageReadStore.cs` | `PageReadHead` carries `RuntimeId`, `HasImage`, `HasDiagnostics`; the listing query selects them |
| `src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs` | New: the comparison and outcome rule extracted from `RestoreEndpoint.PlanAsync`, shared by the capability and the restore |
| `src/uk.osric.asp.Web/Pages/PageDiagnostics.cs` | New: the core-to-Sdk diagnostic mapper, shared by `PageWriterScope` and the editor scope |
| `src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs` | Remains the file reader; the helper owns comparison and outcome, not the seeder |
| `src/uk.osric.asp.Web/Editor/PageEditor.cs` | Its conditional save delegates to the singleton `PageSaver`, and `GetRevisionAsync` is the one page-scoped revision read; the capability no longer needs a scoped store for a save |
| `src/uk.osric.asp.Web/Editor/PageSaver.cs` | New `SaveIfRevisionAsync`: compile, conditional store, diagnostics and metrics, with the render's deadline bounding only the compile-slot wait |
| `src/uk.osric.asp.Web/Pages/IPageRevisionStore.cs`, `SqlitePageRevisionStore.cs` | A conditional save on its own context, sharing the head check with `SqlitePageStore.TrySavePageAsync` |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | Deleted; its scrub code moves to `Admin/ScrubEndpoint`, and its views, flash and save go |
| `src/uk.osric.asp.Web/Admin/ScrubEndpoint.cs` | New: the moved GET confirmation and POST, `/admin/scrub/{**path}` |
| `src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs` | IDEA-028, landed: the preview and the one-button restore; this plan moves its comparison into the helper and leaves its views alone |
| `src/uk.osric.asp.Web/Program.cs` | Remove the `/edit` maps; add scrub and emergency restore |
| `src/uk.osric.asp.Web/Pages/ReservedPaths.cs`, `Assistant/ShellRoutes.cs` | `/admin/restore` and `/admin/scrub` are added; `/edit` leaves both lists |
| `src/uk.osric.asp.Web/Http/PageUrls.cs` | `/pages?edit=`, `?page=`, `?rev=`, `?from=&to=`, the confirmations, `/admin/scrub` and `/admin/restore`; the editor URL helpers go |
| `src/uk.osric.asp.Web/Html/SiteNavigation.cs`, `Admin/AdminNavigation.cs`, `Metrics/StatusEndpoint.cs` | Owner links move to `/pages`; `/admin` and `/status` link the recovery route |
| `src/uk.osric.asp.Web/Authentication/LocalRedirect.cs`, `LoginEndpoint.cs` | Fallback and cancel link move to `/pages` |
| `src/uk.osric.asp.Web/Admin/ImportEndpoint.cs`, `ExportEndpoint.cs`, `Assistant/AssistantEndpoint.cs`, `Assistant/AssistantMemoryEndpoint.cs`, `Errors/ErrorMessage.cs` | Every `/edit` link becomes `/pages` |
| `src/uk.osric.asp.Web/Pages/Seeds/pages` | The listing grows the editor, confirmations, history/diff/restore and seed install; it loads `/editor.css` and `/editor.js` and opts into scripts |
| `src/uk.osric.asp.Web/Pages/Seeds/editor.js` | The caret key includes the edited path; no other behaviour change |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The editor section, the recovery section and every `/edit` link |
| `src/uk.osric.asp.Web/Pages/Seeds/_layout`, `scratch`, `recent` | Every `/edit` link becomes the matching `/pages` URL |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The shell-file sentence and the editor vocabulary; `Version` moves from whatever trunk holds when this lands (`page-author-v24` today) |
| `test/uk.osric.asp.Web.Tests/PageEditorScopeTests.cs` | New: the authority table, stale save, delete, restore, install and seed paths |
| `test/uk.osric.asp.Web.Tests/PageLayoutChainTests.cs` | The capability is present on `/pages` and absent on a layout and another page |
| `test/uk.osric.asp.Web.Tests/PageCatalogScopeTests.cs`, `SqlitePageReadStoreTests.cs` | State and class from the image columns; no extra read |
| `test/uk.osric.asp.Web.Tests/EditorEndpointTests.cs` | Deleted; the view, save, history and diff tests move to the page suite |
| `test/uk.osric.asp.Web.Tests/ScrubEndpointTests.cs` | New: the moved confirmation, typed key and refusals |
| `test/uk.osric.asp.Web.Tests/RestoreEndpointTests.cs` | IDEA-028, landed: preview, one-button restore, no-op, non-seed, failure; the extraction keeps them passing |
| `test/uk.osric.asp.Web.Tests/ShellRoutesTests.cs`, `ReservedPathsTests.cs`, `SiteNavigationTests.cs` | The new routes, `/edit` and `/pages` both unreserved, and the owner links |
| `test/uk.osric.asp.Web.Tests/PageAssetSeederTests.cs` | The closed seed list still matches; `/pages` names `IPageEditor` and `/help` names `/pages` |
| `test/uk.osric.asp.Web.IntegrationTests/PageEditorTests.cs` | New: the full `/pages` round trip and the stale `409` |
| `test/uk.osric.asp.Web.IntegrationTests/RestoreEndpointTests.cs` | IDEA-028, landed: a broken `/pages`, sheet, module or layout comes back on one POST |
| `test/uk.osric.asp.Web.IntegrationTests/EditorEndpointTests.cs`, `EditorAuthenticationTests.cs` | `/edit` is a `404` and becomes a page when one is stored; login and the `/pages` guard |
| `test/uk.osric.asp.Web.BrowserTests/EditorFlowTests.cs`, `EditorBrowserTests.cs` | The same flows from `/pages?edit=`, including the gutter, caret and diagnostic link |
| `docs/pages.md` | A tenth capability, the read state, the editor page and its URL |
| `docs/shell.md` | The editor section, the routes, the nav, the recovery links and the retired `/edit` key |
| `docs/testing.md` | The capability, page, shell and browser suites |
| `docs/decisions.md` | DEC-259–DEC-268, taken at the merge (DEC-09) |
| `docs/jobs/IDEA-017.md`, `docs/jobs/README.md` | Close IDEA-017 and remove it from the table; IDEA-028 closes on its own |
| `docs/plans/archive/page-editor.md` | Move this plan here when it lands |

## Phases

### Phase 0 — IDEA-028 (landed 2026-11-13)

The emergency restore is live: `/admin/restore`, its reserved and shell-route record, the `/status`
and `/admin` links, and its unit and integration tests. Its comparison and outcome records are the
seed rule Phase 2 extracts (D11, DEC-267).

### Phase 1 — the read state (landed 2026-11-13)

Extend `PageReadHead`, `SqlitePageReadStore.ListAsync` and `PageCatalogScope` so a listing row
carries the compile state and class the shell listing shows. Keep the read public and bounded.

**Done when** `PageCatalogScopeTests` proves a stored image's state, a stale runtime and a no-code
source map to the same words the shell `PageEditor.ListAsync` would produce; the listing makes no
source read; no new store query is added.

### Phase 2 — the capability (landed 2026-11-13)

Add `IPageEditor` and its values to the Sdk, `PageEditorScope` to Web, the `PageSaver` conditional
save, the seed helper plumbing, and the `/pages`-only registration. Keep `IPageWriter` untouched.

Landed: `IPageEditor`, `PageKeyResolution`, `PageEditResult`/`PageEditOutcome` and
`PageDeleteResult` in the Sdk with their `PublicAPI.Unshipped.txt` lines; `IPageRevisionStore.TrySaveAsync`
over one `SqlitePageStore.TryStoreRevisionAsync`; `PageSaver.SaveIfRevisionAsync` owning the
conditional algorithm, with `PageEditor` and `PageWriterScope` delegating to it and `PageEditor` no
longer taking the image builder; `PageSeedInstaller` owning the comparison and outcome rule for both
`/admin/restore` and the capability; `PageDiagnostics` as the one core-to-Sdk mapper; `PageChain`
registering the scope for `/pages` only. `PageWriterScopeTests`, `PageSaverTests` and
`SqlitePageRevisionStoreTests` keep the single-algorithm proof.

**Done when** `PageEditorScopeTests` covers the refusal table, a conditional save that stores and one
that returns `Stale` without storing, a tombstone delete, a revision restore, a seed install and a
matching seed that is not written; a save that has begun is stored after the render's deadline; a
layout and a non-editor page do not get the capability; an anonymous render compiles and stores
nothing; `PageSaverTests`, `SqlitePageRevisionStoreTests` and `PageEditorTests` prove the conditional
save still has one algorithm; the Sdk public API is updated and the tests still pass.

### Phase 3 — the page (landed 2026-11-13)

Rewrite `Pages/Seeds/pages` as the listing and editor, load `/editor.css` and `/editor.js`, opt into
scripts, add the confirmations, diff and history, and update `editor.js`'s caret key. Move the view
tests into the page suite and add the browser round trip.

Landed: the page is the listing, the editor, the source-and-history view, the revision view, the diff
and the three confirmations, one action per request. The request is dispatched by one small function
per action; `PageEditorScope.SaveAsync` gained the null expectation a new page's form carries, which
the browser suite found the hard way — without it the page could not create anything. It names itself — every URL is built from
`ctx.Path`, so `Pages:Editor:Key` can move the editor without editing the page — and it writes every
value through `ctx.Write`, never a raw interpolation of stored source. `IPageEditor.SeedSourceAsync`
joined the capability because the install confirmation needs the bundled source to diff against and a
page has no other way to read it. The request is dispatched by one small function per action
(`NewPageAsync`, `SavePageAsync`, `PostActionAsync`, `OpenEditorAsync`, `OpenReadAsync`) rather than a
200-line chain; each sets `stage`, and the markup below switches on it. `editor.js` keys the caret on
the `edit` query value, so two tabs on two pages keep two carets. `/help` teaches `?edit=`, the
confirmations and `IPageEditor` in the same change.

**Done when** `PageEditorPageTests` proves the listing's groups, the editor's source and head hash,
a save, a `409` that keeps the text, a save with an error, save-and-view's redirect, delete, history
and restore links, the diff, the install confirmation, the restore action, the shell-file refusal,
the anonymous redirect, the create form and a page stored at a key other than `/pages`.

**Done when** a signed-in owner creates, opens, edits, saves, saves-and-views, sees diagnostics and
history, compares two revisions, deletes and recreates, restores a revision and installs a seed from
`/pages`; an anonymous visitor is sent to sign in; a stale save is `409` with the submitted text; the
gutter and caret still work; the seeded page compiles and its tests pass.

### Phase 4 — the shell stops being the editor (landed 2026-11-13)

Landed: scrub at `/admin/scrub/{**path}` with its own `ScrubEndpoint`; `/edit` gone from routing, the
reserved list and `ShellRoutes`, and from `appsettings.json`'s copy of the list; `EditorEndpoint`
deleted with `EditorEndpointTests` and the browser `EditorFlowTests`, whose flows the page suite
covers; every shell link, error remedy, assistant tool and seed pointed at the editor. The editor's
key left the literals behind too: `IContext.EditorPath`, `PageUrls`' methods take the key, and
`EditorPaths.Of(services)` is how a shell view reads it. `LocalRedirect` falls back to `/` rather
than the editor, which is the owner's call.

This phase started only after Phase 0 shipped. `EditorEndpoint` and its views are gone.

**Done when** `GET` and `HEAD /edit` answer `404` through the page catch-all, and `/edit/anything`
does too; an anonymous `POST` reaches the same `404`, while an authenticated post without antiforgery
is the host's ordinary `400`; storing a page at `/edit` makes it serve; `/admin/scrub` keeps the
typed confirmation; every shell and page link points at `/pages`; reserved paths and `ShellRoutes`
are equivalent; the old editor view tests are either ported or deleted with their reason.

### Phase 5 — documents and the manual (landed 2026-11-13)

Landed: `PageAuthorPrompt` is `page-author-v25` and lists the current shell files, saying the editor
is not one of them; `/help` has taught `IPageEditor`, `?edit=`, the confirmations, `/admin/scrub` and
`/admin/restore` since phase 3; [pages](../pages.md) gains the tenth capability and `EditorPath`;
[the shell](../shell.md) says there is no editor in the shell and what the capability replaces;
[testing](../testing.md) names `PageEditorScopeTests`, `PageEditorPageTests` and the browser suite.
IDEA-017 is closed. The decision numbers are taken when this merges (DEC-09), not here.

Update `/help`, `PageAuthorPrompt` (including its version), `docs/pages.md`, `docs/shell.md`,
`docs/testing.md` and the decision rows; close IDEA-017 and remove the retired key from the
documentation. Move this plan to `docs/plans/archive/`.

**Done when** no document describes `/edit` as the editor, `/help` teaches `/pages?edit=`,
`/admin/scrub` and `/admin/restore`, the prompt names the shell-file records correctly, and the
manual's editor section matches the page's actions and refusals.

## Testing

### Unit

- `PageEditorScopeTests`: every row of the refusal table; a save at another key; diagnostics and hash;
  stale save; tombstone delete; missing delete; editor self-delete; revision restore by hash with a
  wrong path; seed install; matching seed; non-seed install; one action per render; `HEAD`,
  abandoned and deadline refusals; a started save that is stored after the render has ended.
- `PageSaverTests` / `SqlitePageRevisionStoreTests` / `PageEditorTests`: the conditional save stores
  only while the head matches, returns stale without a write, and owns its context; `PageEditor`
  delegates to the same algorithm.
- `PageLayoutChainTests`: capability presence on `/pages` only, absent from `/_layout` and from
  `/about`.
- `PageCatalogScopeTests` / `SqlitePageReadStoreTests`: state, class and staleness from the image
  columns; a no-code page reads "Stored as source"; the listing still charges no source budget.
- `PageAssetSeederTests`: the seed list is unchanged; `/pages` names `IPageEditor` and
  `IPageCatalog`; `/help` names `/pages`, `/admin/restore` and `/admin/scrub`.
- `ScrubEndpointTests`: the moved GET, typed confirmation, wrong key, live page and missing page.
- `RestoreEndpointTests` (landed): preview states; restore changed, missing and tombstoned; skip
  matching and non-seed; one failure does not stop; summary counts.
- `ShellRoutesTests` / `ReservedPathsTests` / `SiteNavigationTests`: new routes, `/edit` and `/pages`
  both unreserved, owner links.

### Integration

- `PageEditorTests`: a signed-in owner lists, opens, saves, saves-and-views, sees a compile error,
  compares revisions, restores one, installs a seed, deletes and recreates; an anonymous visitor is
  redirected; a stale save is `409` and the submitted text is present.
- `RestoreEndpointTests` (landed): `/pages` saved into a compile error is recovered by one POST to
  `/admin/restore`; a deleted `/_layout` and a broken `/editor.js` recover; a non-seed page stays.
- `EditorAuthenticationTests`: `/pages?edit=` redirects an anonymous visitor to sign-in and writes
  nothing on an anonymous POST.
- `EditorEndpointTests`: `GET`/`HEAD` `/edit` and `/edit/anything` are `404`; an anonymous `POST`
  reaches the missing-page `404`, while an authenticated post without antiforgery is `400`; storing
  a page at `/edit` makes it serve; the retired key is not a shell file or a reserved page.
- `SiteChromeTests` / `SiteLayoutRecoveryTests`: the shell and the recovery routes answer when the
  layout is gone.

### Browser

- `EditorFlowTests` and `EditorBrowserTests` run against `/pages?edit=`: the line gutter follows
  input, the caret survives a save, a diagnostic link reveals the line, save-and-view lands on the
  page, and the stale `409` keeps the draft.

### Commands

`make test` for the unit and integration suites, `make test-browser` for the browser cases, and
`make format-check` for the style and SPDX rules. The emergency restore and the page editor are
proved by behaviour, not by a browser test that restates the markup.

## Documentation

- `/help` is the author manual and must change in the same commit: the editor section becomes
  `/pages`, the recovery section names `/admin/restore` and `/admin/scrub`, and the "Keeping this
  page current" section points at `/pages?edit=/help`.
- `docs/pages.md` gains `IPageEditor` as the tenth capability and describes `PageEntry.State`.
- `docs/shell.md` describes the new routes, the retired `/edit` key, the navigation and the recovery
  links.
- `docs/testing.md` names the new suites and what each proves.
- `docs/decisions.md` carries the numbered rows, DEC-259–DEC-268, taken at the merge; D6 and D10
  were numbered earlier with the phases that landed them (DEC-258 and DEC-256).
- `docs/jobs/IDEA-017.md` is removed from the job list when the work lands; IDEA-028 closes with its
  own change. The plan moves to `docs/plans/archive/`.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| A stored page gains arbitrary write authority | `IPageEditor` is registered only for `/pages`; reserved keys refused; a layout never sees it; a `HEAD` and an anonymous render get no writable handle; one action per render |
| The editor page saves itself into a compile error and the owner is locked out | `/admin/restore` is live and restores `/pages`, `/editor.css`, `/editor.js` and `/_layout`; `/status` links it |
| The editor page deletes itself and the owner is locked out | The capability refuses a delete of `/pages`; the row has no Delete; recovery is still available |
| A stale tab overwrites a newer save | Conditional save with the rendered hash; `409` with the submitted text and the current source |
| `/edit` disappears from a bookmark and an open tab | Accepted: there is no redirect, and the URL is a `404` until a page is stored there. The editor is `/pages`, and `/admin/restore` covers a broken editor |
| `/edit` becomes a free page key and the assistant can write it | Intentional; it is an ordinary stored page the assistant may write, like every other (ARCH-15). A guarded page is the only way to close it |
| The page editor loses the compile-state column | `PageEntry.State` is part of the read contract; the listing test proves the same words |
| Scrub is lost with the shell editor | It moves unchanged to `/admin/scrub/{**path}` and is linked from tombstones |
| Two save paths drift | The conditional algorithm moves to `PageSaver`, `PageEditor` delegates to it, and the seed helper shares the comparison and save rule with the emergency page |
| The Sdk change recompiles every stored image | That is the normal cost of an Sdk change (DEC-26); one deliberate release, with the new public API updated and the seed tests passing |
| A shell route and its reserved key drift | `ShellRoutesTests` pins both directions; `/admin/restore` and `/admin/scrub` land with their records |
| The page editor becomes a second assistant path | The assistant keeps `PageToolExecutor` and `PagePathPolicy`; the page capability is not offered to tools |

## Open questions

- **Scrub.** The plan keeps it shell at `/admin/scrub/{**path}`. Whether the page should own the typed
  confirmation, and whether it should be a fourth page action, is a decision for after the editor has
  run for a release.
- **A future `/edit` page.** The key is free after this change, but what it should become — a
  redirect page, a landing page that points at `/pages`, or something else — is for whoever writes
  it. This plan deliberately leaves it empty and `404`.
- **The seed count.** The emergency restore is sequential because the shipped seed list is small. If
  it grows enough for a single POST to be slow — it ships seventeen seeds today — the batch store is
  the next place to look; it is not part of this plan.
