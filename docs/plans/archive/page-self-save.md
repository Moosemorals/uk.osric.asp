<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page that saves its own source

Status: **implemented** 2026-11-09, in the order the phases below set out. Written 2026-11-09, now
that IDEA-015 has landed: review chose a capability reached through `ctx.Features` with nothing added
to `IContext` for the read ([page store read](page-store-read.md), DEC-168–DEC-175), and the same
shape is used here. The authority is deliberately **not** symmetric with the read: the read is as
public as the page that calls it (DEC-169), and this write requires a signed-in visitor (DEC-190). The
job was [IDEA-016](../../jobs/README.md#closed).

## The short version

- **IDEA-016 lands as one capability.** `ctx.Features.GetRequired<IPageWriter>().SaveAsync(template)`
  saves the calling page's own source. **`IContext` gains nothing** (DEC-168, DEC-189).
- **One write and no key.** The handle is bound to `ctx.Path`, exactly as `IPageStorage` is
  (DEC-125): isolation is structural, and a page cannot name another page. A page that needs to write
  a *named* key is [IDEA-017](../../jobs/IDEA-017.md).
- **Authority is asymmetric, on purpose.** The read is permissive because a page's output is already
  public; a write changes the site, so it is authority, and authority is the shell's. `SaveAsync`
  refuses an anonymous render **before it compiles or stores anything**, and says so in words the
  visitor can act on (DEC-190). A page that would rather send the visitor to sign in guards its own
  form, and the demo page does.
- **A save belongs to the next request.** The running image is already loaded; the write is a
  revision. The response in hand is still the old page unless the page shows the result it just got,
  which is exactly what the demo page does (DEC-195).
- **One saver, one insert.** The page's save is the shell's save: `PageEditor.SaveAsync`'s body moves
  into a singleton `PageSaver`, and the insert becomes one context per save over the context factory,
  calling the *same* statement the editor, the seed restore and the import batch already call. There
  is no second writer (DEC-175, DEC-192).
- **A started save is never abandoned.** The wait for a compile slot ends at the render's deadline, but
  a compile that has begun runs to its own timeout and the revision is stored, because losing the
  author's edit is the one loss this site already refuses (DEC-20, DEC-23, DEC-43, DEC-191).
- **The diagnostics are `Sdk` values**: severity, 1-based template line and column, the author-facing
  code and its text. `Core` keeps its own type and the capability maps (DEC-88, DEC-193).
- **One seeded demo page.** `/scratch` is a page that shows its own source in a textarea and saves
  itself, with the next-request rule and the stale-form courtesy check visible in its source
  (DEC-196).

## Goals

- A page can be its own editor, which is what DEC-01 says the product is: the 1999 textarea-and-save
  experience, with the compile and the diagnostics the shell already gives the owner.
- The write is as safe as the shell's save and no safer-looking than it is: same compile, same
  insert, same revision, same `page.save.duration`, same image state on the page list (DEC-165).
- The authority question is answered once, in the capability, in the direction the site already
  answers it: a write needs an identity, a read does not (DEC-190).
- A page that saves can render its own answer — clean, warnings, or errors with positions — without
  a second render and without a per-visitor message (that is [IDEA-014](../../jobs/README.md)).
- Nothing a page can name comes from `Core` or `Web` (DEC-88).

## Non-goals

- **A named key.** `SaveAsync` takes no path; a page writes itself and nothing else. Widening it is
  [IDEA-017](../../jobs/IDEA-017.md), which is also where the editor becomes a page.
- **Delete, scrub, restore a seed, import, export.** The shell keeps every one of them (DEC-175).
- **A rename.** A save appends a revision at the page's own key; moving a key is not a write.
- **Conditional saves in the contract.** The editor's `SaveIfRevisionAsync` stays the shell's. A page
  that wants a "somebody else saved this" message compares the hash it read with the head it reads
  again, using the read it already has (D1, D8).
- **A second render after the save.** A page saves, then answers; the next request serves the new
  revision (DEC-195). Streaming is [ARCH-13](../../jobs/README.md)'s.
- **A preview, an autosave, or scripted editing.** [IDEA-001](../../jobs/IDEA-001.md) covers the
  editor's client side.
- **New caps or a `Pages:Write` section.** A save is bounded by limits the site already has (D5).
- **Speculative widening of the diagnostic value** — no column ranges, no end positions, no fixits.

## What exists today

Checked against the code on 2026-11-09.

- **The contract is `IContext` plus `Features`, and five capabilities are registered**: `IPageStorage`,
  `IHttpFetch`, `IPageCatalog`, `IPageLayout` and `IMarkdownRenderer`
  ([IContext.cs](../../../src/uk.osric.asp.Sdk/IContext.cs), [pages](../../pages.md#page-contract),
  DEC-142). Nothing has been added to `IContext` since the capabilities moved there, and the read did
  not add anything either (DEC-168).
- **A capability is offered by the host and cannot be added to by a page**, and it is registered or
  absent, never `null` ([IPageFeatures.cs](../../../src/uk.osric.asp.Sdk/IPageFeatures.cs), DEC-143).
- **A capability that does I/O is bound to the render.** `PageStorageScope` is the model: bound to the
  page's key, refuses a write on a `HEAD`, throws when the render was abandoned, and passes the
  render's deadline into the store call ([PageStorageScope.cs](../../../src/uk.osric.asp.Web/Pages/PageStorageScope.cs),
  DEC-123, DEC-125, DEC-126).
- **`ctx.Path` is canonical and a reserved key never renders.** `PageEndpoint` answers `404` for a key
  `Pages:ReservedPaths` claims, and `PageChain` refuses to use one as a layout, so "a page cannot save
  a reserved key" is structural rather than a check that could drift
  ([PageEndpoint.cs](../../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs),
  [PageChain.cs](../../../src/uk.osric.asp.Web/Pages/PageChain.cs), [ReservedPaths.cs](../../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs)).
- **A page runs for `GET`, `POST` and `HEAD`.** A body over 1 MiB is refused with `413` before it is
  read, and an **authenticated** `POST` without a valid antiforgery pair is refused before the page
  runs (`page.form_unverified`); an anonymous `POST` carries no cookie-backed identity to forge and
  does run ([PageEndpoint.cs](../../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs)). So a signed-in
  owner's save is CSRF-checked by the host, and an anonymous one has to be refused by the capability.
- **`ctx.Request.Form` collapses a repeated field to its first value**
  ([PageRequest.cs](../../../src/uk.osric.asp.Sdk/PageRequest.cs)). The shell's editor refuses a
  `template` field with more than one value; a page's save takes the first, which is the snapshot's
  documented rule.
- **The one save is `PageEditor.SaveAsync`**: build the image (`PageMetrics.ReasonSave`), store the
  revision, record `page.save.duration`, log the outcome, and keep the source when the compiler
  itself faults. `/edit`, seed restore and every assistant save tool reach the store through it
  ([PageEditor.cs](../../../src/uk.osric.asp.Web/Editor/PageEditor.cs),
  [EditorEndpoint.cs](../../../src/uk.osric.asp.Web/Editor/EditorEndpoint.cs),
  [PageToolExecutor.cs](../../../src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs), DEC-175).
- **The one insert statement is `SqlitePageStore.WriteRevisionAsync`**: the revision row, then the
  head upsert, in one transaction, used by the single save and by the import batch alike
  ([SqlitePageStore.cs](../../../src/uk.osric.asp.Web/Pages/SqlitePageStore.cs),
  [SqlitePageBatchStore.cs](../../../src/uk.osric.asp.Web/Pages/SqlitePageBatchStore.cs)). The retry that
  regenerates a revision id after a unique violation lives in `SavePageAsync` around it.
- **The save path is scoped, and a page's task is not.** `IPageStore`, `PageEditor`,
  `PageImageBuilder` and `PageRenderer` are scoped; `IPageStateStore` and `IPageReadStore` are
  singletons over `IDbContextFactory<StorageDbContext>` precisely because "a render that was abandoned
  can outlive the request scope, and a page's task must never touch a disposed one"
  ([Program.cs](../../../src/uk.osric.asp.Web/Program.cs),
  [SqlitePageStateStore.cs](../../../src/uk.osric.asp.Web/Pages/SqlitePageStateStore.cs), DEC-172).
- **A render's deadline does not cancel a compile.** The wait for a compile slot is abandoned at the
  deadline because nothing has been compiled; a compile that has started runs to
  `Pages:Compile:TimeoutSeconds` (10 s) and stores its image, while `Pages:Render:TimeoutSeconds` is
  5 ([PageImageBuilder.cs](../../../src/uk.osric.asp.Web/Pages/PageImageBuilder.cs),
  [appsettings.json](../../../src/uk.osric.asp.Web/appsettings.json), DEC-43).
- **A save stores what it compiled.** An error is stored and reported, a warning keeps its image, and
  a compiler fault stores the template and rethrows (DEC-20, DEC-23, DEC-178).
- **`Core.Compilation.PageDiagnostic` is `(Severity, TemplatePosition?, Message)`**, positions are
  1-based template space with unknown as `null`, and the message is `{code}: {text}` — `PAGE001: …`,
  `TEMPLATE001: …`, `SCRIPT002: …`, and Roslyn's `CS####: …`
  ([PageDiagnostic.cs](../../../src/uk.osric.asp.Core/Compilation/PageDiagnostic.cs), DEC-22, DEC-21).
  The code is therefore not a field; it is the prefix of the message, in one form every producer
  writes. The editor shows `PageDiagnostic.ToString()` as a single line.
- **A page may name only `Sdk` and the allow-listed base types**, and a type in `uk.osric.asp.Sdk` is
  admitted by the namespace rule, so new contract values need no policy change
  ([PageApiPolicy.cs](../../../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs), DEC-88).
- **`Sdk` declares what it publishes**, so four new types — and every member of them — are a
  deliberate edit to `PublicAPI.Unshipped.txt` (DEC-166).
- **`/help` is the author manual and a seed**, and a change to what a page may do updates it in the
  same change ([help](../../../src/uk.osric.asp.Web/Pages/Seeds/help), DEC-173). The author prompt is
  `page-author-v11`.
- **`/scratch` is free**: no seed claims it, and it is not reserved ([Seeds](../../../src/uk.osric.asp.Web/Pages/Seeds),
  [ReservedPaths.cs](../../../src/uk.osric.asp.Web/Pages/ReservedPaths.cs)).

## What this touches

| Job or decision | What it already says | What this plan does |
| --- | --- | --- |
| [IDEA-016](../../jobs/README.md#closed) | A page cannot save its own source; no write member and no `Sdk` diagnostic; a save bound to `ctx.Path`, compiled before it is stored, answering with diagnostics and the hash; authority stays with the shell | **Implements it**, as a capability rather than an `IContext` member, with the check in the capability rather than in `SimpleContext` (DEC-189, DEC-190) |
| [IDEA-015](../../jobs/README.md) (closed) | The read is a capability, public by default, and the shell keeps every write (DEC-168–DEC-175) | Follows the shape and reverses the authority for the write, on purpose: a disclosure and a change are not the same act (DEC-190) |
| [IDEA-017](../../jobs/IDEA-017.md) | A page cannot edit another page; the full editor is the last of the capability jobs | Hands it the saver and the capability; what it adds is a key (DEC-192) |
| [IDEA-014](../../jobs/README.md) | A page cannot keep per-visitor state, so a form cannot redirect and say why | Untouched, and the reason the demo page answers the save in the response it is already writing (DEC-195) |
| DEC-168, DEC-169 | The store read is a capability and is public | The pattern, applied with the opposite answer for authority |
| DEC-175 | The shell keeps every write; IDEA-016 reuses `PageEditor.SaveAsync` rather than opening a second writer | Kept literally: the saver and the insert move, they are not copied (DEC-192) |
| DEC-125, DEC-126 | `IPageStorage` is bound to the page's key; a `HEAD` reads but never writes | Followed in shape: this handle is bound to `ctx.Path` and refuses a `HEAD` |
| DEC-123 | I/O on a capability is a `Task`, takes no token, and is linked to the render's deadline | Followed, with one stated exception: a save that has begun finishes (DEC-191) |
| DEC-43, DEC-20, DEC-23 | A compile is not cancelled by a deadline; a compiler fault keeps the source; a warning keeps its image | Extended to the save: the revision is stored even when the response has already given up (DEC-191) |
| DEC-02, DEC-03, DEC-132 | Pages are public by default; any account is an owner; only the editor is behind a route policy | The write requires that owner and there is still no role: one signed-in visitor, no ownership of a page (DEC-190) |
| DEC-142, DEC-143 | A capability is registered or absent, never `null`; the host decides what a render offers | A sixth capability, registered for every content render and refused per call rather than withheld |
| DEC-158 | A layout is pure markup and may not set the response | The writer is offered to the content layer only, so a layout cannot rewrite itself on every request (DEC-194) |
| DEC-165, DEC-178 | A page has an image state that the page list, history and assistant all read | Unchanged: a page's save is a save, so the state is the one the editor already shows |
| DEC-26 | `RuntimeId` includes the Sdk module version id | One deliberate bump: new contract types invalidate every stored image, which recompiles on its next request |
| DEC-166 | `Sdk` declares every public symbol | Four new types, declared member by member in `PublicAPI.Unshipped.txt` |
| DEC-173 | `/help` is the manual and is updated with the capability | A new section, and the demo page named in it |
| DEC-110 | Every shell view is asserted for accessibility | `/scratch` is a stored page, so its markup is asserted through `MarkupReader` in the integration suite instead |

## Design

### D1 — One capability, one write, no key

`Sdk/IPageWriter.cs`:

```csharp
public interface IPageWriter {
    Task<PageSaveResult> SaveAsync(string template);
}
```

Four `Sdk` types, because a page may name neither `Core` nor `Web` (DEC-88):

```csharp
public sealed record PageSaveResult(string Hash, IReadOnlyList<PageDiagnostic> Diagnostics) {
    public bool HasErrors { get; }
    public bool HasWarnings { get; }
}

public readonly record struct PageDiagnostic(
    PageDiagnosticSeverity Severity, int? Line, int? Column, string Code, string Message);

public enum PageDiagnosticSeverity { Warning, Error }
```

- **No key argument.** The handle is the isolation, exactly as `IPageStorage` is bound to
  `IContext.Path` (DEC-125, DEC-126). A page cannot reach another page because it cannot name one,
  and a reserved key cannot be reached because a reserved key never renders.
- **No read member.** A page that wants its own source reads it with
  `IPageCatalog.ReadAsync(ctx.Path)`, which is already there and already public (DEC-168). The pair is
  one line apart in the demo page, and it is why the writer needs nothing but a save.
- **No conditional save.** `IPageStorage` is last-write-wins (DEC-125) and so is this: two tabs on one
  page, the later save wins. A page that wants a conflict message reads the head again before it
  saves — the demo page shows the six lines that costs — rather than widening the contract with the
  editor's `SaveIfRevisionAsync`. That belongs with [IDEA-017](../../jobs/IDEA-017.md)'s editor.
- **No dedupe.** Saving the template that is already the head appends a revision, as the editor's save
  does. A save is a revision, and the history is the record.
- **`Task`, no token** (DEC-123). A `null` template is an `ArgumentNullException` at the door
  (DEC-167); the empty template is legal, because an empty page is a page.
- `PageSaveResult.HasErrors` and `HasWarnings` exist because the answer a page must render is the
  editor's own switch — "Saved successfully", "Saved with warnings", "Saved with errors" — and
  DEC-23's rule that a warning never stops a page should be read once in the contract, not re-derived
  by every page that saves itself.
- `PageDiagnostic` is a `readonly record struct` of flat values; `PageSaveResult` is a class, like
  `PageSource`, because it carries a list.

### D2 — Authority is asymmetric: the read is permissive, the write is not

The read was made public by review, and the argument still holds for a read: a page's rendered output
is already public, so publishing a template it could have rendered is a way to publish something by
accident rather than a new boundary (DEC-169). **A write is a different act.** It does not disclose
the site, it changes it: it appends a revision to the page every visitor is served. There is no
"careless page" defence for that, and the site's whole authority model is one line long — a signed-in
account is an owner, and nothing else is (DEC-02, DEC-03). So:

- **`SaveAsync` refuses when `ctx.User` is `null`.** Nothing is compiled, nothing is stored, and the
  refusal names the reason in words the visitor can act on: *"Sign in to save a page. Nothing was
  saved."*
- **The refusal is a `UserFacingException`** — the one exception the shell shows verbatim — because the
  state is the visitor's, not a page bug: an account that timed out between the form and the post is
  not a programming error. The page that wants a redirect instead writes the ordinary guard over its
  own `POST`, and the demo page does. `/help` shows both and says which is nicer.
- **The check lives in the capability, not in `SimpleContext` and not in page code.** IDEA-016 point 3
  said "in `SimpleContext`"; the capability is where a capability's rules live now (DEC-168), and it is
  the only place that can refuse before a compile is spent. A page's own guard is a courtesy that runs
  *before* the save, never the enforcement.
- **The shell's antiforgery rule is already the other half.** An authenticated `POST` without a valid
  pair is refused before the page runs, so there are exactly two ways to reach a save: a signed-in
  visitor with a verified form, or a page bug that a 500 will find.
- **Three refusals are page bugs**, and they are `InvalidOperationException`, the shape
  `PageStorageScope` already uses: a save on a `HEAD` (which runs the page once per validator), a save
  after the render has ended (abandoned or past its deadline), and a second save in one render. A page
  that saves on `GET` learns in the first `HEAD` that it must save on `POST`; `/help` says so.

### D3 — One saver, one insert: the page's save is the shell's save

A page's task can outlive the request, so the save cannot use the scoped `IPageStore` chain. And
DEC-175 says there is no second writer. So the save is **moved**, not copied:

- **`PageSaver`** (new, singleton) is today's `PageEditor.SaveAsync` body: build the image with
  `PageMetrics.ReasonSave`, store the revision, record `page.save.duration`, log the outcome, and keep
  the source when the compiler faults (DEC-20). It returns the revision it stored and the diagnostics,
  so a caller that needs the new hash has it and the editor that does not can ignore it.
- **`IPageRevisionStore`/`SqlitePageRevisionStore`** (new, singleton over
  `IDbContextFactory<StorageDbContext>`) is one context per save and one transaction around the same
  `WriteRevisionAsync` statement the editor, the seed restore and the import batch already use. The
  retry that regenerates a revision id after a unique violation moves out of
  `SqlitePageStore.SavePageAsync` into one `internal static` helper, so the row shape, the head upsert
  and the retry are written once and called from two places.
- **`PageEditor.SaveAsync` delegates to `PageSaver` and keeps its signature**, so `/edit`, seed restore,
  the assistant's save tool and every editor test are unchanged. `SaveIfRevisionAsync` (the editor's
  conflict-detecting save) stays where it is — it still compiles through the same `PageImageBuilder`,
  now a singleton — and is not offered to a page.
- **`PageImageBuilder` has no per-request state** — a compiler, the compile budget and the metrics, all
  singletons already — so it is registered as a singleton and the saver can hold it.

The result is that a page's save and the owner's save are the same compile and the same insert, and
the only thing the capability adds is the decision about *who* may call it and *which* key it is for.

### D4 — A save belongs to the next request

The running image is already loaded and is not re-read: a save made during a render changes what the
*next* request serves, and the response in hand is still produced by the template the page is running.
Three consequences, all of which the plan states rather than smooths over:

- The page shows the result it just got — the hash and the diagnostics — instead of re-rendering
  itself. It can also read back what it saved with `IPageCatalog.ReadAsync(ctx.Path)` in the same
  render, because that read goes to the store and not to the image.
- The save is committed before the rest of the render, so a page that saves and then throws has still
  saved. That is the editor's behaviour too, and it is the right way round: the author's edit is the
  thing that must not be lost.
- Two patterns work. **Save and answer** — the POST's response is the diagnostics, with a link to the
  page and the submitted template still in the textarea (the demo page, and the only pattern that
  works without per-visitor state). **Save and redirect** — `ctx.Redirect(ctx.Path, 303)` sends the
  visitor to the page the next request will serve, at the cost of saying nothing about the compile
  unless the page keeps state ([IDEA-014](../../jobs/README.md)).

### D5 — What bounds a save, without a new setting

- **One save per render.** A second save in the same render has nothing to say that the first did not,
  and would queue for another compile; the capability refuses it. Since the writer is offered to the
  content layer only (D7), that is one save per request in the whole chain.
- **The template is the request's own body**, already capped at 1 MiB by `PageEndpoint` before the
  parser sees it, so no new size limit is invented. A page that builds its own template is bounded by
  the body cap and the deadline like any other page.
- **The wait for a compile slot is abandoned at the render's deadline** (`PageImageBuilder.Build`'s
  `renderDeadline`), because nothing has been compiled and nothing is lost — the visitor is told the
  page took too long, and the author's text is still in their browser.
