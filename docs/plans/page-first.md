<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Page first

| | |
| --- | --- |
| Status | Planned, not started |
| Size | Large — a rule and its audit, two capabilities, a store registration change, a seeded page and its sheet, and the tests for each |
| Spike | None. The page contract already carries everything a form needs, and `/scratch` is the worked example |
| Decisions | DEC-205 (the rule) and DEC-206 (the capability), recorded with this plan; DEC-200 is superseded in phase 3 |
| Backlog | [IDEA-024](../jobs/IDEA-024.md) (a page cannot take an upload or serve a file), [IDEA-025](../jobs/IDEA-025.md) (the assistant conversation as a page), [ARCH-13](../jobs/ARCH-13.md) (a page cannot stream) — the three reasons a shell route is not already a page |

## The short version

The site is a page that can edit itself, and its shell has been quietly growing views that are not
the page runtime at all. This plan makes the rule explicit and applies it to the first case:

> **Anything a page can serve is a page.** The shell keeps what bootstraps a broken site, what *is*
> the page runtime, and what a page cannot do yet — and every one of those last ones has a job.

The first customer is the owner's memory page, which landed last week as four shell routes
(`/assistant/memory` and its save and forget). It is a list and a form with no JavaScript, no
streaming and no upload — everything a page is *for*. It becomes:

- **One capability, owner-bound.** `IMemoryCatalog` (read) and `IMemoryWriter` (write) in `Sdk`,
  offered through `ctx.Features`, bound to `ctx.User` by the host so a page cannot name an owner.
  An anonymous render is refused by the capability, not merely by the page's own guard.
- **One stored page.** `/memory`, seeded like `/help` and `/scratch`, with its own `/memory.css`,
  saving itself on a `POST` and answering `303` back to itself.
- **Four shell routes fewer.** `AssistantMemoryEndpoint` and its registration go. `/status` stays,
  deliberately: it is the first thing to read when a page did not load.

Nothing about recall changes, and the store's rules do not move: `EditAsync` is still the one write
that can pin, a stale version is still refused with the stored content, and a forget is still the
tool's delete.

## Goals

1. One written rule for where code goes, applied to every route the shell answers today, with the
   exceptions named and a job behind each.
2. The memory page becomes a page: its list, its form, its confirmations, its styles and its tests are
   page artefacts, editable and restorable like `/help`.
3. Memory becomes a product capability: a page written by the owner can read and change their own
   memory, and cannot touch anybody else's, by construction rather than by the page remembering to
   check.
4. The assistant's side gets smaller. The shell route, the endpoint, its four routes and their tests
   are deleted, not wrapped.
5. The page contract grows by one capability, deliberately: `Sdk` values, a refusal for an anonymous
   render, the writer withheld from a layout, and the capability absent when the assistant is off.

## Non-goals

- **Making everything a page at once.** This plan moves the memory page and writes the rule. The
  other shell routes get jobs, not migrations.
- **A per-route policy for pages.** Pages have no route policy and should not get one: the guard is
  the page's `ctx.User` check and the backstop is the capability's refusal. Adding policy would put
  authority in configuration, which is where a page cannot see it (DEC-02, DEC-169).
- **A second page for the forget confirmation.** A page is its own route: the confirmation is
  `/memory?forget={id}`, and the form posts back to the same key.
- **A page-side editor.** `/edit` is the tool that repairs a broken page, so it can never be one.
- **Streaming, uploads or script endpoints.** Those are what the *remaining* shell routes wait for,
  and they are backlog (ARCH-13, IDEA-024, IDEA-025).
- **Moving `/status`.** It stays shell: diagnosing why a page did not load starts there, which makes
  it bootstrap rather than a view.
- **Team memory, sharing, or memory in page state.** One owner per row is unchanged (DEC-184).

## The rule

> **Anything a page can serve is a page.** A route stays in the shell only for one of three reasons,
> and "we have not got to it" is not one of them: it is a job.

