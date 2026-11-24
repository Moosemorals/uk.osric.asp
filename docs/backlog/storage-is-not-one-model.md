<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Storage is not one model: a page reaches a patchwork of stores

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [storage-model](../plans/storage-model.md) |
| Area | Sdk, Runtime, Web (pages, storage, work), docs |
| Location | `src/uk.osric.asp.Sdk/IPageStores.cs` (new), `IValueStore.cs` (new), `IRecordStore.cs` (new), `IMessageStore.cs` (new), `IObjectStore.cs` (new), `ISecretStore.cs` (new), `StoreLifetime.cs` (new), `StoreFilter.cs` (new), `PageValue.cs`, `IContext.cs`, `IPageResponse.cs`, `src/uk.osric.asp.Web/Pages/PageStoresScope.cs` (new), `PageChain.cs`, `PageStorageScope.cs`, `PageSessionScope.cs`, `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `privacy`, `_layout`, `editor-mode`, `_lib/editor`, `docs/storage.md`, `docs/pages.md`, `docs/plans/account-store.md`, `docs/plans/page-inbox.md` |
| Spike | [The record sandbox](../spikes/store-sandbox.md) |

> **Planned 2026-11-21** — [one storage model](../plans/storage-model.md) designs the record sandbox,
> settles the two subjects and five shapes, and slices the replacement into a spike, six slices and
> the release that closes the window, each its own branch and its own stop. It absorbs the two plans
> waiting for a yes — records for [`no-account-store`](no-account-store.md), messages for
> [`message-does-not-survive-a-redirect`](message-does-not-survive-a-redirect.md) — and neither of
> those should be approved. The secrets slice was added at the owner's request on 2026-11-22, with
> [`a-page-cannot-keep-a-secret`](a-page-cannot-keep-a-secret.md) recorded as the item it delivers.
> The old capabilities stay one release as obsolete adapters and then go.
> The owner said yes on 2026-11-21. Slice 0 — the sandbox spike — is
> [written](../spikes/store-sandbox.md); slice 1, values, lifetimes and identity, and slice 2,
> records and the sandbox, slice 3, messages, and slice 4, objects, are merged, and slice 5, secrets,
> is built on its branch and waits for the owner.

## Idea

A page reaches stores that were each designed for the case in front of them: `IPageStorage` (a page's
key/value, shared by every visitor), `IPageSession` (one browser's key/value), the page store read
and written through `IPageCatalog`/`IPageWrites`, and a job's journal and channel. Two more are
planned as separate capabilities — the per-account SQL store (`no-account-store`) and the inbox
(`message-does-not-survive-a-redirect`) — and each would arrive with its own table, its own options
section, its own limit unit and its own lifetime clock.

So the same question is answered five times and differently: **whose data is this, and how long does
it live?** The subject key is a page path, a session id, an account name or "account or session".
The limit unit is per page, per visitor, per inbox and per (account, page). The lifetime is the page's,
30 minutes, eight hours, thirty days from activity, or the job's. Nothing is wrong in isolation; the
set is incoherent, and the gaps fall between the parts:

- no queryable store that a page, a browser or the site owns — only the account's, which does not
  exist yet;
- no large bytes anywhere, so an uploaded image has no home;
- no atomic change to a page value, so a counter is racy;
- page values are unreadable across pages, while session values are readable across pages, with no
  reason for the asymmetry;
- an anonymous visitor's data is lost at sign-in, or would be under the inbox plan, with no rule that
  says which is intended.

The owner's decision of 2026-11-21 is to replace the model rather than add a sixth store. The rule is:
**the subject is the binding, the shape is the interface.** Lifetime and limits belong to the subject;
authority and TTL belong to the verb.

## The model

Two subjects, five shapes. `Site` is a reserved column, not a subject, until a use case needs it.

| | Page | Visitor |
| --- | --- | --- |
| Value (JSON, ordinal key, last-write-wins) | yes | yes |
| Record (declared table, SQL) | yes | yes |
| Message (durable, consumed once) | — | yes |
| Object (bytes, content-addressed) | yes | yes |
| Secret (opaque text, never returned by anything else) | yes | — |

- **Page** — shared by every visitor; the page's own data. Key: the page key. Lifetime: the page's;
  survives edit and restore, moves with the key, and is the only subject an archive carries.
- **Visitor** — this person. Key: the session id while anonymous, the account when signed in; a
  `Browser`-lifetime row is keyed by the browser id instead. On sign-in its `Person` rows are re-keyed
  into the account in one transaction; adoption is a re-key, not a read, so a one-shot survives it.
  `Session` rows go with the rotation, and `Browser` rows are untouched.
- **Site** — reserved. It is the multi-writer case only (many pages setting one shared cell), and it
  needs a governance rule first. Cross-page *reads* are served by the Page subject instead.

Lifetimes are a property of the entry, returned by a read and selectable in a listing filter:

| Lifetime | Where | Dies when |
| --- | --- | --- |
| `Person` (Visitor default) | Visitor | the account goes; anonymous, the session, until adoption moves it |
| the page's own (Page default) | Page | the page goes |
| `Session` | Visitor | the session rotates — sign-out and sign-in both |
| `Browser` | Visitor | the browser's cookie is cleared or expires; survives sign-in and sign-out |
| `NextRead` | both | the next successful read of that key, atomically |
| `Until(expiry)` | both | the clock says so; composes with the others |

- `Session` and `Browser` are **lifetimes, not subjects**. Both exist because NoScript visitors are
  first class: per-browser state must be readable by the server, so localStorage is at most a
  progressive enhancement.
- `Browser` needs an identity that outlives the session. The host mints a long-lived `HttpOnly` `b`
  cookie on the first `Browser` write; it is not the `s` session id, is never linked to an account,
  and dies when the visitor clears cookies or its configured lifetime passes. Editor mode becomes
  `Browser`, and the seeded `/privacy` page names the cookie. A read by key searches the session
  scope, then the browser and then the person for the current reader, and answers which lifetime it
  found: the more ephemeral value shadows the longer-lived one.
- `NextRead` is a keyed one-shot. `TryGetValueAsync` on a `NextRead` key is a single
  `DELETE … RETURNING`; `KeysAsync` lists without consuming. The caller cannot know the lifetime
  before it reads, so a consumed value reads as not found afterwards.
- **N reads is not a lifetime.** A countdown is state the page owns: a value plus an integer under
  another key, and a Record if the decrement must be atomic. A lifetime is a rule the store can
  enforce without knowing what the page is doing.

The rest of the shape:

- **Reads are open across page keys; writes are bound to the caller's key.** `/a` reads `/_lib/b`'s
  Page values with `TryGetValueAsync("/_lib/b", key)`. A page may not write another page's key, and
  that is **ownership, not protection**: page code runs in-process with the site's identity
  (`pages-there-is-no-sandbox`), and the page store's source is deliberately writable by any page the
  visitor can use (`pages-any-page-may-write-the-store`). The difference is whose namespace it is.
  Source is the site's, so the credential authorizes any render to write it; data is the subject's, so
  only the subject's own render writes it. The binding keeps one writer per namespace and gives a
  page's delete, move, scrub and archive a single owner. Many pages writing one shared cell is the
  reserved `Site` subject, not a page key they all name.
- **The one deliberate escape is `ctx.Stores.For(key)`**, restricted to a key that names a library or
  a worker: shared code owns its namespace and writes it, and a page reaches the data by calling the
  code.
- **Authority lives in the verbs, not in the layer.** Every write refuses a `HEAD`, an abandoned
  render and a passed deadline, on every subject and every layer. Content, layout and embedded all
  get `ctx.Stores`; the page-source write keeps its own signed-in, one-action rules and stays out of
  the data stores, because source is content.
- **Limits are per subject instance**, not per store: a person's values and messages draw on one
  person's budget, and the numbers are chosen together.
- **Objects have both doors.** A site route `/o/{sha}` serves bytes with `ETag`, `Range` and a cache
  policy that follows the subject; a page may also open the bytes and transform them — an image
  filter, a re-encode — and put the result back, where content addressing dedupes it. Private
  subjects are checked for ownership on the route.
- **Shared code owns its namespace.** A library declares its tables and its data under its own key,
  and a page calls the library's methods; the page never names the library's key or tables. That is
  the approved pattern: **the library declares the schema, the page calls the library's methods, and
  the methods reach the data.** Declarations are applied when the declaring source is saved, not when
  a caller is saved, and a change to a library recompiles the pages that depend on it.
- **A job gets three scopes.** `Page` bound to the page that started it is the caller's data;
  `Visitor` bound to the starter is the person's; `For(workerKey)` is the worker's own data, shared
  by every run of that worker. The run itself has no store — `IWorkerContext` is in memory — and a
  job gets neither `Session` nor `Browser`, because it has no browser.

## What this replaces

The item is the intent to replace the current model, not a sixth store beside it:

| Today | New |
| --- | --- |
| `IPageStorage` | `ctx.Stores.Page.Values` |
| `IPageSession` | `ctx.Stores.Visitor.Values` with `StoreLifetime.Session`, `Browser` for a device preference, or `Person` for data that should follow |
| planned `IPageInbox` | `ctx.Stores.Visitor.Messages`; the flash becomes `NextRead` |
| planned `IPageAccountStorage` | `ctx.Stores.Visitor.Records` |
| `PageState`, `PageSessionValues` | one `StoreValues` table with lifetime columns |
| `Pages:Storage`, `Pages:Session` | one `Pages:Stores` record with a row per subject |
| `IPageCatalog` / `IPageWrites` | unchanged: the page/source store, not a data store |
| job journal / channel | unchanged: a run is a transport, not a subject |

It also settles the reach question from the same conversation: a layer's role decides the **response**
it owns, not what it can reach. `pages-one-render-three-roles` and `pages-layouts-are-markup` keep
their response half and lose their capability half, and the layout, `HEAD` and embedded exclusions on
`IPageWrites`, `IPageJobs` and the session store go.

The two plans already waiting for a yes — [account-store](../plans/account-store.md) and
[page-inbox](../plans/page-inbox.md) — are superseded in shape. Their items become cells rather than
stores, and neither plan should be approved: this item replaces what they would have built.

## What the owner answered

The six questions put on 2026-11-21 are answered. They settle the model; the plan still designs the
bounds.

1. **SQL includes joins and aggregates.** The record store is not four verbs over one table. Joins and
   aggregates are in scope, over the tables the page or library declares for its subject, bounded by
   the result-row and result-character limits and the statement timeout. `ATTACH`, `PRAGMA` and DDL stay
   out; the plan designs the allow-list and the owner filter that a join sees.
2. **Libraries own state.** `For(libraryKey)` is approved, with the pattern in the model: the library
   declares the schema, pages call its methods, and its methods reach the data.
3. **`Browser` is the answer for editor mode.** A `Browser` lifetime survives sign-in and sign-out on
   one device without following the person. It needs the stable `b` cookie above, is not adopted, and
   the privacy page names it. Editor mode becomes `Browser`, not `Session` and not `Person`.
4. **The library pattern is the approved way to share a schema.** A page reads another page's values
   by key, but a shared queryable table is the library's: the page calls the library's methods and
   never queries the table itself.
5. **Static versus instance, yes.** The worker's own key is the code's static namespace, the starting
   page is the caller's, and the starter's `Visitor` is the person's. The run has no store. A worker
   therefore gets `For(workerKey)` as well as `Page` and `Visitor`, for state shared by every run of
   that worker.
6. **The archive carries no ephemeral values.** It keeps its two tables unchanged and exports only
   Page values with the page's own lifetime; a `NextRead` or `Until` value is skipped, because
   exporting it would make a one-shot fire at the destination. The rest of the question was whether
   `PageState` would need lifetime columns to round-trip them; it does not.

## Not in scope

- **Assistant memory.** `AssistantMemories` stays the assistant's, as
  `assistant-memory-belongs-to-the-assistant` records.
- **Cross-account reads, sharing and roles.** One owner per row remains the rule.
- **A page reading another page's declared table.** A shared schema is the library's and is reached
  through its methods; the declaration belongs to the source that wrote it.
- **A site subject.** Reserved until many pages must write one shared cell, and then only with a
  governance rule for declaration and write authority.
- **N-read lifetimes.** A countdown is page code.
- **Migrating another site's data.** The archive carries the Page subject and nothing else.

**Origin:** the owner's question of 2026-11-21, thinking about the shape of the storage gaps after
the account-store and inbox plans were drafted: "they feel a bit disjointed". The model above is the
outcome of that conversation. The six questions it raised are answered above; the next step is a plan
that designs the record sandbox and slices the work.