- **The compile itself is not.** It runs to `Pages:Compile:TimeoutSeconds`, and the revision is stored
  even if the response has already gone as a `504` (D6).
- **The insert is one transaction on one connection, with `SqliteErrors.RetryCount` attempts** — the
  bound the editor's save already has.
- **No `Pages:Write` section.** Every bound above is a bound the site already has, and a setting that
  never binds is a setting nobody maintains. The read needed caps because it reads *any* key; this
  writes one page's own template.

### D6 — A started save is never abandoned

A deadline that cancels a write the author asked for loses their text, and this site already refuses
that loss twice: a compile is not cancelled by a deadline, because throwing it away leaves the page
unservable (DEC-43), and a compiler fault still stores the source, because losing the edit to report
our bug would be worse (DEC-20). A save is the same kind of work, so:

- **Before it starts**, the save is refused when the render has ended, when there is no signed-in
  visitor, and on a `HEAD` — all three cost nothing and lose nothing.
- **After it starts**, the compile keeps its own timeout and the revision is stored. The page's task
  may finish after the shell has answered without it; the body it writes is already ignored
  (`SimpleContext.Abandon`), and what survives is the edit.
- **The seam is what makes that safe.** A scoped `DbContext` would be disposed under an abandoned
  render; the singleton seam opens its own context per save, for `SqlitePageStateStore`'s reason
  (DEC-172).