| Route | Why it is not a page | Where that is recorded |
| --- | --- | --- |
| `/edit` and its commands (save, history, delete, scrub, restore a seed) | **Bootstrap.** It is the tool that repairs a page, so a broken site cannot be repaired from inside one | DEC-205 |
| `/login`, `/logout` | **Bootstrap.** You cannot reach a page you must sign in for if signing in is a page you must sign in for | DEC-205 |
| `/status` | **Bootstrap.** Diagnosing why a page did not load — the counters, the storage and the build — starts here | DEC-205, and the operator's call |
| `{**path}`, `/favicon.ico`, `/heath`, `/metrics` | **The runtime.** The page catch-all, the reserved key that stops a page claiming the icon, the probe and the scrape target are the thing pages run on | DEC-205 |
| `/assistant`, `/assistant/{id}`, its events, approvals, options, stop and delete | **A page cannot, yet.** A live run needs a stream (ARCH-13) and the conversation's actions need a capability (IDEA-025) | [ARCH-13](../jobs/ARCH-13.md), [IDEA-025](../jobs/IDEA-025.md) |
| `/admin/import`, `/admin/export` | **A page cannot, yet.** A page can neither take an upload nor serve a file | [IDEA-024](../jobs/IDEA-024.md) |
| `/assistant/memory` | **It can.** This plan moves it | this plan |

The table is the audit and it is expected to shrink: when IDEA-024 lands, `/admin/*` joins the page
column, and the rule is what stays the same. A new route starts by asking which column it is in, and
"the page column" is the default answer.

## What exists today

The pieces this plan builds on:

| Piece | Today | Used here for |
| --- | --- | --- |
| `IContext` and `IPageFeatures` | `User`, `AntiforgeryField`, `Write`, `Redirect`, `SetStatus`, `SetHeader` and the capabilities | Everything the page needs; no contract change |
| `IPageCatalog`, `IPageWriter`, `IPageStorage`, `IHttpFetch`, `IPageLayout`, `IMarkdownRenderer` | The capabilities a render offers, built per render in `PageChain` from `PageEnvironment` | The pattern the memory capability copies |
| `PageWriterScope`, `PageCatalogScope`, `PageStorageScope` | Capability scopes in `Web.Pages`: mapping `Web` records to `Sdk` values and refusing what the policy forbids | The shape of `MemoryCatalogScope` and `MemoryWriterScope` |
| `PageEnvironment` | The singleton record the renderer needs, which exists "so the endpoint's signature stays readable as the page contract grows a capability" | Gains the memory store, offered only while the assistant is enabled |
| `IMemoryStore` and `SqliteMemoryStore` | Owner-scoped, every statement filtered in SQL, scoped over `StorageDbContext` | The rules the capability exposes; the registration changes in D3 |
| `AssistantMemoryEndpoint` | The shell page: four routes, the list, the row and the confirmation | Deleted; the page replaces it |
| `Pages/Seeds/memory`-shaped seeds | `/help`, `/scratch`, `/pages`, `/recent` and their sheets | The page's home, its stylesheet and its tests |
| `PageHarness` | Renders a stored template with fakes for every capability | Gains a memory fake, so the page renders in a unit test |
| `AccessibilityTests` | Twelve rules over every view in `ShellViews` | A page fixture, so the page is held to the same rules as a shell view |

[IDEA-019](../jobs/IDEA-019.md) was "found together while asking what a shell view would need to
become a page"; this plan is the answer for one view, and it needs none of IDEA-019's three gaps:
memory needs no repeated values, no cookie and no hashing.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Sdk/IMemoryCatalog.cs`, `IMemoryWriter.cs`, `MemoryNote.cs`, `MemoryEdit.cs`, `MemorySaveResult.cs` | The capability, its values and its request |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | The new surface |
| `src/uk.osric.asp.Web/Pages/MemoryCatalogScope.cs`, `MemoryWriterScope.cs` | The owner binding, the anonymous refusal and the mapping |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Offers both capabilities, the writer only to the content layer, and neither to a layout |
| `src/uk.osric.asp.Web/Pages/PageEnvironment.cs` | Carries the memory store, or nothing when the assistant is off |
| `src/uk.osric.asp.Web/Assistant/SqliteMemoryStore.cs` | Over `IDbContextFactory<StorageDbContext>`, so it can be a singleton |
| `src/uk.osric.asp.Web/Program.cs` | The store becomes a singleton; the four memory routes and `AssistantMemoryEndpoint` go |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | The assistant navigation links to `/memory` |
| `src/uk.osric.asp.Web/Assistant/AssistantMemoryEndpoint.cs` | Deleted |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | One sentence naming `/memory`; `Version` to `page-author-v15` |
| `src/uk.osric.asp.Web/Pages/Seeds/memory`, `memory.css` | The page and its sheet |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` | Its memory section goes, into `memory.css` |
| `docs/architecture.md`, `docs/decisions.md` | The rule and the audit; DEC-200 moved to superseded in phase 3 |
| `docs/assistant.md`, `docs/storage.md`, `docs/testing.md`, `docs/glossary.md` | The page, the capability, the suites, the terms |
| `docs/jobs/IDEA-024.md`, `IDEA-025.md`, `docs/jobs/README.md` | The two jobs this plan opens |
| `docs/plans/assistant-memory-page.md` | A note: its shell view is superseded, its store work stands |

