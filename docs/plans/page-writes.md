<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page may write the store

| | |
| --- | --- |
| Status | Approved 2026-11-15; in flight on `page-writes`. The branch carries trunk's seed provenance and `/pages` rewrite, so the plan is read against that |
| Size | M — one capability replaces two, one registration gate and one delete protection removed, a public type renamed, and the tests, the manual, the prompt and the jobs for each |
| Spike | None. Every claim about what exists was read off `trunk` on 2026-11-15 and re-checked after merging `1aade5d`, which landed seed provenance and the editor rewrite |
| Decisions | At the merge: **pages-any-page-may-write-the-store** is new; the entry that made the editor's registration the authority is retired and replaced by it; `pages-a-page-may-save-itself` is edited in place |
| Backlog | [IDEA-043](../jobs/IDEA-043.md) is this step; [IDEA-047](../jobs/IDEA-047.md) is the batch, split out of it |

## The short version

Two capabilities become one. `IPageWrites` is the store's write, offered to every page that is not a
layout, and the authority it checks is the signed-in visitor's rather than the page's. The self-save
stays, as the bound member it is today; the named, conditional save and the rest of the editor's
members sit beside it on the same interface, and one render still makes one write.

The batch — several pages, a named policy, one report — is
[IDEA-047](../jobs/IDEA-047.md), not this step. It is the half that needs a vocabulary of policies, a
transaction boundary and a report value; this half only removes a gate and merges two handles that
existed apart because of that gate.