- **What the visitor sees** is a `504` and a page that has changed. The next request serves the new
  revision, which is the honest end state: the work happened, the answer was late. The demo page keeps
  the submitted template in the textarea of the response it does send, so nobody is sent to fix
  something they can no longer see.

### D7 — The writer is offered to the content layer, not to a layout

A layout is markup and nothing else (DEC-158). A layout that saved itself would append a revision on
every render of every page it wraps, and would race the owner's editor every time somebody looked at
the site — a revision flood, and a lost update nobody asked for. So `PageChain.CreateLayer` registers
the writer only when the layer is the content, and a layout that asks gets `GetRequired`'s exception
naming the capability.

That is a deliberate difference from the read, which is offered to every layer and may be used by a
layout. The rule is one line in the manual: **a layout is offered the read and not the write.**

### D8 — The seeded `/scratch`, a page that edits itself

One seed, a fragment the default layout wraps:

- **It guards its whole view** with the ordinary line —
  `if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }` — so an anonymous visitor meets a
  sign-in page rather than the capability's refusal. The refusal stays the backstop for pages that do
  not guard, which is exactly the relationship DEC-169 gave the read's guard.
- **It reads itself** with `IPageCatalog.ReadAsync(ctx.Path)`, which gives the source and the head's
  hash, and writes the source into a `<textarea>` with `<%= %>` so the escaping rules are the demo's
  own lesson.