## Design

### D1 — The rule, written down in three places

A rule that lives in one conversation is a rule that erodes. It is written where each reader already
looks:

- **`docs/decisions.md`, DEC-205** — the rule itself, with the three reasons and the audit's shape.
- **`docs/architecture.md`** — a "Where a view lives" paragraph in the shell section: the shell answers
  bootstrap, runtime and not-yet routes; a view that is a list, a form or a report is a page.
- **This plan's table** — the audit as it stands today, and the note that it shrinks. The table is
  copied into the architecture document so a reader without the plan sees the current division.

The default is the page column, and the default is what a reviewer asks for: a new shell route needs
one of the three reasons in its pull request, and "it is easier in C#" is an IDEA file, not an answer.

### D2 — The capability

Two interfaces, because a read and a write are not the same authority (the shell already makes that
split between `IPageCatalog` and `IPageWriter`):

```csharp
public interface IMemoryCatalog {
    int Maximum { get; }                       // Assistant:MaxMemoriesPerOwner, for the count
    IReadOnlyList<string> Kinds { get; }       // "preference", "fact", "feedback", "reference"
    Task<IReadOnlyList<MemoryNote>> ListAsync();
    Task<MemoryNote?> FindAsync(long id);
}

public interface IMemoryWriter {
    Task<MemorySaveResult> SaveAsync(MemoryEdit edit);
    Task<bool> ForgetAsync(long id);
}
```

```csharp
public sealed record MemoryNote(
    long Id, string Kind, string Content, int Version, DateTimeOffset Written,
    DateTimeOffset? LastUsed, int RecallCount, bool Pinned, long? SourceConversationId);

public sealed record MemoryEdit(long Id, int ExpectedVersion, string Kind, string Content, bool Pinned);

public sealed record MemorySaveResult(bool IsSuccess, MemoryNote? Memory, string? Error);
```

The rules the capability keeps:

1. **The owner is never an argument.** `MemoryCatalogScope` and `MemoryWriterScope` hold the
   request's `PageUser` and pass `user.Name` to the store. A page cannot name an owner, so no page —
   edited by the assistant or written by the owner — can read or change another account's memory
   (DEC-206).