The IDE's Save all and the assistant's multi-file edit found the one-save rule, and an editor sub-route
found the one-key rule ([IDEA-043](../jobs/IDEA-043.md)'s origin). Both narrowings were deliberate: a
handle with a key parameter was kept from every page because every stored page would then have been a
writer of the whole store, and a render was kept to one save because a render that wrote twice could
leave the site half-changed. The owner has decided the first trade is worth making — nothing about
editing is special to one page, because the authority is the signed-in visitor — and the batch, which is
the answer to the second, is [IDEA-047](../jobs/IDEA-047.md) on its own branch.

## What the owner decided

| Question | Answer | What it settles in this plan |
| --- | --- | --- |
| Merge the self-save into the write capability? | Yes: "if a page can save any page it can certainly save itself" | `IPageWriter` goes; a page saves itself through the same interface it saves any page through |
| The merged capability's name | `IPageWrites` | The renamed type, the seeds, the manual, the prompt and the SDK public API |
| A bound self-save, or one save only? | Keep the bound one | Two saves on one interface, each with its own answer (below) |
| One write per render | Keep it, until the batch | Every member shares one action; [IDEA-047](../jobs/IDEA-047.md) removes the rule |
| The editor key's delete protection | Remove it | `DeleteAsync` treats `Pages:Editor:Key` like any other key; `/admin/restore` is the way back |
| `Pages:Editor:Key` itself | Keep it | It stops deciding authority and keeps deciding where the editor is served |
| The job | Split it | This branch rewrites IDEA-043 and files IDEA-047 |
| The record | Retire the old entry and name the new rule | The merge writes three changes to [decisions](../decisions.md) (below) |

## What exists today

| | `IPageWriter` | `IPageEditor` |
| --- | --- | --- |
| Members | `SaveAsync(template)` | `Resolve`, `SeedSourceAsync`, `SaveAsync(key, template, expectedHash)`, `AdoptSeedAsync`, `DeleteAsync`, `RestoreRevisionAsync`, `InstallSeedAsync` |
| Answer | `PageSaveResult` — hash and diagnostics | `PageEditResult` — Saved/Stale/Unchanged, hash, diagnostics; `PageDeleteResult` |
| Conditional? | No: it always writes | Yes: the head hash the form was rendered from, and `null` means "create only" |
| Offered to | every content layer (`PageChain.CreateLayer`) | the content layer whose key equals `Pages:Editor:Key` and no other |
| Bound to a key? | yes, `ctx.Path`, with no key parameter | no: every call names a key |
| Refusals | anonymous (`UserFacingException`), `HEAD`, ended render, second save (`InvalidOperationException`), reserved key (unreachable, because reserved keys never render) | anonymous, `HEAD`, ended render, second action, unaddressable key, reserved key, and a delete of the editor itself |

Both already end in the one compile-then-store path, [`PageSaver`](../../src/uk.osric.asp.Web/Editor/PageSaver.cs);
both save paths are handed `CancellationToken.None` and the render's deadline, so a started save is
never abandoned (`pages-a-page-may-save-itself`).

The chain's registration is the whole of the authority rule:

```csharp
capabilities.Add<IPageWriter>(new PageWriterScope(key, _environment.Saver, _user, writable: !_isHead));

if (_editor is { } editor && string.Equals(key.Key, _environment.Editor.Key, StringComparison.Ordinal)) {
    capabilities.Add<IPageEditor>(new PageEditorScope(
        key, editor, _environment.Editor.Key, _user, writable: !_isHead, _environment.Reserved, _environment.Seeds));
}
```

Trunk moved under the plan while it waited. The seed-provenance work removed `SeedPaths()` from the
interface, added `AdoptSeedAsync`, took `PageSeeds` in the register's place and rewrote the editor seed
around `SeedCommands`; the chain's call above is that version. The merge keeps all of it: the two saves
join the seed members rather than replacing one, and the scope keeps its `PageSeeds` dependency.

`Pages:Editor:Key` is read in four places beyond that: `ctx.EditorPath` (`SimpleContext`), every
remedy link through [`EditorPaths.Of`](../../src/uk.osric.asp.Web/Pages/EditorPaths.cs), the
`?edit=`/`cmd=` URLs `PageUrls` builds, and the delete refusal inside `PageEditorScope`.

## The design

### 1. One capability, offered to every content layer

`IPageEditor` and `IPageWriter` become `IPageWrites`, and the registration gate goes. Every content
layer is offered it; a layout is still offered neither write. The scoped `PageEditor` (the store's
read/write facade the capability is built on) stays what it is, and the chain keeps its optional
parameter: a host that supplies no page store offers no write at all, which is a host shape, not a
per-page rule.

```csharp
// Every content layer, whatever its key: the visitor is the authority, not the page. A layout is
// markup and is offered neither write (`pages-layouts-are-markup`).
if (!isLayout && _editor is { } editor) {
    capabilities.Add<IPageWrites>(
        new PageWritesScope(key, editor, _user, writable: !_isHead, _environment.Reserved));
}
```

`IPageWriter`'s separate scope goes with its interface: the bound save moves onto
[`PageEditorScope`](../../src/uk.osric.asp.Web/Pages/PageEditorScope.cs), which is renamed, and which
already has the anonymous, `HEAD`, ended-render and one-action checks the bound save needs.

### 2. Two saves, two answers

The owner kept both, and the interface says why each exists:

- **`SaveAsync(template)`** — this page's own source, bound to `ctx.Path`, unconditional, answering
  `PageSaveResult` (hash and diagnostics). It is the recovery path: one call, no key to get wrong,
  and the answer a page shows about itself. `/scratch` is the worked example.
- **`SaveAsync(key, template, expectedHash)`** — any key the shell does not route, conditional on the
  head the form was drawn from, answering `PageEditResult` so a stale or unchanged save is a value
  rather than an exception.

The two cannot drift into two save algorithms: both call `PageEditor`, which is `PageSaver`, which is
one compile-then-store. `PageSaveResult` therefore survives; only the interface that produces it
changes.

### 3. The authority is the visitor; the refusals that stay

Nothing about *who may write* changes except the page no longer being asked. In order, every write:

1. refuses an anonymous render with a `UserFacingException` — "Sign in to edit a page. Nothing was
   saved." — before anything is compiled or stored;
2. refuses a `HEAD` (it runs once per validator, so a write would count as a visit);
3. refuses an ended or timed-out render;
4. refuses a second write in one render;
5. for the named members, resolves the key: an unaddressable key is an `ArgumentException`, a reserved
   key is an `InvalidOperationException`;
6. for `DeleteAsync`, no longer refuses `Pages:Editor:Key`.

`Resolve` and `SeedSourceAsync` stay reads and stay offered to an anonymous render, exactly as
`IPageCatalog` is: the read is as public as the page that calls it
(`pages-reading-the-store-is-public`), and the write is what needs an identity.

### 4. One write per render, until the batch

The scope keeps one action counter, and every member spends it — the bound save, the named save, the
delete, the restore and the install. This is a deliberate tightening in one corner: today the editor
page holds both capabilities and so two counters, and could save itself *and* edit another page in one
render. After the merge it makes one write like every other page. No seeded page did that, and it is
the rule the old comment intended.

[IDEA-047](../jobs/IDEA-047.md) removes the rule and puts a policy in its place: all or nothing, stop
at the first refusal, or save what can be saved and report each refusal.

### 5. The editor key keeps one job and loses two

`Pages:Editor:Key` is still a policy value — a name that decides routing — and is still required and
validated at startup. What changes is what it decides:

- it no longer decides **who is offered the capability** (the gate is gone);
- it no longer decides **what may be deleted** (the protection is gone, by the owner's answer);
- it still decides **where the editor is served**, which is what `ctx.EditorPath`, the shell's remedy
  links, `PageUrls.Editor` and `/admin/restore` all read through `EditorPaths.Of`.

`PageEditorOptions`' doc comment shrinks to that one job. Nothing in configuration changes, so a
partial production override still merges as it did.

Deleting the editor is now a page operation like any other: the tombstone keeps its history, `/pages`
answers `404`, and `/admin/restore` — a shell route, so it answers when the page pipeline cannot —
reinstalls the seeded editor. The seeded `/pages` hides the Delete action on its own row, so the
common way to reach that state is gone; the capability is not where the guard lives.

### 6. The rename, and what it breaks

`IPageEditor` and `IPageWriter` are public types every stored page compiles against, so a rename is a
site-wide recompile. The repository's own seeds (`/pages`, `/help`, `/scratch`), the assistant prompt's
worked example and the tests move in the same commit; pages stored outside the repository do not.

The pages that name `IPageEditor` today — at least `/pages` itself and the owner's `/ide-plan` — stop
compiling until they are edited, which is a stored page with a compile error rather than a broken
site: `/pages` reads and shows source through `IPageCatalog`, which needs no compile, so every broken
page is repairable in the editor. If `/pages` is the broken one, `/admin/restore` reinstalls the seed.
The plan says this out loud because the rename is the only irreversible part of the step.

`PageKeyResolution`'s doc comment names `IPageEditor.Resolve`; it moves to `IPageWrites.Resolve`.

### 7. What does not move

Nothing else in the contract. A layout gets no write. A reserved key is refused. A `HEAD` gets the
read members. An anonymous render is refused before anything compiles. A page's write still belongs to
the next request, and a started save still finishes. A worker is still offered no write
(`pages-a-worker-gets-no-response`). The shell's own writes (import, export, restore, scrub) stay routes for the
reasons `pages-page-first` records.

## The contract

```csharp
/// <summary>Writes the site's page store: this page's own source, and any page the visitor may edit.</summary>
public interface IPageWrites {

    PageKeyResolution Resolve(string key);
    Task<string?> SeedSourceAsync(string key);

    /// <summary>Saves this page's own source as a new revision.</summary>
    Task<PageSaveResult> SaveAsync(string template);

    /// <summary>Saves a page, only while its head still has the expected revision.</summary>
    Task<PageEditResult> SaveAsync(string key, string template, string? expectedHash);

    Task<PageEditResult?> AdoptSeedAsync(string key);
    Task<PageDeleteResult> DeleteAsync(string key);
    Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash);
    Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash);
}
```

The interface's remarks state the three rules a page author has to know: the authority is the visitor,
the capability is offered to every page and to no layout, and one render makes one write. The manual
and the prompt reuse those sentences.

## What it touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageWrites.cs` | New: the merged capability, with both saves and the read members |
| `src/uk.osric.asp.Sdk/IPageEditor.cs`, `IPageWriter.cs` | Deleted |
| `src/uk.osric.asp.Sdk/PageKeyResolution.cs`, `PageSaveResult.cs` | Doc comments point at `IPageWrites` |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | The two interfaces' lines become `IPageWrites`'; `PageSaveResult` stays |
| `src/uk.osric.asp.Web/Pages/PageEditorScope.cs` → `PageWritesScope.cs` | Renamed, gains the bound save, loses `editorKey` and the delete refusal, keeps the `PageSeeds` dependency the seed-provenance work added, and loses `PageSaver` (it reaches it through `PageEditor`) |
| `src/uk.osric.asp.Web/Pages/PageWriterScope.cs` | Deleted; its tests and its checks move into the merged scope |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Offers `IPageWrites` to every non-layout layer; no key comparison |
| `src/uk.osric.asp.Web/Pages/PageEditorOptions.cs` | Doc comment: the key says where the editor is served, not who may write |
| `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` | Unchanged: the optional service and the signature stay, and a host without one offers no write |
| `src/uk.osric.asp.Web/Editor/PageEditor.cs` | Doc comment only: the conditional save is no longer "the editor alone's" |
| `src/uk.osric.asp.Web/Pages/Seeds/pages` | `IPageEditor` → `IPageWrites` and the handle's name follows; trunk's rewrite already hides the Delete action on the editor's own row |
| `src/uk.osric.asp.Web/Pages/Seeds/scratch` | `IPageWriter` → `IPageWrites`; the prose names the merged capability |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The two sections become one: the two saves, the visitor, the keys, one write, and the editor key's two lost jobs |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | `IPageWrites` in the section and the worked example; `Version` → `page-author-v34` |
| `docs/pages.md` | The `IPageWriter` and `IPageEditor` bullets become one; the worker paragraph names `IPageWrites` |
| `docs/architecture.md` | "offered `IPageEditor` by its key" becomes "offered `IPageWrites` like every page" |
| `docs/decisions.md` | At the merge only (below) |
| `docs/plans/ide-host.md` | This branch: slice 4 splits, and the names it intends are stated |
| `docs/jobs/IDEA-043.md`, `IDEA-047.md`, `README.md` | This branch: the job split |
| `docs/jobs/IDEA-025.md`, `IDEA-033.md`, `IDEA-036.md`, `IDEA-037.md`, `IDEA-042.md`, `docs/plans/page-apps.md` | Location lines and live specs name `IPageWrites`. IDEA-025's analysis of today's authority is rewritten, because "the writer is bound and the editor is offered to one key" is the rule this step removes; closed rows in the jobs index keep their history |

## Tests

Test-first, in the existing suites; one behaviour per test. The two scope suites become one.

| Behaviour | Test |
| --- | --- |
| A signed-in visitor saves its own source, bound to `ctx.Path` | existing `ASignedInVisitorSavesItsOwnSourceAsync`, `TheSaveIsBoundToThePagesOwnKeyAsync` |
| A page saves another page's key | new: `APageSavesAnotherPageAsync` |
| A named save is conditional; a stale one writes nothing | existing `AStaleSaveWritesNothingAsync`, `ASaveStoresTheSourceAndReportsTheRevisionAsync` |
| An expectationless save creates but never overwrites | existing `ANewPageIsSavedWithoutAnExpectationAsync`, `AnExpectationlessSaveCannotOverwriteAPageAsync` |
| An anonymous write stores and compiles nothing | existing pair from both suites, now one message |
| A `HEAD`, an abandoned render and a past deadline are refused | existing |
| One render makes one write, across both saves | existing `OneRenderMakesOneActionAsync` plus new `TheBoundSaveAndTheNamedSaveShareOneActionAsync` |
| The editor key is deletable; reserved keys are not | new `TheEditorKeyCanBeDeletedAsync`, existing `AShellFileIsRefusedAsync` |
| Every content layer is offered `IPageWrites`; a layout is not | `PageLayoutChainTests`, retitled |
| A host with no page store offers no write | `PageLayoutChainTests`, kept |
| The seeded editor still reads, saves, restores and installs | existing `PageEditorPageTests` and `Web.IntegrationTests/PageEditorTests` |
| A non-editor page writes another key over the real host | new integration test: a signed-in `POST` to a stored page saves `/target`, and the next request serves it |
| The manual and the prompt name the capability | existing seeder and prompt tests, strings updated |

The two delete-protection tests (`TheEditorCannotDeleteItselfAsync`,
`TheConfiguredKeyIsTheOneTheEditorCannotDeleteAsync`) are replaced by the one that proves the opposite.

## Documentation

- **`/help`** is the author manual and is a page (`product-manual-is-a-page`): one section for the
  capability, its two saves and their two answers, the visitor rule, the one-write rule, and the fact
  that the editor's key is only an address now — with `/admin/restore` named as the way back from a
  deleted or broken editor.
- **`PageAuthorPrompt`** gains the same words and the new type, with its `Version` bumped: the
  assistant is the second caller and must stop believing a page writes only itself.
- **[pages](../pages.md)** records the contract; **[architecture](../architecture.md)** stops saying
  the editor is offered the capability by its key.
- **[decisions](../decisions.md)** at the merge, below.

## The record at the merge

Three changes, and the citations that follow them.

1. **Retire the entry that made the registration the authority.** It moves to the `Retired` section
   with its replacement named, and the entry goes. Its subject — the write capability — continues
   under a name that states the current rule.
2. **Add pages-any-page-may-write-the-store.** The authority is the signed-in visitor's; the
   capability is offered to every page and to no layout; the visitor check is in the capability,
   before anything compiles; the key decides where the editor is served and nothing else; the editor's
   key is deletable and `/admin/restore` is the way back.
3. **Edit `pages-a-page-may-save-itself` in place.** The self-save is the bound member of
   `IPageWrites` rather than an interface of its own, the named save is conditional, the one-write
   rule stands until **pages-a-batch-names-its-policy** (047), and a started save is still never
   abandoned. `pages-delete-install-and-restore` and `pages-scratch-saves-itself` are checked and left
   alone.

The retired name is cited in ten backticked places outside the record: two in [pages](../pages.md),
one in [IDEA-037](../jobs/IDEA-037.md), six in the archived editor plan and one in the archived
edit-safety plan. The six headings and the row are repointed where a pointer is what they are, and the
lines that record *what that plan wrote at the time* say "the capability entry (since retired)" instead,
so the archive does not claim a plan wrote a name that did not exist yet. The archived naming plan's
mapping table names it without backticks and is left alone: it records which `DEC-` numbers became that
name, which is still true.

## The job split

This branch is documentation only, and it does three things:

- **[IDEA-043](../jobs/IDEA-043.md)** shrinks to this step: the widened authority, the merged
  capability, the rename, the lost delete protection, and the tests, manual and prompt for them.
- **[IDEA-047](../jobs/IDEA-047.md)** is new: `SaveManyAsync(edits, policy)` with All, FirstRefusal and
  Continue; compile-before-transaction; the per-edit report; one save as a batch of one; the removal of
  the one-write rule; nothing else in the batch (no rename, search, delete, install or restore).
- **[the IDE plan](ide-host.md)** gains 047 as its own slice and states both names it intends.

## Slices

One branch, one gate, four commits, each read on its own. The rename cannot be split: the SDK type and
the seeds that name it have to land together or `make check-pages` fails on the site's own manual.

1. **The capability and its tests.** The interface, the merged scope, the chain registration, the lost
   delete protection, the seeds, the public API, the harness, the tests.
2. **The manual and the prompt.** `/help`, `PageAuthorPrompt` and its version, the seeder and prompt
   tests.
3. **The docs.** [pages](../pages.md) and [architecture](../architecture.md).
4. **The merge.** The three decision changes, the citation repointing, and the ide-host pointer at the
   name it took.

`make check` is the gate: format, licence, audit, decisions, `check-pages` (the seeds compile), the
tests and the publish.

## Risks

- **The rename breaks stored pages outside the repository.** `/pages` and any owner page that names
  `IPageEditor` or `IPageWriter` stop compiling until edited. The editor reads source without compiling,
  and `/admin/restore` reinstalls the seeded editor, so nothing is unrecoverable — but the owner sees
  error pages until they edit them, and the plan says so rather than discovering it.
- **Any page may now write the store.** That is the owner's decision. What is left is the signed-in
  visitor, the reserved keys, the one-write rule, and `/admin/restore` for a broken editor.
- **The editor is deletable.** A page with a bug can tombstone the editor. The tombstone keeps its
  history, `/admin/restore` puts the seed back, and the seeded editor no longer offers the action on
  its own row.
- **One capability, two answers.** `PageEditResult` and `PageSaveResult` both describe a save. They are
  not merged because they answer different questions — "did it apply, and if not why" versus "this is
  what your page became" — but the manual has to say which member answers which, or an author will
  expect `Stale` from the bound save.
- **A page's own save stays unconditional.** It can overwrite a head that moved between the read and
  the write. That is today's behaviour and it is the recovery path's simplicity; a page that wants the
  conditional save names its own key and passes its hash.

## Not in scope

- **The batch.** [IDEA-047](../jobs/IDEA-047.md): policies, transaction boundary, report, and the end
  of the one-write rule.
- **Renaming, searching, deleting in bulk, installing or restoring in bulk.** Those stay single-action
  members, and [IDEA-042](../jobs/IDEA-042.md) and [IDEA-038](../jobs/IDEA-038.md) are their own jobs.
- **A page store for a job.** A worker is offered no write (`pages-a-worker-gets-no-response`).
- **The shell's writes.** Import, export, restore and scrub stay routes (`pages-page-first`).
- **Removing `Pages:Editor:Key`.** It still decides routing; a host that stopped knowing where the
  editor is would have to drop `ctx.EditorPath` and the remedy links, which is a second job.

## Open questions

1. **The new decision's name.** The plan proposes **pages-any-page-may-write-the-store**; the merge
   takes whatever the owner says, and every citation of it in this branch is written in bold until
   then.
2. **The archive's wording.** Repointing the archived editor plan's six citations is mechanical, but
   two of the lines say "what this plan wrote to `docs/decisions.md`". The plan rewords those two and
   repoints the other four; the alternative is to unwrap the token and leave the prose exactly as it
   was.
3. **The editor's own Delete action.** Trunk's rewritten `/pages` already hides it on the editor's
   row and in the page's own menu, so this plan changes nothing there; the capability allows the call
   and `/admin/restore` is the answer if one is made.