- **It saves itself** on `POST`, taking the textarea's value from `ctx.Request.Form` and checking that
  the field is there before it spends a compile, then writes the new hash and the diagnostics — line,
  column, code and text — and says in one sentence that this response is the save's answer and the
  next request serves the new source. A save with errors still appears here, with the template that
  produced them still in the form.
- **It carries the hash it read** in a hidden field and compares it with the head before saving, so a
  stale form says "somebody saved this page while you were typing" rather than silently winning. That
  is six lines and two capabilities and no new contract, and it is the argument for D1's "no
  conditional save".
- **It writes its own body with `ctx.Write` where it shows source**, because page source is exactly
  what a page that shows source must not run.
- **It says it is a demonstration** and names [IDEA-017](../../jobs/IDEA-017.md), so nobody mistakes it
  for the editor.
- **One page, not two.** The read had a public and a guarded page because the gate question needed two
  files; the write has one answer, and a second page differing only in a form would be a second demo of
  the same sentence.
- **It is a seed**, so it can be edited, restored and deleted like any other page, and an existing
  installation gains it on its next start. The seed opens with a template block, so it joins the seed
  annotations in `REUSE.toml`. `/help` names it in the new section, and it links back to `/help` and
  `/pages`.

Why a page rather than only tests: the interesting failures only appear with a real layout, a real
form, a real antiforgery field and a real store — a save that compiled but cannot be rendered, a
textarea that ran someone's source, a form that expired. `/scratch` makes those ordinary, and it is
the product of DEC-01 running on the site that claims it.