2. **An anonymous render is refused.** Both scopes throw the `UserFacingException` `PageWriterScope`
   already uses, with a sentence that says to sign in. That is the backstop; the seed's own
   `ctx.User` guard is the nicer redirect in front of it (DEC-190's rule, applied to memory).
3. **The writer is not offered to a layout,** for the reason `IPageWriter` is not: a layout is markup,
   and one that pinned a memory would do it on every render it wraps (DEC-194).
4. **Absent, not null, when the assistant is off.** `PageEnvironment` carries the store only while
   `Assistant:Enabled` is true, so the page finds `TryGet<IMemoryCatalog>` false and says the
   assistant is somewhere else — the contract's existing answer for a capability a render does not
   offer.
5. **A page bug throws, a refusal is a value.** An unknown kind name or a second save in one render
   throws `InvalidOperationException`, as a page bug does elsewhere; a stale version, a secret or a
   note over the cap is `MemorySaveResult` with the sentence the store already answers with, so the
   page can show it.
6. **`Kinds` is strings, not an enum.** `Sdk` cannot see `Core.Assistant.MemoryKind` — `Sdk`
   references nothing — and duplicating the enum would be a second thing to keep in step. The page
   builds its select from `Kinds`, and the writer parses the name with `MemoryKinds.TryParse` in
   `Web`.

### D3 — The store becomes a singleton over the context factory

Every page capability is a singleton over `IDbContextFactory<StorageDbContext>` (`IPageReadStore`,
`IPageRevisionStore`), because `PageEnvironment` is a singleton and a page's task can outlive the
request. `SqliteMemoryStore` is scoped over a `StorageDbContext` today because the run and the tools
resolve it from a scope.

The change is mechanical and keeps every rule where it is: the store takes
`IDbContextFactory<StorageDbContext>` and opens one context per call, and each write still opens its
own transaction on that context. The assistant resolves the same singleton from its scope; the
`users remove` sweep and the conversation delete are unaffected, because the delete does its
provenance update in the conversation store's own transaction.

`IMemoryStore`'s interface does not change, so `FakeMemoryStore`, `ShellMemoryStore` and the run
processor's failing double stay as they are.

### D4 — The page

`/memory` is a seeded page — `Pages/Seeds/memory` — with `Pages/Seeds/memory.css` linked through
`IPageLayout.AddStylesheet("/memory.css")`, exactly as `/status` does.

Its shape is the shell page's, moved into a template:

- the ordinary guard on the first line: `if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }`;
- `layout.Title = "Memory"`, one `<h1>`, a `<p>` with the count against `Maximum`, one `<h2>` per kind
  with rows in `MemoryListing` order, and the "nothing remembered yet" sentence when there are none;
- one `form.form` per row posting to `ctx.Path`, with `ctx.AntiforgeryField` written raw, the row's
  `expectedVersion` in a hidden field, the note in a textarea with its label in the fieldset's legend,
  the kind as a select built from `Kinds`, and the pin as a checkbox;
- a link per row to `?forget={id}`, which renders the confirmation in place of the list — the note,
  its kind and date, the sentence about what a delete cannot reach, a form posting back, and a cancel
  link to the bare key.

The actions are one field, `action`, read from `ctx.Request.Form`:

| `action` on a `POST` to `/memory` | What the page does |
| --- | --- |
| `save` | `IMemoryWriter.SaveAsync(new MemoryEdit(id, version, kind, content, pinned))`; `303` to `/memory` |
| `forget` | `IMemoryWriter.ForgetAsync(id)`; `303` to `/memory` |

A `303` after a successful write is DEC-195's rule for a page that saves, and it is what stops a
reload reposting. A refusal does not redirect: the page renders itself with the store's sentence above
the row and `ctx.SetStatus(400)` or `409`, keeping the submitted text (DEC-203 unchanged) and the
version the row is now at, so one more save can fix it.

Two page-authoring details the shell page had for free and the template must do itself:

- **Encode the note.** `<%= ... %>` escapes; a note is never rendered with `ctx.WriteRaw` and never
  through `IMarkdownRenderer`, because it is data the prompt already declares to be data.
- **Unique ids.** Every control's id is `memory-{id}-…`, so two rows cannot claim the same label.

### D5 — The lifecycle around the page

- **Seeding.** `/memory` and `/memory.css` are two more seeds. A new key is created when it is
  missing, so both appear on an existing site at its next start; a *changed* seed still needs the
  owner to restore it from the page list, which the docs say (the same caveat `/help` has).
- **The shell route goes.** `AssistantMemoryEndpoint`, its four `Map…` calls in `Program`, its
  endpoint tests and its shell view fixtures are deleted. The assistant navigation on `/assistant`
  and on a conversation links to `/memory` instead, and the page links back to `/assistant` so the
  transcript is one click away.
- **Reserved keys are unchanged.** `/assistant` is a prefix and stays one; `/memory` is an ordinary
  page key, so `Pages:ReservedPaths`, `ReservedPaths.Default` and the shell-record test do not move.
  The `/assistant` record's description loses its trailing memory sentence; the page is now something
  the assistant may read *and edit*, like `/help`, and `PageAuthorPrompt` says so.
- **The assistant's prompt** names `/memory` instead of `/assistant/memory`, and `Version` becomes
  `page-author-v15`.
- **`Assistant:ProtectedPaths` is not used here.** The page is the owner's, like `/help`: the
  assistant may edit it, and an operator who does not want that locks it the ordinary way.

### D6 — Failure behaviour

- **The signed-in visitor is not the owner of the row.** `FindAsync` and the writer answer as the
  store does: not found. The page renders its 404 sentence and changes nothing.
- **The note was edited by the assistant between the render and the save.** `409` with the stored
  content; the page keeps the submitted text and shows both, as it does today.
- **The store throws.** The render fails, and the shell's error page answers — a page has no error
  document of its own, and inventing one would be a second error page.
- **The assistant is disabled.** The capability is absent; the page renders its "not available here"
  sentence with a `404`, rather than a sign-in loop.
- **The render is a `HEAD`.** The page renders the list and the writer refuses, because a `HEAD` may
  read and never write (`PageWriterScope`'s rule).

## Considered and rejected

- **One `IMemory` with read and write together.** The shell already separates `IPageCatalog` from
  `IPageWriter` for exactly this reason: a page that lists memories has no business forgetting them,
  and the split is what lets the writer be withheld from a layout and offered only where a write
  makes sense.
- **Passing the owner to the capability.** It turns a public page into a way to read anyone's memory.
  The owner comes from the request, as it does for the page's own save.
- **A pinned flag on the assistant's `UpdateAsync`.** DEC-201 stands: the pin is the owner's. The
  capability is the owner's path, and the tool path still has no argument for it.
- **Keeping `/assistant/memory` and calling the page a view of it.** Two addresses for one thing, one
  of them a shell route that has to be maintained, tested and documented. The point of the rule is
  that there is one.
- **A second seeded page for the forget confirmation.** A page is its own route; a query parameter
  and one form is less to seed, less to keep in step and less to write in the docs.
- **A route policy for pages, so the memory page could be owner-only in configuration.** Pages have no
  policy and should not get one: authority that lives in configuration is authority a page cannot
  see, and the guard plus the capability refusal is the model the site already has (DEC-02, DEC-169).
- **Keeping the shell page and only adding the capability.** Then two implementations of the same view
  drift, and the rule has an exception on its first day.
- **Making `/status` a page now.** It is bootstrap: it is what the owner reads when a page did not
  load. Its charts and its Prometheus query are a capability of their own, and that is a different
  plan.
- **Doing the audit's remaining routes now.** Each needs a capability the contract does not have, and
  each is a job. The rule is worth having before the work, not after it.

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| `IMemoryCatalog` | The owner's memories, read, with the cap and the kinds | `Sdk` |
| `IMemoryWriter` | The owner's save and forget | `Sdk` |
| `MemoryNote`, `MemoryEdit`, `MemorySaveResult` | The values and the request | `Sdk` |
| `MemoryCatalogScope` | Binds the request's user, refuses an anonymous render, maps the rows | `Web.Pages` |
| `MemoryWriterScope` | The same for a write, refused on a `HEAD` and absent from a layout | `Web.Pages` |
| `PageChain` | Offers the capabilities per render | `Web.Pages` |
| `PageEnvironment` | Carries the store while the assistant is enabled | `Web.Pages` |
| `Pages/Seeds/memory`, `memory.css` | The page and its sheet | `Web.Pages.Seeds` |
| `docs/architecture.md` and DEC-205 | The rule and the audit | docs |

## Tests

**`Web.Tests`**

- `MemoryCatalogScopeTests` / `MemoryWriterScopeTests` — the user is taken from the context and never
  from an argument (two accounts, one store); an anonymous render is refused with the sign-in
  sentence; a `HEAD` render cannot save; a layout is offered neither writer; an unknown kind and a
  second save in one render throw; the mapping carries every field of `MemoryNote`.
- `MemoryPageTests`, rendering the seed through `PageHarness` with the real store over a
  `DatabaseTemplate` — the signed-out render redirects to sign-in and writes nothing; the list groups
  by kind, pins first, shows the count and the "nothing remembered yet" sentence; the note is encoded;
  `save` changes content, kind and pin and answers `303`; `forget` renders the confirmation, deletes
  nothing on the `GET` and everything on the `POST`; a stale version answers `409` with the stored
  content and the submitted text; a secret answers `400` with the rule and keeps the text; another
  account's id is not found; the assistant being off renders the "not available" sentence.
- `PageHarness` gains a memory fake, and `AccessibilityTests` gains a page fixture so the twelve rules
  apply to the page as they do to a shell view — one `main`, one `h1`, no skipped heading level, a
  label on every control, a textarea's label in its fieldset legend, and the rest.

**`Web.IntegrationTests`**

- `MemoryPageTests` — with two accounts against the real host: the page lists what a run wrote,
  another account's request for the same id changes nothing, a save from the page is in the next run's
  prompt and a pin makes it survive a turn that asks nothing about it, a forget removes it from the
  next request, and with the assistant disabled the page says so instead of failing.
- `PageAssetTests` — the two new seeds are shipped and reachable by key.

**`Web.BrowserTests`**

- `MemoryPageBrowserTests` replaces `AssistantMemoryBrowserTests`, with only the URL changed: reach
  `/memory`, edit, pin, forget through the confirmation, and see the list empty. The test also proves
  the page's script-freedom: any console error fails it, as every browser test here does.

**The gate.** `make check` and a relative-link and anchor check over `docs/`. The browser suite is
`make deploy`'s (DEC-179).

## Phases

Each phase ends with a green `make check` and its tests written first.

### Phase 0 — the rule and the capability (one day)

- DEC-205 and DEC-206 are recorded with this plan; add the paragraph and the audit table to
  [architecture](../architecture.md).
- The `Sdk` types and the two scopes, with `MemoryCatalogScopeTests` and `MemoryWriterScopeTests`.
- `PageChain` and `PageEnvironment` offer them; `PageHarness` gains the fake.

**Done when** a page can read and change only its own visitor's memory, an anonymous render is refused,
and a layout is offered neither.

### Phase 1 — the store becomes shareable (half a day)

- `SqliteMemoryStore` over `IDbContextFactory`, registered as a singleton.
- `IMemoryStore` unchanged; the store, run processor and account-sweep tests still pass.

**Done when** the assistant and the page share one store and `make check` is green.

### Phase 2 — the page (one to two days)

- `Pages/Seeds/memory`, `Pages/Seeds/memory.css`, the layout's sheet and title.
- `MemoryPageTests`: the list, the save, the forget, every refusal, the assistant-off sentence.
- The accessibility page fixture.

**Done when** a seeded page lists, edits, pins and forgets a memory, and the twelve rules pass over it
in a unit test.

### Phase 3 — the shell route goes (half a day)

- Delete `AssistantMemoryEndpoint`, its routes and its tests and shell-view fixtures, and move
  DEC-200 to [superseded](../decisions.md#superseded) now that the route it describes is gone.
- Point the assistant navigation and the prompt at `/memory`; `page-author-v15`; the `/assistant`
  shell record loses its sentence.
- `MemoryPageTests` in `Web.IntegrationTests`, and the browser test at the new key.

**Done when** `/assistant/memory` is a `404` from a page's point of view — it is simply not a route —
and the page is reached from the assistant.

### Phase 4 — documents and the jobs (half a day)

- [assistant](../assistant.md), [storage](../storage.md), [testing](../testing.md) and
  [glossary](../glossary.md): the page, the capability, the suites, **Memory capability** and
  **Bootstrap route**.
- `assistant-memory-page.md` carries its superseded note (added with this plan); this plan's status
  row says landed.
- IDEA-024 and IDEA-025 are in [the backlog](../jobs/README.md), with their origin recorded.
- Link and anchor check.

**Done when** the docs describe what the code does, the audit's remaining rows each name a job, and
`make check` passes.

## Documentation and decisions

- **DEC-205** — Anything a page can serve is a page. A route stays in the shell only because it
  bootstraps a broken site (`/edit`, `/login`, `/logout`, `/status`), because it *is* the page runtime
  (`{**path}`, `/favicon.ico`, `/heath`, `/metrics`), or because a page cannot do it yet — and each of
  those has a job. `/status` counts as bootstrap because diagnosing why a page did not load starts
  with it.
- **DEC-206** — Memory is a page capability, not a shell view: `IMemoryCatalog` and `IMemoryWriter`
  reach a page through `ctx.Features`, the request's signed-in account is the only owner they can
  name, an anonymous render is refused by the capability itself, the writer is withheld from a layout
  and from a `HEAD`, and the capability is absent while the assistant is off. The owner's memory page
  is the seeded `/memory`, which replaces the shell route.

DEC-200 — the memory page as shell code under `/assistant/memory` — moves to
[superseded](../decisions.md#superseded) in phase 3, when the route it describes is deleted, with the
date and DEC-206 as its replacement; it is deliberately not superseded before the code moves. DEC-201
to DEC-204 stand unchanged: the pin is still the owner's, a stale version is still refused, a refusal
still keeps the text, and a forget is still a delete with a page of its own.

Docs changes: [architecture](../architecture.md) gains the "Where a view lives" paragraph and the
audit table; [assistant](../assistant.md) moves the page out of its own section and into the pages it
links; [storage](../storage.md) records that the store is a singleton over the factory and that the
owner's write is reached from a page; [testing](../testing.md) names the suites;
[glossary](../glossary.md) defines **Memory capability** and **Bootstrap route**;
[the jobs](../jobs/README.md) list IDEA-024 and IDEA-025.

## Risks

| Risk | Mitigation |
| --- | --- |
| **The guard is now the page's own code.** A future edit drops `ctx.User` and the page lists memory to an anonymous visitor. | It cannot: the capability refuses an anonymous render before it reads anything, and `MemoryCatalogScopeTests` proves it by name. The guard is the nicer redirect, not the authority (DEC-190, DEC-206). |
| **A page can now write memory, and pages are editable by the assistant.** The assistant could rewrite the page to change what the owner sees or keep. | The page can only touch the signed-in owner's memory, the same person the assistant is talking to; every write is still keyed by the owner and versioned. An operator who wants the page frozen locks it in `Assistant:ProtectedPaths` (DEC-198), as with `/help`. |
| **The memory page can be broken like any page.** A compile error leaves the owner unable to edit memory until `/edit` fixes it. | That is what `/edit` is for, and why it stays shell (DEC-205). The page is deliberately small. |
| **Two implementations of one view drift.** The shell page's tests and the page's tests both describe the same list. | There is only one, after phase 3 deletes the shell endpoint; nothing is left to drift. |
| **`Sdk` grows and every page recompiles.** A new capability moves the Sdk module version and the runtime id. | One deliberate capability with two interfaces, values in `Sdk`, mapping in `Web`; every stored image recompiles on its next request, which is DEC-26's normal cost. |
| **The store's registration change breaks the run path.** The assistant resolved it from a scope. | The interface does not change and the run processor resolves the same service; `RunProcessorTests` and the integration memory flow still pass in phase 1. |
| **The rule becomes an excuse to churn.** Every shell route is "not a page yet" and someone starts a migration per view. | The rule names the three reasons and the plan moves one view; the rest are jobs with their blockers written down, not a queue of migrations. |
| **The seeded page does not appear as expected.** A new key is created on start, but a changed seed is not reapplied. | The page's first landing is a new key, so it seeds; later changes to the seed are restored from the page list, which the docs say. |

## Delivered when

- `docs/decisions.md` carries DEC-205 and DEC-206, DEC-200 is superseded, and
  `docs/architecture.md` states the rule and the audit.
- A page can read its visitor's memories through `ctx.Features.GetRequired<IMemoryCatalog>()` and
  change one through `IMemoryWriter`, and an anonymous render is refused by the capability.
- `/memory` is a seeded page with its own sheet that lists every memory grouped by kind with the count,
  saves content, kind and pin under the row's version, and forgets one through a confirmation — with
  every refusal keeping the owner's text.
- `AssistantMemoryEndpoint` and its four routes are deleted, the assistant navigation and the prompt
  point at `/memory`, and `page-author-v15` is the prompt version.
- `ShellViews` no longer renders a memory view, `AccessibilityTests` holds the page to the same twelve
  rules, and the browser round trip runs against `/memory`.
- IDEA-024 and IDEA-025 are in the backlog with their origin, and ARCH-13 is named as the third
  blocker.
- `docs/assistant.md`, `docs/storage.md`, `docs/testing.md` and `docs/glossary.md` describe what the
  code does; the memory page plan carries its superseded note; this plan's status row says landed.
- `make check` passes.
