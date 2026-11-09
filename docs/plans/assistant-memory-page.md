<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The memory page

| | |
| --- | --- |
| Status | Landed |
| Size | Medium — two store operations, four routes, a list-and-form view, one seed's styles and the tests for each |
| Spike | None. [The memory spike](../spikes/assistant-memory.md) already settled the store; this plan only reads and writes it |
| Decisions | DEC-200–DEC-204, recorded when this lands |
| Backlog | [IDEA-023](../jobs/README.md#closed), which this plan closed |

## The short version

[Assistant memory](assistant-memory.md) is real: the assistant writes notes with `remember`,
replaces them with `update_memory`, deletes them with `forget_memory`, and every run is given the
relevant ones in its system prompt. What the owner has is the transcript of the turn that wrote a
memory and nothing else. This plan gives them the page IDEA-023 asked for: a list of every memory,
grouped by kind, each row a form that saves the content, the kind and a pin, and a forget that asks
before it deletes.

- **Four routes under `/assistant/memory`.** `GET` lists; `POST /assistant/memory/{id}` saves;
  `GET` then `POST /assistant/memory/{id}/forget` confirms and deletes. All behind the `Editor`
  policy, all owner-filtered, all server-rendered with no JavaScript.
- **Two store operations more.** `ListAsync` reads the owner's memories and `EditAsync` changes one
  under the same version check, normalisation and secret rule the tools use. `MemoryCandidate` gains
  the conversation a memory came from, so the page can link back to it.
- **Pinning becomes a real control.** `EditAsync` is the only write that can set `Pinned`; the
  assistant's `update_memory` still cannot, and the recall selector already puts pinned memories
  first. The page is what makes the column worth having.
- **One pure rule in `Core`.** `MemoryListing.Group` decides the order the page draws: four kinds in
  the store's own order, pinned first inside a kind, newest change first after that.

Nothing about recall changes. The page writes memory the way the tools do, so a memory pinned on it
is in the next run's block and a memory forgotten on it is gone from the next run.

## Goals

1. The owner can see everything the assistant believes about them, in one place, without reading a
   transcript.
2. The owner can correct a memory, change its kind, or pin it so a query that does not mention it
   still finds it — and every one of those is a form on the page, not a conversation.
3. Forgetting is a deliberate act with a confirmation, and it really is a delete: the row, its
   history and its search entry.
4. A save from a stale page is refused rather than silently overwriting an edit the assistant made
   while the page sat open.
5. The page is the same kind of thing as `/status` and `/edit`: shell code, owner-only, a document
   per response, no JavaScript, and every form carrying the antiforgery pair.
6. The count against the cap is visible, so "the assistant is full" is known before a run fails to
   remember something.

## Non-goals

- **A new way to write memory.** The page calls the store; it does not grow a second set of rules.
  Normalisation, the length cap, the duplicate rule and the secret refusal stay where they are.
- **A discussion with the assistant.** "Ask it to remember this" stays the transcript's job. The page
  edits what is already there and has no box for a new note: a memory should be attributable to a
  turn, and a page that invented one could not say which.
- **A bulk forget, or a reset.** IDEA-023 says a "forget everything" wants a typed confirmation and
  its own decision, like a page scrub; this plan leaves it out on purpose.
- **Export and import.** The archive carries page source and page state and nothing else (DEC-139);
  memory is neither, and the backup story is still the SQLite file.
- **Showing what the model is told.** The page shows memories, not the block. The block is a
  selection of them within a character budget, and drawing it would invite the owner to treat it as
  a document to keep tidy.
- **Team memory, sharing or scopes.** One owner per row is the security model; the page does not
  invent a second one.
- **A JavaScript editor.** A memory is a sentence; a textarea and a save button are the whole
  interaction, and the page must read without a script exactly as `/status` does (DEC-81).

## What exists today

The memory feature landed with [its plan](assistant-memory.md); the parts this page stands on:

| Piece | Today | Used here for |
| --- | --- | --- |
| `IMemoryStore` / `SqliteMemoryStore` | `RecallAsync`, `StickyAsync`, `PinnedAsync`, `RememberAsync`, `UpdateAsync`, `ForgetAsync`, `TouchAsync`, `CountAsync`, the provenance sweep and the account sweep, every statement filtered by owner | The page's reads and its one write; gains `ListAsync` and `EditAsync` |
| `MemoryCandidate` | id, kind, content, version, written, last used, use count, pinned | One row; gains `SourceConversationId` |
| `MemoryContent` / `MemorySecrets` | normalisation, the length cap, the secret rules and the sentences they answer with | The page's validation, unchanged and shared |
| `MemoryRecall` / `MemorySelection` | the block and the selection, pinned first | Nothing — pinning is honoured there already |
| `AssistantEndpoint` | `/assistant`, the conversation view, its delete confirmation, the approval and stream routes | The page's shape: `ShellDocument`, `PageDocument.Nav`, `AntiforgeryField`, `Redirect.SeeOther`, `ErrorResponses` |
| `assistant.css` | the conversation list, the transcript and the budget panel | The page's rules, as a new section of the same seed |
| `AssistantOptions` | `MaxMemoriesPerOwner`, `MaxMemoryChars`, `MaxMemoriesPerRun`, `MaxMemoryCharsPerRun` | The count and the form's cap |
| `ShellRoutes` / `ReservedPaths` | one record per reserved key; `/assistant` is a prefix, so `/assistant/memory` is already reserved and unreachable as a page | The `/assistant` record's description, and the page's status as a route the assistant may read and cannot save |
| `PageAssetSeeder` | seeds a page only when no head exists | Why a changed `assistant.css` reaches a fresh site and an existing one only through the editor's restore |

IDEA-023's sketch is the starting point. Three things this plan adds to it, because the store now
exists: `MemoryCandidate` has to carry the conversation a memory came from for the "from a deleted
conversation" line to be possible; the row's save needs a store operation of its own rather than
being composed out of the tool's `UpdateAsync`, which cannot touch `Pinned`; and every refusal has to
re-render the row, because the textarea's content is the owner's work.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Assistant/MemoryCandidate.cs` | `SourceConversationId` |
| `src/uk.osric.asp.Core/Assistant/MemoryListing.cs`, `MemoryGroup.cs` | The grouping rule and its result |
| `src/uk.osric.asp.Core/PublicAPI.Unshipped.txt` | The new public members |
| `src/uk.osric.asp.Web/Assistant/IMemoryStore.cs`, `SqliteMemoryStore.cs` | `ListAsync` and `EditAsync`, and the source conversation on the rows they return |
| `src/uk.osric.asp.Web/Assistant/AssistantMemoryEndpoint.cs` | The four routes and the view |
| `src/uk.osric.asp.Web/Assistant/AssistantEndpoint.cs` | A "Memory" link in the assistant navigation |
| `src/uk.osric.asp.Web/Assistant/ShellRoutes.cs` | The `/assistant` record's description mentions the memory list |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | One sentence about the page; `Version` to `page-author-v14` |
| `src/uk.osric.asp.Web/Program.cs` | The four routes, inside the assistant's own block |
| `src/uk.osric.asp.Web/Pages/Seeds/assistant.css` | The page's rules, after the conversation list and before the chat |
| `test/uk.osric.asp.Core.Tests/MemoryListingTests.cs` | The grouping rule |
| `test/uk.osric.asp.Web.Tests/AssistantMemoryStoreTests.cs` | `ListAsync` and `EditAsync` against `DatabaseTemplate` |
| `test/uk.osric.asp.Web.Tests/AssistantMemoryPageTests.cs` | The view and every refusal, over a hand-built `HttpContext` |
| `test/uk.osric.asp.Web.Tests/ShellViews.cs`, `ShellMemoryStore.cs` | The page and the forget confirmation as shell views, so the accessibility sweep covers both |
| `test/uk.osric.asp.Web.IntegrationTests/AssistantMemoryPageTests.cs` | Two accounts against the real host, and the page's effect on the next run |
| `test/uk.osric.asp.Web.BrowserTests/AssistantMemoryBrowserTests.cs` | The round trip in a browser |
| `docs/assistant.md`, `docs/storage.md`, `docs/testing.md`, `docs/glossary.md` | The page, the owner's write, the suites, the terms |
| `docs/jobs/IDEA-023.md`, `docs/jobs/README.md` | The job closes |

Nothing in `Sdk`, `Runtime` or the page contract changes: a stored page cannot reach memory, and the
page is shell code (DEC-184).

## Design

### D1 — Two store operations and one field

`IMemoryStore` gains exactly what the page needs and nothing else:

```csharp
Task<IReadOnlyList<MemoryCandidate>> ListAsync(string owner, CancellationToken cancellationToken = default);
Task<MemoryWriteResult> EditAsync(
    string owner,
    long id,
    int expectedVersion,
    MemoryKind kind,
    string content,
    bool pinned,
    CancellationToken cancellationToken = default);
```

`GetAsync` reads one row for the confirmation page and for deciding which refusal a failed save is;
it is the same owner-filtered lookup, so an id that is not the owner's is not there. `ListAsync` is a
read: every row the owner holds, mapped to `MemoryCandidate`. It does not filter by
kind or by pinned, because the page draws all of it and the grouping decides the order; it does not
page, because `MaxMemoriesPerOwner` is the page's own limit. The rows are ordered in C# by `Written`
descending and then by id descending, for the same reason `SqliteConversationStore.GetLastRunAsync`
orders runs that way: SQLite cannot order by a `DateTimeOffset`, and two hundred rows are cheaper to
sort here than to store as a sortable column.

`EditAsync` is the owner's write and the only one that can set `Pinned`:

1. Normalise the content with `MemoryContent.Normalise` and refuse a secret with `MemorySecrets`,
   with the same sentences `RememberAsync` uses — the owner is as likely to paste a token as the
   model is, and DEC-188 is about what the store holds, not about who typed it.
2. Open one transaction and read the row by id and owner. A row that is missing, or belongs to
   somebody else, is the same answer: `No memory with id {id}.`
3. Refuse a version that is not `expectedVersion`, answering with the row's current content, exactly
   as `UpdateAsync` does, so a page left open while the assistant edited the memory cannot overwrite
   that edit.
4. Append the row as it stands — version, kind and content — to `AssistantMemoryEdits`, then set the
   content, the kind, `Pinned` and `UpdatedAt`, bump `Version`, and keep `SourceConversationId` and
   `SourceRunId`: an edit does not change where the memory came from, and the history row records
   the conversation it was edited from.
5. Return the row as it now stands, so the view can render what was stored rather than what was
   submitted.

The assistant's `UpdateAsync` keeps its signature, which has no `pinned` argument at all: a model
cannot pin a memory because there is no path through the executor that could say so, and a store
test asserts that a tool write leaves `Pinned` alone. Pinning is the owner's, and this page is where
they exercise it (DEC-201).

`MemoryCandidate` gains one field, `long? SourceConversationId`, and the store's projections add the
column to the three selects that already build a candidate (`RecallAsync`, `StickyAsync`,
`PinnedAsync`, and the new `ListAsync`). The tool results do not show it — the model has no use for
a conversation id it cannot open — so `MemoryData` in `PageToolExecutor` keeps its five fields.

### D2 — Routes and refusals

| Route | Method | Answers |
| --- | --- | --- |
| `/assistant/memory` | `GET` | The list: every memory, grouped by kind, pinned first inside a kind, with the count against the cap |
| `/assistant/memory/{id:long}` | `POST` | Saves content, kind and pinned; `303` to the list |
| `/assistant/memory/{id:long}/forget` | `GET` | The confirmation page, showing the memory as it stands |
| `/assistant/memory/{id:long}/forget` | `POST` | Deletes the row, its history and its index entry; `303` to the list |

The routes are registered inside the assistant's own `if (assistantEnabled)` block, so a site with the
assistant switched off answers them with the same "not available" page every other `/assistant` path
gets. `{id:long}` binds digits only, so `/assistant/memory` never collides with
`/assistant/{id:long}`, and the memory routes are declared next to the conversation ones rather than
in a second place.

The refusals, each a document rather than a status:

| Case | Status | What the page says |
| --- | --- | --- |
| No signed-in owner | `404` | The conversation-not-found page, which is what every owner-scoped assistant route answers an anonymous caller |
| An id that is not the owner's, or is not there | `404` | `assistant.memory_not_found` — "That memory does not exist", remedies to `/assistant/memory` |
| A form the antiforgery check refused | `400` | `assistant.form_expired` — nothing was saved, reload and try again |
| Content that is empty, too long, or looks like a secret | `400` | The list again, with the row's submitted text kept and the store's own sentence above it |
| A version the assistant has overtaken | `409` | The list again, with the row showing the stored content and a line saying the assistant changed it |

The last two are the interesting ones. A bare error page would throw away what the owner typed, and
the textarea is the only place that text exists: so a failed save re-renders the list with the
offending row in an editing state, carrying the submitted values and an error line, and answers
`400` or `409`. That is the same choice the editor makes for a page that does not compile — the
diagnostics come back with the source rather than instead of it (DEC-203).

### D3 — The list

The document is the shell's: `ShellDocument.BeginAsync` with `/assistant.css`, the site menu from
`SiteNavigation.For`, and an assistant navigation landmark of Conversations, Memory (current) and
Page list. Then:

- an `<h1>Memory</h1>`;
- a `<p class="assistant-memory-count">` — "12 of 200 memories", or "200 of 200 memories, so the
  assistant cannot remember anything new until one is forgotten" at the cap, and a sentence saying so
  when there are none: "The assistant has not remembered anything yet. It writes a memory with the
  `remember` tool, and the conversation that wrote it shows the call.";
- one `<h2>` per kind that has memories, in `MemoryKinds.All` order — Preference, Fact, Feedback,
  Reference — and nothing for a kind with none;
- one article per memory, each holding a form.

A row's meta line says whether the memory is pinned, when it was written and how often it has been
used, and where it came from — a link to the conversation while that conversation exists, and "from a
deleted conversation" when `SourceConversationId` is null, which is what the conversation delete
leaves behind (DEC-184). The note itself is the row's textarea, so the page shows it once: a second,
read-only copy above an editable one is the same text twice. The use count reads "never used" or "used 4
times, last on 2026-11-06". The content is written as text, HTML-encoded by the view: a memory is
data that goes into a prompt, and the page that shows it draws a `<script>` in it as characters, not
as an element (DEC-200).

The grouping and the order inside a group are one pure rule, `MemoryListing.Group` in `Core`:

```csharp
public sealed record MemoryGroup(MemoryKind Kind, IReadOnlyList<MemoryCandidate> Memories);

public static class MemoryListing {
    public static IReadOnlyList<MemoryGroup> Group(IReadOnlyList<MemoryCandidate> memories);
}
```

It keeps the four kinds in order, drops a kind with nothing in it, and orders each group pinned first
and then by `Written` descending, with the id breaking a tie so the page cannot reorder itself
between two renders. An empty list is an empty result, not four empty headings: the page writes the
"nothing remembered" sentence instead.

### D4 — The form

Every row is its own form, `class="form"`, posting to `/assistant/memory/{id}`:

```html
<form method="post" action="/assistant/memory/42" class="form">
    <input type="hidden" name="__RequestVerificationToken" value="…">
    <input type="hidden" name="expectedVersion" value="3">
    <fieldset>
        <legend>Memory 42</legend>
        <textarea id="memory-42-content" name="content" maxlength="1000" rows="4">…</textarea>
    </fieldset>
    <dl>
        <dt><label for="memory-42-kind">Kind</label></dt>
        <dd><select id="memory-42-kind" name="kind">…</select></dd>
        <dt><label for="memory-42-pinned">Pinned</label></dt>
        <dd><input type="checkbox" id="memory-42-pinned" name="pinned" value="on" checked></dd>
    </dl>
    <button type="submit">Save memory</button>
</form>
```

The textarea's label is its fieldset's legend, which is the form rule `layout.css` already enforces,
so the row looks like the editor's source box rather than like a new kind of control; the fieldset's
legend says which memory the box is, and the visible content above the form already says what it
holds. Every id is `memory-{id}-…`, so two rows cannot claim the same label. `maxlength` is
`Assistant:MaxMemoryChars`, so the cap is the browser's first and the store's second, and the store is
still the authority.

The select offers the four kinds by their stored names, with the current one selected. The checkbox
is the pin, and its checked state is `Pinned`. The hidden `expectedVersion` is what the row was
rendered from, and it is the whole of the concurrency story: the form is refused if anything wrote
the memory in between.

The save reads `content`, `kind`, `pinned` and `expectedVersion` from the form, refuses a request with
no form, more than one value for a field, a missing or unparsable `expectedVersion`, or an unknown
kind name with the `assistant.form_expired`/`assistant.memory_invalid` pages, and otherwise calls
`EditAsync` and redirects `303` to `/assistant/memory`. A success message is not needed: the reload
shows the row as saved, which is the message.

### D5 — Forget

A row carries a link to `/assistant/memory/{id}/forget`, not a button: deleting is the one action that
cannot be undone, and it gets a page of its own, exactly as deleting a conversation does.

The confirmation renders the memory as it stands — the content, its kind and its written date — asks
"forget this memory?", offers a `POST` button and a cancel link, and says what the delete reaches:

> Forgetting deletes the memory, its history and its search entry. It does not reach backups,
> snapshots or the operator's copies of the database: that is the same best-effort erasure a page
> scrub gets, and the storage document says so.

The `GET` never deletes and the `POST` never asks: the confirmation is a page, the delete is the
button on it. A `POST` to an id that is not the owner's is the `404` page and changes nothing; a
`POST` that succeeds answers `303`. The store call is the same `ForgetAsync` the `forget_memory` tool
makes, so the page cannot delete something the tool could not: head, history and index row in one
transaction, no tombstone (DEC-204).

### D6 — Markup, styles and the seed

The page adds one section to `Pages/Seeds/assistant.css`, between the conversation list and the chat:

| Class | What it is |
| --- | --- |
| `.assistant-memory-count` | The count line under the heading |
| `.assistant-memories` | The list of groups |
| `.assistant-memories > section` | One kind's heading and its rows |
| `.assistant-memory` | One row: the content, its meta line, the form |
| `.assistant-memory-meta` | Written, used, from, and the Pinned badge |
| `.assistant-memory-pinned` | The badge itself |
| `.assistant-memory-error` | The error line a refused save leaves above the row |
| `.assistant-memory textarea` | A short box, the width of the row |
| `.assistant-memory-forget` | The forget link, at the end of the row |

No rule is load-bearing: the form is `form.form` and the tables and lists are the shell's, so the page
is usable with a stylesheet that predates it. That matters because `PageAssetSeeder` seeds a page only
when its head is missing, so the new sheet reaches a fresh install automatically and an existing site
only when the owner restores `/assistant.css` from the page list, which the docs say.

The page loads no script. `assistant.js` stays what the conversation needs and nothing else.

### D7 — The assistant's side: the shell record, the nav and the prompt

`/assistant` is a reserved prefix, so `/assistant/memory` is already a key no page can claim,
`list_pages` already shows it as a shell file and `read_page` already answers with the `/assistant`
record. That record's description gains the page: "The page assistant: conversations, transcript,
run stream, tool approvals and the owner's memory list." The reserved keys themselves do not change,
so `Pages:ReservedPaths`, `ReservedPaths.Default` and the test that pins the records to them are all
untouched, and the assistant still cannot save the route (DEC-198).

The assistant navigation on `/assistant` and on a conversation gains a Memory link, so the page is
one click from where a memory is written. Nothing is added to the site menu: the page is the
assistant's, not the site's.

The prompt gains one sentence and `Version` becomes `page-author-v14`. The Memory section currently
ends "Only the owner pins a memory or edits it on their own page, so do not offer to." What it lacks is
that the page exists and what it does: "The owner sees every memory on `/assistant/memory` and can
correct, pin or forget one there; a memory you are unsure of is one they can fix without asking you."
A run records the prompt it used, and a prompt whose memory rules changed is a different prompt.

### D8 — Failure behaviour

- **The store throws on the list.** The page answers the error document (`ErrorResponses` with a
  `500` and the usual remedies) rather than an empty list: an empty list would say the assistant
  knows nothing, which is a lie the owner cannot tell from the truth.
- **The store throws on a save or a forget.** Nothing was written; the same error document answers,
  naming the action that failed.
- **The row was deleted by the assistant while the page was open.** The save answers `404` and the
  page says so; the forget has the same answer. Both are ordinary races, not faults.
- **The row was edited while the page was open.** `409`, with the stored content and the sentence
  that says the assistant changed it.
- **The assistant is switched off between two requests.** The route answers the disabled page, like
  every other assistant route.

Nothing here retries and nothing queues: the page is a request, and the owner can try again.

### D9 — Privacy, deletion and what the page promises

The page shows one owner's memories to that owner and nobody else, because every store call filters
on the account the request is signed in as. The content is never rendered as Markdown or HTML, never
put in a log line and never a metric tag; the access log records the request, as it records every
request. A forget is a delete, with the caveat D5 states, and the account sweep still runs from
`users remove`. The page adds one instrument's worth of nothing: the existing counters describe the
assistant's tool calls, and an owner editing a form is an ordinary request, so the page gets no
metric of its own rather than a second meaning for `assistant.memory.writes`.

## Considered and rejected

- **A JSON endpoint and a script.** The conversation page needs one because it streams; a list and a
  form do not. `/status` and the editor are server-rendered for the same reason, and DEC-81 keeps the
  shell free of JavaScript where markup will do.
- **Editing a memory in the transcript.** The turn that wrote it is a tool call and its result, and a
  form there would be a second write path inside a page that reloads on every terminal event. The
  page is the one place an edit lives.
- **Composing the save from the tool's `UpdateAsync`.** It cannot set `Pinned`, which is deliberate,
  and giving it a flag would give the tool family a way to pin. A separate `EditAsync` keeps the
  assistant's path unable to pin by construction.
- **A pin toggle of its own, as a second form per row.** Two forms per memory can disagree about the
  version, and the owner would have to save twice to change a sentence and pin it. One form with one
  version is one write.
- **Merging a stale save.** A three-way merge of prose is a guess, and the memory may have been
  rewritten by the owner in another tab as easily as by the assistant. Refusing with the stored text
  shows the owner what they are overwriting.
- **A page-local delete with no confirmation.** `forget_memory` is immediate because a tool call is
  reviewed in the transcript before its result; a link a thumb can hit is not.
- **Showing the memory block the run was given.** It is a selection within a budget, and it changes
  every run. The page shows the facts, not the selection.
- **Paging the list.** The cap is 200 rows and the page is one owner's. Paging would be dead code
  until the cap changes, and the cap changing is its own decision.
- **A page under `/edit`.** The editor is the page store. Memory is the assistant's, and its routes
  belong where its owner filter and its stylesheet already are.

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| `MemoryListing` | The order the page draws: kinds in order, pinned first, newest change first | `Core.Assistant` |
| `MemoryGroup` | One kind's heading and its memories | `Core.Assistant` |
| `MemoryCandidate.SourceConversationId` | Where a memory came from, so a row can link to it | `Core.Assistant` |
| `IMemoryStore.ListAsync` | The owner's memories, for the page | `Web.Assistant` |
| `IMemoryStore.EditAsync` | The owner's save: validation, history, version and the pin | `Web.Assistant` |
| `AssistantMemoryEndpoint` | The four routes and the list, the row and the confirmation | `Web.Assistant` |
| The `/assistant.css` section | The row, its meta line and the error line | `Web.Pages.Seeds` |

## Tests

The order is the repository's: the pure rule first, then the store, then the composed host.

**`Core.Tests`**

- `MemoryListingTests` — four kinds in the store's order; an empty kind is left out; pinned first
  inside a group; newest `Written` first after the pinned ones; the id breaks a tie; an empty list is
  no groups; a group with one memory.

**`Web.Tests`**

- `AssistantMemoryStoreTests`, added to — `ListAsync` returns only the owner's rows and orders them
  newest first; `EditAsync` changes content, kind and pinned and bumps the version; it appends the
  previous version to `AssistantMemoryEdits` and keeps the head's provenance; a stale version is
  refused with the current content and changes nothing; another owner's id is refused the same way a
  missing one is; a secret and an over-long content are refused with the same sentences the tool
  gets; `UpdateAsync` still cannot change `Pinned`.
- `AssistantMemoryPageTests` — the view over a hand-built `HttpContext` and a `DatabaseTemplate`
  database: the list groups by kind and shows the count against the cap; the content is encoded, so a
  `</textarea><script>` memory is characters; a null provenance reads "from a deleted conversation"
  and a resolvable one links to `/assistant/{id}`; "never used" and "used 4 times, last on …"; a save
  saves and answers `303`; a stale save answers `409` with the stored content; a secret answers `400`
  with the rule's sentence and keeps the submitted text; a wrong-owner id is `404` with nothing
  changed; a `POST` the antiforgery check refused is `400`; the forget `GET` shows the content and
  deletes nothing, the `POST` deletes the row, its history and its index entry and answers `303`; an
  empty list says so rather than drawing four headings.
- `ShellViews` gains `assistant-memory` and `assistant-memory-forget`, so `AccessibilityTests` applies
  its twelve rules to both: one `main`, one `h1`, no skipped heading level, a label on every control,
  every `form.form` field in a `dd` or a fieldset legend, and the rest.

**`Web.IntegrationTests`**

- `AssistantMemoryPageTests` — with two accounts against the real host: the owner's page lists the
  memory the assistant wrote, and another account's request for the same id is `404`; a save from the
  page is in the next run's system prompt (the scripted client captures the request), and so is a
  memory pinned from the page that the turn does not mention; a forget from the page removes it from
  the next request; the page shows the count at `MaxMemoriesPerOwner`, which the fixture reaches by
  writing the rows directly; with the assistant disabled the memory route answers the disabled page.

**`Web.BrowserTests`**

- `AssistantMemoryBrowserTests` — sign in, reach `/assistant/memory` from the conversation's
  navigation, edit a memory's content and pin it, reload and see both, then forget it through the
  confirmation page, reload and see the list without it.

**The gate.** `make check` (format, licence, audit, build, test, publish) and a relative-link and
anchor check over `docs/` after the documentation changes. The browser suite is `make deploy`'s, as
always (DEC-179).

## Phases

Each phase ends with a green `make check` and its tests written first.

### Phase 0 — the rule and the store (half a day)

- Record DEC-200–DEC-204.
- `MemoryCandidate.SourceConversationId`, `MemoryListing` and `MemoryGroup`, with the tests failing.
- `ListAsync` and `EditAsync` in `IMemoryStore` and `SqliteMemoryStore`, and the source conversation
  in every projection that builds a candidate.
- `AssistantMemoryStoreTests`' additions.

**Done when** the store can list and edit what the page needs, the assistant's `UpdateAsync` still
cannot pin, and the grouping is pinned by tests.

### Phase 1 — the list (half a day)

- `AssistantMemoryEndpoint` with `GET /assistant/memory`, the route registration and the nav link.
- The view: groups, rows, the count, provenance, use, the encoded content.
- `AssistantMemoryPageTests` for the read and `MemoryListingTests`.

**Done when** the owner sees every memory grouped by kind with the count, another owner's id is not
found, and a memory full of markup is text.

### Phase 2 — the save and the forget (one day)

- `POST /assistant/memory/{id}` with the version, the kind and the pin, and the re-rendering refusals.
- `GET` and `POST /assistant/memory/{id}/forget`, with the confirmation and the erasure sentence.
- The endpoint tests for every refusal.

**Done when** a save from the page changes the memory the next run recalls, a stale save is refused
with the stored content, and a forget removes head, history and index entry.

### Phase 3 — the styles, the record and the browser (half a day)

- The `assistant.css` section and the shell record's sentence.
- The prompt sentence and `page-author-v14`.
- The `ShellViews` fixture, `AccessibilityTests` coverage and `AssistantMemoryBrowserTests`.

**Done when** the accessibility sweep covers the page, the shell record names it, and a browser round
trip edits, pins and forgets a memory.

### Phase 4 — documents and the closed job (half a day)

- [assistant](../assistant.md): the page replaces "until the page lands".
- [storage](../storage.md): the owner's write and the pin.
- [testing](../testing.md): the suites.
- [glossary](../glossary.md): **Memory page** and **Pin**.
- Delete `docs/jobs/IDEA-023.md` and add its row to [the closed jobs](../jobs/README.md#closed).
- The plan's status row moved to landed; link and anchor check.

**Done when** the docs describe what the code does, IDEA-023 is closed with its outcome, and
`make check` passes.

## Documentation and decisions

- **DEC-200** — The memory page is shell code under `/assistant/memory`, behind the `Editor` policy
  and the owner filter, server-rendered with no JavaScript, and one document per response: the list,
  a row's form, a forget's confirmation. A memory is shown as encoded text and never rendered as
  Markdown or HTML, because it is data the prompt already declares to be data.
- **DEC-201** — Pinning is the owner's, and only the owner's: `EditAsync` is the one write that can
  change `Pinned`, it is reached from the page and never from a tool, and a pinned memory is listed
  first inside its kind and honoured first by recall.
- **DEC-202** — A save carries the version the row was rendered from and a stale one is refused with
  the stored content rather than merged, the same rule `edit_page` and `update_memory` already use.
- **DEC-203** — A refused save re-renders the row with the submitted text and an error line above it,
  because the textarea is the only copy of what the owner typed; the answer is a `400` or a `409`
  carrying the page, not a bare error page.
- **DEC-204** — Forgetting from the page is the same delete `forget_memory` makes — head, history and
  index row in one transaction, no tombstone — and it asks on a page of its own first, which also
  says what the delete cannot reach.

Docs changes: [assistant](../assistant.md) gains the page where it now says the transcript is the only
view; [storage](../storage.md) gains the owner's write beside the assistant's; [testing](../testing.md)
names the three suites; [glossary](../glossary.md) defines **Memory page** and **Pin**; IDEA-023 moves
to [closed](../jobs/README.md#closed); and the plan's status row says landed.

## Risks

| Risk | Mitigation |
| --- | --- |
| **The page becomes the injection foothold.** The owner or a pasted page talks the assistant into a memory, and the page makes it look authoritative. | The page shows exactly what the run will be given, as data; editing it is the owner's act; the prompt still declares a memory data rather than instructions (DEC-188). The list is the review surface the memory plan said was missing. |
| **A pin floods the block.** Every pinned memory is selected before anything else. | Pinned memories are still bounded by `MaxMemoriesPerRun` and `MaxMemoryCharsPerRun`, a pinned memory that does not fit is reported as left out, and the page shows the pin beside the content that earns it. |
| **A stale form overwrites an assistant edit.** Two tabs, or a run that updated the row. | `expectedVersion` on every form, one transaction, a `409` carrying the stored content. |
| **A failure loses the owner's text.** | Every refusal re-renders the row with the submitted values and the error line (DEC-203), and `maxlength` makes the cap the browser's first refusal. |
| **The new styles never reach an existing site.** Seeding only fills a missing head. | The page is usable with `layout.css`'s `form.form` alone, and the docs say to restore `/assistant.css` from the page list after this lands. |
| **A second write path to memory drifts from the tools'.** | One store operation, `EditAsync`, with the same normalisation, the same cap, the same secret rules, the same history and the same version check; the endpoint validates form shape only. |
| **The page quietly grows into memory authoring.** A "new memory" box is the obvious next ask. | It is a non-goal with a reason: a memory should be attributable to a turn. Adding one is a decision, not a form. |
| **The list gets long and slow.** | The cap is 200 rows and the read is one indexed statement plus a sort in C#; the count shows how full it is, and paging is its own decision. |

## Delivered when

- `/assistant/memory` lists every memory the signed-in owner holds, grouped by kind with pinned first,
  with the count against `Assistant:MaxMemoriesPerOwner`, and nothing of another owner's.
- A row's form saves content, kind and pinned, bumps the version, keeps the previous version as
  history, and answers `303`; a stale version is refused with the stored content, and a refused save
  keeps the text the owner typed.
- A pin set on the page is honoured by the next run's recall; a memory forgotten on the page is gone
  from the next run and from the index, its history with it.
- The forget page asks before it deletes and says what the delete does not reach.
- The assistant navigation reaches the page, the `/assistant` shell record names it, and the assistant
  still cannot save the route.
- `ShellViews` and `AccessibilityTests` cover the page, and the browser suite walks the round trip.
- `docs/assistant.md`, `docs/storage.md`, `docs/testing.md` and `docs/glossary.md` describe what the
  code does; DEC-200–DEC-204 are in `docs/decisions.md`; IDEA-023 is closed; the plan's status row says
  landed.
- `make check` passes.