## Considered and rejected

**`SaveAsync` on `IContext`.** IDEA-016's original sketch. Rejected by the same review that settled
the read: `IContext` is the contract every stored page compiles against, and a page that never saves
itself should never meet a write (DEC-142, DEC-168).

**Gating the write with the page's own guard.** Rejected: the page is the thing being defaced. A
page that forgot its guard would be rewritable by anyone, and the model would put the site's authority
in whichever file was written last. The capability refuses, and the page's guard is only a nicer
redirect in front of it (D2).

**Withholding the capability from an anonymous render.** Rejected: `GetRequired` would answer "This
render does not offer `IPageWriter`" — a `500` whose text names a type instead of telling the visitor
to sign in — and `TryGet` would make the same page behave differently for two visitors with nothing in
its source to explain it. Registered, and refusing with a message, is the same rule with better words.

**Refusing an anonymous save with `InvalidOperationException`.** Rejected: it is not a page bug, and
the shell would answer with the generic "An unexpected error occurred" text. The session that expired
is the visitor's to fix, so it gets `UserFacingException` and its own sentence.

**A second capability for a named key now.** Rejected: that is [IDEA-017](../../jobs/IDEA-017.md), and
it needs the editor's view underneath it. One handle bound to one key is the whole of this job.

**A conditional save in the contract.** Rejected: the editor's conflict detection exists because two
owner tabs are ordinary; a page can do the same check with the read it already has (D1, D8), and a
contract member that only one page uses is a contract to maintain for everybody.

**Cancelling a started save at the render deadline**, the shape `PageStorageScope` and
`PageHttpFetch` use. Rejected: those calls can be retried and lose nothing; a save loses the text the
author typed. The site already refuses that loss for compiles (DEC-43) and for compiler faults
(DEC-20), and it is worth the one exception to "the render bounds every call" to keep it (DEC-191).

**Offering the writer to every layer, as the read is.** Rejected: a layout writing itself once per
render is a revision flood, and a layout is markup (DEC-158).

**Turning the editor's views into the demo page.** Rejected: that is
[IDEA-017](../../jobs/IDEA-017.md). `/scratch` is 60 lines and proves the capability; the editor is a
different job with its own markup, accessibility and browser tests.

