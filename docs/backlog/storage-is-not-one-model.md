<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Storage is not one model: a page reaches a patchwork of stores

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Sdk, Runtime, Web (pages, storage, work), docs |
| Location | `src/uk.osric.asp.Sdk/IPageStores.cs` (new), `IValueStore.cs` (new), `IRecordStore.cs` (new), `IMessageStore.cs` (new), `IObjectStore.cs` (new), `StoreLifetime.cs` (new), `StoreFilter.cs` (new), `PageValue.cs`, `IContext.cs`, `IPageResponse.cs`, `src/uk.osric.asp.Web/Pages/PageStoresScope.cs` (new), `PageChain.cs`, `PageStorageScope.cs`, `PageSessionScope.cs`, `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `privacy`, `_layout`, `editor-mode`, `_lib/editor`, `docs/storage.md`, `docs/pages.md`, `docs/plans/account-store.md`, `docs/plans/page-inbox.md` |
| Spike | None yet; the SQL sandbox is the spike the plan needs |

## Idea

A page reaches stores that were each designed for the case in front of them: `IPageStorage` (a page's
key/value, shared by every visitor), `IPageSession` (one browser's key/value), the page store read
and written through `IPageCatalog`/`IPageWrites`, and a job's journal and channel. Two more are
planned as separate capabilities — the per-account SQL store (`no-account-store`) and the inbox
(`message-does-not-survive-a-redirect`) — and each would arrive with its own table, its own options
section, its own cap unit and its own lifetime clock.

So the same question is answered five times and differently: **whose data is this, and how long does
it live?** The subject key is a page path, a session id, an account name or "account or session".
The cap unit is per page, per visitor, per inbox and per (account, page). The lifetime is the page's,
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
**the subject is the binding, the shape is the interface.** Lifetime and caps belong to the subject;
authority and TTL belong to the verb.

## The model

Two subjects, four shapes. `Site` is a reserved row, not a subject, until a use case needs it.

| | Page | Visitor |
| --- | --- | --- |
| Value (JSON, ordinal key, last-write-wins) | yes | yes |
| Record (declared table, SQL) | yes | yes |
| Message (durable, consumed once) | — | yes |
| Object (bytes, content-addressed) | yes | yes |

- **Page** — shared by every visitor; the page's own data. Key: the page key. Lifetime: the page's;
  survives edit and restore, moves with the key, and is the only subject an archive carries.
- **Visitor** — this person. Key: the session id while anonymous, the account when signed in. On
  sign-in its `Person`-lifetime rows are re-keyed into the account in one transaction; adoption is a
  re-key, not a read, so a one-shot survives it.
- **Site** — reserved. It is the multi-writer case only (many pages setting one shared cell), and it
  needs a governance rule first. Cross-page *reads* are served by the Page subject instead.

Lifetimes are a property of the entry, returned by a read and selectable in a listing filter:

| Lifetime | Dies when |
| --- | --- |
| `Person` (default) | the account or session that owns it goes |
| `Session` | the session rotates — sign-out and sign-in both |
| `NextRead` | the next successful read of that key, atomically |
| `Until(expiry)` | the clock says so; composes with the others |

- `Session` is a **lifetime, not a subject**. It exists because NoScript visitors are first class:
  per-browser state must be readable by the server, so localStorage is at most a progressive
  enhancement.
- `NextRead` is a keyed one-shot. `TryGetValueAsync` on a `NextRead` key is a single
  `DELETE … RETURNING`; `KeysAsync` lists without consuming. The caller cannot know the lifetime
  before it reads, so a consumed value reads as not found afterwards.
- **N reads is not a lifetime.** A countdown is state the page owns: a value plus an integer under
  another key, and a Record if the decrement must be atomic. A lifetime is a rule the store can
  enforce without knowing what the page is doing.

The rest of the shape:

- **Reads are open across page keys; writes are bound to the caller's key.** `/a` reads `/_lib/b`'s
  Page values with `TryGetValueAsync("/_lib/b", key)`. A page may not write another page's key. The
  one deliberate escape is `ctx.Stores.For(libraryKey)`, restricted to a key that names a library, so
  a library's shared cache has a home without letting a page write a page.
- **Authority lives in the verbs, not in the layer.** Every write refuses a `HEAD`, an abandoned
  render and a passed deadline, on every subject and every layer. Content, layout and embedded all
  get `ctx.Stores`; the page-source write keeps its own signed-in, one-action rules and stays out of
  the data stores, because source is content.
- **Caps are per subject instance**, not per store: a person's values and messages draw on one
  person's budget, and the numbers are chosen together.
- **Objects have both doors.** A site route `/o/{sha}` serves bytes with `ETag`, `Range` and a cache
  policy that follows the subject; a page may also open the bytes and transform them — an image
  filter, a re-encode — and put the result back, where content addressing dedupes it. Private
  subjects are checked for ownership on the route.
- **A job gets `Page` bound to the page that started it and `Visitor` bound to the starter**, and no
  `Session` at all. A library never renders, so it has no namespace of its own by default; it uses
  the including page's handles, or `For(libraryKey)` when its state must be shared across callers.

## What this replaces

The item is the intent to replace the current model, not a sixth store beside it:

| Today | New |
| --- | --- |
| `IPageStorage` | `ctx.Stores.Page.Values` |
| `IPageSession` | `ctx.Stores.Visitor.Values` with `StoreLifetime.Session`, or `Person` for data that should follow |
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
stores, and neither plan should be approved while this is unanswered.

## Open questions

Nothing is planned or built until the owner answers. The first two decide the shape of the work; the
rest can wait for the plan.

1. **How much SQL is a page allowed?** Four verbs over declared tables, or joins and aggregates too?
   This is the cost centre: the sandbox, the `TEMP VIEW` owner filter, the caps and the schema
   extraction all move with the answer. The owner is returning to this.
2. **The library write door.** `For(libraryKey)` is the proposal; the alternative is that library
   state belongs to its callers and no escape exists. The first library that wants a shared cache or
   version marker should pick the restriction.
3. **The editor-mode seed.** It is the only per-browser use today. Is its preference `Session` (dies
   at sign-in) or `Person` (follows the account)? If "survives sign-in on this browser" is wanted,
   that is a stable device cookie and a `Browser` subject, and it needs its own decision.
4. **Cross-page records.** Values are read across pages from the start; a queryable table declared by
   one page and read by another is a schema dependency the catalogue would have to track. Deferred
   until a caller needs it.
5. **The worker's own key.** A job writes the starting page's Page namespace, so the worker source's
   own key has no data writer. Whether a job should also get a handle bound to the worker is open;
   nothing needs it today.
6. **The archive.** It carries the Page subject, so its `Pages` and `PageState` tables grow the
   lifetime columns, or `NextRead`/`Until` values are refused on export. The plan decides which.

## Not in scope

- **Assistant memory.** `AssistantMemories` stays the assistant's, as
  `assistant-memory-belongs-to-the-assistant` records.
- **Cross-account reads, sharing and roles.** One owner per row remains the rule.
- **A site subject.** Reserved until many pages must write one shared cell, and then only with a
  governance rule for declaration and write authority.
- **N-read lifetimes.** A countdown is page code.
- **Migrating another site's data.** The archive carries the Page subject and nothing else.

**Origin:** the owner's question of 2026-11-21, thinking about the shape of the storage gaps after
the account-store and inbox plans were drafted: "they feel a bit disjointed". The model above is the
outcome of that conversation, and the SQL sandbox is the next thing to answer.