**A `Pages:Write:MaxTemplateChars` cap.** Rejected: the request cap already binds the only way a
template can arrive, and a page that builds one is bounded like any other page (D5).

**Splitting the diagnostic into `Core`'s type moved into `Sdk`.** Rejected: the generator would then
depend on the page contract, and `Core` would lose the value its own compiler tests use (DEC-10,
DEC-88, IDEA-016 point 1).

## Components

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IPageWriter.cs` | **new** — the one write, reached through `Features` |
| `src/uk.osric.asp.Sdk/PageSaveResult.cs`, `PageDiagnostic.cs`, `PageDiagnosticSeverity.cs` | **new** — the values a page may name |
| `src/uk.osric.asp.Sdk/IContext.cs` | **unchanged** |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | **changed** — the four new types, member by member |
| `src/uk.osric.asp.Web/Pages/PageWriterScope.cs` | **new** — the capability: the authority, `HEAD`, one save, the deadline, the mapping from `Core`'s diagnostics |
| `src/uk.osric.asp.Web/Editor/PageSaver.cs` | **new** — compile-then-store moved out of `PageEditor`, answering with the revision |
| `src/uk.osric.asp.Web/Pages/IPageRevisionStore.cs`, `SqlitePageRevisionStore.cs` | **new** — the singleton insert seam over the context factory |
| `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs` | **changed** — the retry-and-insert loop becomes one `internal static` both callers use |
| `src/uk.osric.asp.Web/Editor/PageEditor.cs` | **changed** — `SaveAsync` delegates to `PageSaver`; `SaveIfRevisionAsync` unchanged |
| `src/uk.osric.asp.Web/Pages/PageImageBuilder.cs` | **unchanged** in code, registered as a singleton |
| `src/uk.osric.asp.Web/Pages/PageEnvironment.cs` | **changed** — `Saver` |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | **changed** — the content layer's `PageWriterScope`, and only the content layer's |
| `src/uk.osric.asp.Web/Program.cs` | **changed** — `PageSaver`, `IPageRevisionStore`, `PageImageBuilder`'s lifetime |
| `src/uk.osric.asp.Web/appsettings.json` | **unchanged** — no new setting |
| `src/uk.osric.asp.Web/Pages/Seeds/scratch` | **new** — the page that edits itself |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | **changed** — the capability, the asymmetry, the demo page, and the layout rule |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | **changed** — `page-author-v12` |
| `REUSE.toml` | **changed** — the new seed's licence annotation |
| `test/uk.osric.asp.Web.Tests/PageHarness.cs`, `ShellViews.cs`, `PageEndpointTests.cs` | **changed** — the new `PageEnvironment` member, with a fake saver |
| `test/uk.osric.asp.Web.Tests/FakePageRevisionStore.cs` | **new** — an in-memory insert seam |
| `docs/pages.md`, `docs/storage.md`, `docs/decisions.md`, `docs/jobs/`, `docs/testing.md` | **changed** — see "Documentation and decisions" |

## Tests

**`Web.Tests` — `PageWriterScopeTests`** (new; the heart of the plan)

- `ASignedInVisitorSavesItsOwnSource`, `TheSaveIsBoundToThePagesOwnKey`
- `AnAnonymousSaveIsRefused`, `AnAnonymousRefusalNamesSigningIn`, `AnAnonymousSaveStoresNothingAndCompilesNothing`
- `ASaveOnHeadIsRefused`, `AnAbandonedRenderCannotSave`, `ASaveAfterTheDeadlineIsRefused`
- `ASecondSaveInOneRenderIsRefused`
- `ACleanTemplateAnswersWithItsRevisionAndNoDiagnostics`
- `AWarningIsAnsweredAndThePageIsStillStored` (DEC-23)
- `AnErrorIsAnsweredWithItsPositionAndStoredWithoutAnImage` (DEC-23)
- `ADiagnosticCarriesItsCodeAndItsTextApart`
- `ADiagnosticWithNoPositionHasNoLineOrColumn` (DEC-22)
- `TheDiagnosticsAreOrderedForDisplay`
- `ACompilerFaultStoresTheSourceAndThrows` (DEC-20)
- `TheSlotWaitEndsAtTheRenderDeadlineButTheCompileDoesNot` (D6)

**`Web.Tests` — `PageSaverTests`** (new; the moved algorithm, with `FakePageRevisionStore`)

- `ACleanTemplateIsStoredWithItsImage`, `AWarningIsStoredWithItsImage`, `AnErrorIsStoredWithoutAnImage`
- `ACompilerFaultStoresTheSourceAndRethrows`
- `TheResultNamesTheRevisionThatBecameTheHead`
- `TheSaveIsRecordedAsASave` — `page.save.duration` with a compiled outcome

**`Web.Tests` — `SqlitePageRevisionStoreTests`** (new, over `DatabaseTemplate`)

- `ASaveWritesOneRevisionAndMovesTheHead`
- `AUniqueViolationIsRetriedWithANewRevisionId` — the loop the editor already relies on
- `TheSeamAndTheEditorStoreTheSameRows` — the same template through both paths gives the same shape
- `ASaveOpensItsOwnContext` — a save after the caller's scope is gone still lands

**`Web.Tests` — `PageEditorTests`** (unchanged, deliberately)

The refactor's regression net: every editor, restore and assistant save test keeps passing against the
delegating `PageEditor`.

**`Web.Tests` — `PageFeaturesTests`, `PageLayoutChainTests`** (changed)

- `EveryContentLayerIsOfferedTheWriter`, `ALayoutIsNotOfferedTheWriter`

**`Web.IntegrationTests` — `PageSelfSaveTests`** (new; drives the seeded page through the real host)

- `TheDraftSendsAnAnonymousVisitorToSignIn`
- `TheDraftShowsItsOwnSourceInATextareaWhenSignedIn`
- `ASignedInOwnerSavesTheDraftAndTheNextRequestServesIt`
- `ASaveWithErrorsIsAnsweredWithItsDiagnostics` — and the next request answers `500` with the same ones
- `ASaveWithWarningsIsServedOnTheNextRequest` (DEC-178)
- `AnAuthenticatedPostWithoutTheFieldIsRefusedBeforeThePageRuns`, `TheDraftIsUnchangedThen`
- `AStaleFormIsRefusedAndTheNewerSaveStands` — the demo's courtesy check
- `AHeadRequestNeverSaves`
- `TheDraftIsWrappedInTheStoredLayout`
- `TheDraftSourceIsEscapedInItsOwnTextarea` — a page whose source contains `<script>` is shown as text

**`Web.IntegrationTests` — `PageAssetTests`** (changed)

The seed list gains `/scratch` and the parametrised seed tests pick it up; the draft is a fragment
that sets a title, is reachable, and its guard is asserted against the shipped template, so an edit
that drops the guard fails a test rather than a review.

**Accessibility.** `AccessibilityTests` renders shell views and does not reach stored pages, so
`/scratch`'s markup is asserted through `MarkupReader` in `PageSelfSaveTests`: one `h1`, the form's
one `dl` with a label for every control, the textarea inside a `fieldset` with its `legend`, and the
save's answer in a `<section>` with its own heading.

**Deliberately not tested.** Browser behaviour: the draft has no script, so `Web.BrowserTests` gains
nothing. The compiler's own diagnostics: `Core.Tests` and `Runtime.Tests` own them.

## Phases

Each phase ends with `make test` green and `make format-check` clean.

1. **The shared saver.** `PageSaver`, `IPageRevisionStore`/`SqlitePageRevisionStore`, the extracted
   retry-and-insert helper, `PageImageBuilder`'s lifetime, and `PageEditor.SaveAsync` delegating to it.
   No page can see anything new. **This is the phase that touches hot code** — `/edit`, seed restore
   and the assistant save through it — so it lands on its own with `PageEditorTests` unchanged and the
   new saver and seam tests beside it.
2. **The capability.** The four `Sdk` types, `PageWriterScope`, the `PageEnvironment` and
   `PageChain` wiring, the authority, `HEAD`, the one-save rule, the deadline, the diagnostic mapping,
   `PublicAPI`, and the unit tests. **This is the risky phase** and it is worth landing with no page
   changed.
3. **The demo page.** `Seeds/scratch`, the `/help` section, `REUSE.toml`, the integration tests, the
   seed-list tests.
4. **Teach and record.** `PageAuthorPrompt` to `page-author-v12`; `docs/pages.md`,
   `docs/storage.md`, `docs/decisions.md`, `docs/testing.md`; the jobs (IDEA-016 closed, IDEA-017's
   note updated); every relative link and anchor checked.

Phase 2 depends on phase 1, phase 3 on phase 2. Phases 1 and 2 are one sitting; phase 3 is another.

## Documentation and decisions

- **DEC-189**: saving a page's own source is a capability, `IPageWriter.SaveAsync(template)`, reached
  through `IContext.Features`, with `PageSaveResult`, `PageDiagnostic` and `PageDiagnosticSeverity` as
  the values a page may name. The handle is bound to `ctx.Path` and takes no key, there is no read
  member and no conditional save, and `IContext` and `PageResponse` are unchanged.
- **DEC-190**: authority is asymmetric. The read is public (DEC-169) and the write requires a
  signed-in visitor, because a write changes what the site serves and the site has one authority rule
  (DEC-02, DEC-03). The check is the capability's, not the page's and not `SimpleContext`'s, and it
  runs before anything is compiled or stored; an anonymous save is refused with a `UserFacingException`
  naming signing in, while `HEAD`, an ended render and a second save are page bugs and
  `InvalidOperationException`. This overrules IDEA-016 point 3 only in *where* the check lives.
- **DEC-191**: a started save is never abandoned. An ended render, `HEAD` or a missing identity
  refuses before the save starts; once it has started the compile keeps its own timeout (DEC-43) and
  the revision is stored even when the response has already answered `504`, because losing the
  author's edit is the loss DEC-20 and DEC-23 already refuse. The wait for a compile slot is abandoned
  at the render's deadline.
- **DEC-192**: there is still one writer. `PageSaver` is `PageEditor.SaveAsync`'s algorithm as a
  singleton and `PageEditor` delegates to it; `IPageRevisionStore` is a singleton over the context
  factory that calls the same `WriteRevisionAsync` statement as the single save and the import batch,
  which is what makes a page's write safe after its request has gone (DEC-172). DEC-175 stays true: the
  page's save *is* the shell's save, and delete, scrub, restore, import and export stay the shell's.
- **DEC-193**: the author-facing diagnostic is an `Sdk` value with severity, a 1-based line and column
  (both `null` when the position is unknown, DEC-22), the code and its text; the code is split from the
  message's `{code}: {text}` form, which every producer already writes, in the capability's mapping.
  `Core.Compilation.PageDiagnostic` is unchanged, because moving it would make the generator depend on
  the page contract (DEC-88).
- **DEC-194**: the writer is offered to the content layer and not to a layout, because a layout is
  markup (DEC-158) and would otherwise append a revision on every render it wraps.
- **DEC-195**: a save belongs to the next request. The running image is not re-read, the response in
  hand is the old page unless the page shows the result it just got, the save is committed before the
  rest of the render, and one render makes at most one save.
- **DEC-196**: the demonstration is one seeded page, `/scratch`, which guards its own view, reads its
  own source through `IPageCatalog`, saves through `IPageWriter`, answers the save in the response it
  is already writing, and refuses a stale form by comparing the hash it read with the head.
- `docs/pages.md`: "Page contract" lists six capabilities and carries the `IPageWriter` bullet with the
  authority rule and the layout rule; "Compiled pages" says a page can save itself through the same
  saver the editor uses.
- `docs/storage.md`: the "Source storage" paragraph says a page may save its own template, only for a
  signed-in visitor, through the same insert, and that a started save is stored even if the render
  timed out.
- `docs/testing.md`: one sentence naming `PageWriterScopeTests`, `PageSaverTests`,
  `SqlitePageRevisionStoreTests` and `PageSelfSaveTests`.
- `docs/jobs/IDEA-016.md`: deleted; its row leaves the open table and it joins "Closed" with a pointer
  here, recording that the check moved into the capability and that the write is gated where the read
  is not. `docs/jobs/README.md`: IDEA-017's note says the saver and the self-save exist and the named
  key is what is left; the rough-order step 8 says IDEA-016 has landed.
- `docs/glossary.md`: no new term. Page, template, revision and save are already defined, and neither
  "writer" nor "saver" is vocabulary rather than a type name.
- `deploy/README.md`: nothing to deploy; the demo page is a seed and there is no new setting.
- **Decision numbering**: the plan was drafted against DEC-189–DEC-196, but other branches held
  179–188 by the time the work landed (browser-tests 179, ops-06 180–185, assistant-memory-spike
  186–188), so the eight entries are **DEC-189–DEC-196**: the capability, the authority, the started
  save, the one writer, the diagnostic, the layout rule, the next request and the demo page.

## Risks

- **The owner can break a page that edits itself.** `/scratch` can be saved with a template full of
  errors, and it then answers `500` with its own diagnostics until somebody fixes it. That is the
  product working (DEC-01), `/edit` has the restore button, and the diagnostics are the same ones the
  editor shows.
- **An edit can be lost.** A save that has not started when the render ends is refused, so a page that
  does a slow fetch before saving can lose the post; a template over the 1 MiB request cap never
  reaches the page; and a browser that drops the connection mid-post loses it as it would anywhere
  else. The demo page keeps the submitted template in the textarea of the response it sends, which is
  the only mitigation a page without per-visitor state has ([IDEA-014](../../jobs/README.md)).
- **A save is a compile, and compiles are serialised.** One compile slot means a save queues behind
  every other compile; the slot wait ends at the render deadline, so under load a visitor meets `504`
  while their edit still lands (D6). Worth watching: the page list will show revisions whose authors
  believe the save failed.
- **A page that saves on `GET` costs a compile per visit** and appends a revision each time. The
  capability allows it (one save per render); `/help` says to save on `POST` only, and no seed does it.
- **Last write wins.** Two tabs on one page: the later save replaces the earlier. The editor's
  conditional save is deliberately not offered (D1), and a page that wants the check writes it.
- **A save revives a page deleted mid-render**, because a save appends a revision and clears the head's
  tombstone. The set of pages that can do that is exactly the set that was live when their render
  started, and the editor has the same race with two tabs; it is noted rather than fixed.
- **An anonymous post reaches the error page.** A page that renders a save form without guarding its
  `POST` sends an anonymous visitor to a `500` that says to sign in, rather than to `/login`. The
  backstop is the capability's; the nicety is the page's, and `/help` shows the line.
- **One more contract bump.** Four new `Sdk` types change the module version id, so every stored
  image recompiles on its next request (DEC-26). Expected, and the same cost the layout and the read
  paid.
- **The refactor touches the save everything uses.** `/edit`, seed restore and the assistant's save
  tool all reach the store through `PageEditor.SaveAsync`. Phase 1 is deliberately separate, keeps
  `PageEditorTests` unchanged, and adds the seam's own tests before any page can see the capability.
- **The demo page becomes content.** It is a seed, so an installation keeps it and the owner may edit,
  break or delete it. If it goes, `/help`'s link points at a missing page; a seeder test pins the guard
  in the shipped template, because the guard is what makes the demo safe to ship.

## Settled at review

| Question | Answer |
| --- | --- |
| A capability or more `IContext` | A capability, `IPageWriter`, through `ctx.Features`; `IContext` unchanged (DEC-189) |
| Is the write gated | Yes, and it is the one place authority is asymmetric: the read stays public (DEC-169) and the write requires a signed-in visitor, refused inside the capability (DEC-190) |
| Where the gate lives | In the capability, not in `SimpleContext` and not in page code; the page's own `ctx.User` guard is a nicer redirect in front of it |
| Is a started save cancelled at the deadline | No. The queue for a compile slot ends at the deadline; the compile and the insert finish (DEC-191) |
| Is there a second writer | No. `PageSaver` and `IPageRevisionStore` move the editor's save and call the same insert the import batch calls (DEC-192) |
| Layouts | Offered the read and not the write (DEC-194) |
| A conditional save in the contract | No: the page compares the hash it read with the head, and the demo does (DEC-196) |
| How it is proven | Unit tests for the capability, the saver and the insert, and the seeded `/scratch` driven through the real host (DEC-196) |
