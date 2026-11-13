<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Revision restore

| | |
| --- | --- |
| Status | Implemented on this branch; the decisions are numbered when it merges |
| Size | Large — two editor actions and their confirmation views, a command rename, three assistant tools, a paging pair on both listings, a prompt version, and the tests and documents for each |
| Area | Web (editor, assistant), seeds |
| Backlog | ARCH-07, closed by this plan |
| Decisions | Referred to in prose here and numbered when the work merges (DEC-09) |

## The short version

A page's content is code, and its history is already stored, listed and readable. What is missing is
the act: recovering from a mistaken save still means copying a revision's source out of one view and
pasting it into another. This plan finishes ARCH-07 in both directions.

- **The editor restores.** `GET /edit/{path}?cmd=restore&hash=…` shows the revision's metadata and
  the diff from the page's current source to that revision; `POST` to the same URL saves that source
  as a **new** revision under the running build and lands on the editor with the restore's
  diagnostics. Nothing is rewound, and a tombstoned path comes back with its history intact. A
  restore that would leave the page's source exactly as it is — the current revision, or any older
  revision carrying the same text — is refused: history already records that source, and one more
  row to page past is noise.
- **A seed is installed, not restored.** The seed action moves from `cmd=restore` to `cmd=install`,
  because two different things can no longer share one word: the action becomes *Install from seed*
  and gets the same diff-and-confirm page the revision restore has, with the same refusal for the
  case that would add nothing.
- **The assistant gains history.** Two reads, `list_revisions` and `read_revision`, let the model
  say what a page used to hold and quote a revision accurately. A third tool, `restore_revision`,
  puts one back under approval — because `write_page` already restores, by retyping a source it read
  and risking a different page. It cannot scrub, and it refuses a restore that would change nothing,
  exactly as the editor does.
- **Both listings page.** `list_revisions` takes `limit` and `before`; `list_pages` takes the same
  `limit` and `after`. The old 500-row constant and its silent cap go.

The visible change is small — a link, a confirmation page, three tools — and the plan is large
because of what it must not disturb: the one saver, the revision-identity rules, the line-number
contract the assistant already promises, and the vocabulary the author manual teaches.

## Goals

1. A revision is recoverable from the editor in two clicks, with the change it will make shown
   before it is made, and with the compiler's answer afterwards.
2. Restoring is an ordinary save of old source: one writer, one revision shape, the current runtime,
   history preserved, a tombstone cleared.
3. The two "put it back" actions are distinguishable by name in the URL, the action, the flash and
   the manual: a seed is **installed**, a revision is **restored**.
4. The assistant can read the history it can already change and put a revision back without
   retyping it, under the same gate `read_page`, `diff_page` and `write_page` repeat, and with no new
   store query.
5. Neither surface can perform the no-op: a restore that would leave the source unchanged is
   refused, and a matching seed cannot be installed, so both actions always change what is live.
6. A long history and a long page list are reachable a bounded slice at a time, by one vocabulary the
   model learns once.
7. ARCH-07 closes: the job file goes, and the documents that promised or cited it say what is true.

## Non-goals

- **An assistant that scrubs or rewinds.** A restore is a write the assistant already has by another
  name (D10), so it gets the faithful form of it; destroying history stays the owner's, and no tool
  can move a head to anything but source the store already holds.
- **A "reason" on a revision.** History stays an audit trail of saves, not a journal of intentions.
  Adding an annotation column would change the revision row for one view's benefit.
- **A restore from the page list, or an undo button on the editor.** Both are plausible and neither
  is needed once history can restore: the history view is where a person already compares revisions.
- **Reading a revision through `?v=`.** Since the immutable-assets work that URL addresses any
  retained revision of a path — one with code, and a tombstoned one — but it answers with what the
  revision *renders*: it executes the code, wraps it, and sets the cache headers the revision's
  verdict earns (DEC-233, DEC-238, DEC-242). A tool that wants stored source to quote, range and
  budget reads the store.
- **A second store read.** All three tools use `PageEditor`'s existing `ListRevisionsAsync`,
  `GetRevisionTemplateByHashAsync` and `SaveAsync`; ARCH-16 is not reopened.
- **Editing history UI into a page.** `/edit` is bootstrap and cannot be a page (DEC-210). The
  history, revision and confirmation views stay in the shell.
- **Changing what a revision hash is.** Identity stays source, creation runtime, save time and save
  id; a restore appends a revision with a new hash because it is a new save, even when the source is
  byte-identical to an *older* revision already stored.

## What exists today

| Piece | Today | Used here for |
| --- | --- | --- |
| `PageRevisions`, `RevisionHash`, `PageTemplate.NewRevision` | Every save appends a row with a unique hash; a head points at the current revision and saving upserts `IsDeleted = 0` | Restore is a save, so the append and the tombstone clearing already exist |
| `PageSaver.SaveAsync` / `PageEditor.SaveAsync` | Compile-then-store, one writer, current runtime, diagnostics returned | The restore's only write path |
| `EditorEndpoint` `cmd=history`, `cmd=revision`, `cmd=diff` | Lists revisions without source, shows one revision's source, diffs two, all `no-store` | The column, the hash rule and the diff style the new views copy |
| `EditorEndpoint` `cmd=delete`, `cmd=scrub` | GET renders a confirmation page with antiforgery and a Cancel; POST acts and `303`s | The confirmation shape, reused rather than invented |
| `EditorEndpoint` `cmd=restore` | The **seed** restore: posts straight from the page list, saves the bundled source, flashes and returns to `/edit` | Becomes `cmd=install`, with a confirmation in front of it |
| `DiffRenderer.ToHtml(before, after)` | Escaped, scoped `table.diff`; an empty side is all additions; an unchanged pair writes "No changes." | The confirmation's diff, in both actions |
| `PageUrls.EditorCommand` / `EditorRevision` / `EditorDiff` | Every editor URL built in one place, encoded once by the HTML layer | Two new members |
| `ToolRegistry`, `AssistantTool`, `ToolPaths`, `ToolResults` | One type per tool, one list, one gate, one bounded envelope (DEC-231) | Three new rows, two of them through the read gate and one through the write gate |
| `PageEditor.ListRevisionsAsync` / `GetRevisionTemplateByHashAsync` | A page's history without source, each row carrying `IsCurrent`; one revision's source by hash | The two assistant reads, the confirmation, and the listing's cheap hint that the current row needs no link |
| `PageEditor.SaveAsync` | The editor's own save, compile-then-store through the one saver | The restore's write, and the assistant's `restore_revision` |
| `RevisionHash.IsCanonical` | The one spelling of a hash: exactly 64 lowercase hexadecimal characters, matched ordinal, which `?v=` already enforces (DEC-241) | The hash rule for the restore, both tools' arguments and the existing editor and `diff_page` checks |
| `PageTemplate.TemplateHasCode`, `PageImageState.StoredAsSource` | A revision records whether it had code; one that did not is stored as source, never compiled, and its state reads *Stored as source* (DEC-235) | A restore stores that verdict with the new row, and the history and the tool answers name the state |
| `?v=`, `PageAssetPolicy`, `PageAssetMap` | One retained revision of a path, rendered and cached immutably (DEC-233, DEC-236, DEC-242) | Deliberately unused: it renders a revision rather than reading its stored source |
| `PageToolData.TemplateSourceLines` and `read_page`'s range rule | 1-based physical lines, a range check, a budget window that keeps the head and the tail (DEC-112) | The same rule for `read_revision`, from one function |
| `PageToolExecutor.Definitions`, `ChangesPages` | Derived from `ToolRegistry` and `ToolSpec.Writes` | Nothing to change but the count |

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | `install` replaces the seed's `restore`; `restore&hash=` is the revision; two confirmation views; the restore POST; a restore column in the history table; the seed action becomes a link; the deleted row loses Recreate; the tombstone note is reworded; `hash`, `from` and `to` validate with `RevisionHash.IsCanonical` |
| `src/uk.osric.asp.Web/Http/PageUrls.cs` | `EditorInstall(path)`, `EditorRestore(path, hash)` |
| `src/uk.osric.asp.Web/Assistant/Tools/HistoryTools.cs` | New: `ListRevisionsTool`, `ReadRevisionTool`, `RestoreRevisionTool` |
| `src/uk.osric.asp.Web/Assistant/Tools/PageTools.cs` | `list_pages` gains `limit` and `after`; `read_page` uses the extracted line projection |
| `src/uk.osric.asp.Web/Assistant/Tools/DiffTool.cs` | Its hash check becomes `RevisionHash.IsCanonical`, so the tools and the editor refuse a wrongly-cased hash the same way |
| `src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs` | Three rows, immediately after `diff_page` |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | `page-author-v22`, a `## History` section, one sentence on listing paging |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The editor's actions, and the recovery section under "Assets, seeds and recovery" |
| `src/uk.osric.asp.Web/Pages/Seeds/editor.css` | The `.page-actions form` rule goes with the form it styled |
| `test/uk.osric.asp.Web.Tests/EditorEndpointTests.cs` | The renamed command, both confirmations, the restore POST, and the history column |
| `test/uk.osric.asp.Web.Tests/PageToolExecutorHistoryTests.cs` | New: all three tools and the paging pair, in the shape `PageToolExecutorDiffTests` set |
| `test/uk.osric.asp.Web.Tests/PageToolExecutorTests.cs` | `list_pages` paging; `read_page` still truncates through the shared projection |
| `test/uk.osric.asp.Web.Tests/ToolSpecsTests.cs` | Eighteen tools; the `ChangesPages` cases, with `restore_revision` among the writers |
| `test/uk.osric.asp.Web.Tests/PageAuthorPromptTests.cs` | `v22` in both version pins, and the section |
| `test/uk.osric.asp.Web.IntegrationTests/EditorEndpointTests.cs` | The restore round trip and the install confirmation against the real host |
| `test/uk.osric.asp.Web.BrowserTests/EditorFlowTests.cs` | The seed test clicks through the confirmation; the deleted-key walk goes through the create-or-open form; a restore walk |
| `docs/assistant.md`, `docs/testing.md`, `docs/shell.md`, `docs/storage.md`, `docs/glossary.md` | Eighteen tools; what the tests prove; install and restore in the editor's story; the vocabulary |
| `docs/jobs/README.md` | The job leaves the table and the rough order; a Closed row points here |
| `deploy/operations.md`, `docs/plans/immutable-page-assets.md` | The two citations of the job, now pointing at this plan |

## Design

### D1 — Two verbs, one word each

`cmd=install` saves a bundled seed; `cmd=restore&hash=` saves a retained revision. The seed action
had `restore` because it was the only thing that put anything back; now that the author's own history
is what most needs restoring, the word belongs to it. The alternatives are both worse: leaving the
seed on `restore` makes the revision action `restore-revision`, which reads like a command line
flag and is the action a person will reach for first, or sharing `restore` and dispatching on the
presence of a hash, which makes one command mean two things and puts the difference in a query
parameter nobody reads.

The rename is copy as well as code, and the copy follows the command or the two actions are
confused again: the page list's action, the confirmation's heading and button, the flash line and
`/help` all say **install**. A stale POST to the old URL is not silently accepted: a `restore`
without a hash answers `400` with the history and the editor as remedies, which is what a
half-understood bookmark deserves.

### D2 — A confirmation is a GET that shows the diff, and only the POST writes

Both actions get the same shape, which is the shape `delete` and `scrub` already use: a `GET` renders
a page with a Cancel, and with a form carrying the antiforgery field when there is something to do; a
`POST` to the same URL acts and answers `303`.

| Action | Confirmation | Posts to |
| --- | --- | --- |
| Restore a revision | `GET /edit/{path}?cmd=restore&hash={hash}` | the same URL |
| Install a seed | `GET /edit/{path}?cmd=install` | the same URL |

The page carries `DiffRenderer.ToHtml(before, after)`, where `after` is the source that will be
stored and `before` is the page's **current** source — read with `PageEditor.GetAsync`, which answers
nothing for a tombstone, so a restore or install on a deleted path reads as pure addition. That is
honest: nothing is live, so nothing is being changed, only brought back.

Not compiling is part of the promise: both views read source and metadata only, so a GET neither
compiles nor writes, and both answer `no-store` because they show page source to one owner.

The confirmation says, in this order: what will be saved (hash and save time for a revision; "the
bundled seed" for an install); the diff; and one sentence of consequence — "This page is deleted.
Restoring brings it back and clears the tombstone."

**An empty diff ends the page.** When the source that would be stored is the source the live page
already holds, the confirmation renders the diff's own "No changes." and a sentence saying there is
nothing to restore, and **no form** — the same shape for a seed that already matches and for a
revision whose text the page already holds. Nothing is refused with an error to a person who
followed a link; the write is refused if it is posted anyway (D3, D4).

An install needs a seed rather than a history, so it is the one confirmation that works on a path
with no head at all — a seed whose page was scrubbed can be brought back from a hand-typed URL,
though the listing cannot reach it. A restore needs a revision, so a path with no head has nothing to
offer and answers `404`.

### D3 — A restore is a save of old source, and it refuses one that would change nothing

The write is `PageEditor.SaveAsync(path, revision.Template)` and nothing else. That gives the change
everything the plan needs without a new store method: the source is compiled by `PageSaver` under
`PageSaver.RuntimeId` (so a revision written by an earlier build is compiled by *this* one), the
result is stored even when it does not compile, the diagnostics come back to the editor, the
revision is a new row with a new hash, and a tombstone is cleared by the head upsert that already
runs on every save. A revision with no code is not compiled at all: the save judges it and stores the
source with `TemplateHasCode = false` (DEC-235), so a restored stylesheet or script is served from
source, costs no compile, and reads as *Stored as source* in the history.

**A restore is refused when the chosen revision's source is what the live page already holds.** A
restore of the current revision, or of any older revision carrying the same text, would append a row
and change nothing else: history already records that source, and the only effect would be one more
entry to page past. So the chosen revision has to differ from the page as it stands.

The comparison is one line — `live.Template` against `revision.Template`, ordinal — and it is the
same line an install makes, which is the point: both actions refuse the change that would leave the
live source exactly as it was. A tombstoned path has no live source (`GetAsync` answers nothing), so
nothing is compared and every row of its history is restorable, including the revision it was
deleted at — the "bring it back" case, and so is an install of a deleted seed. A revision whose
source is the empty string is not special: on a live page with an empty source it is refused like any
other match, and on a tombstoned path nothing is compared at all.

`PageRevisionSummary.IsCurrent` is still used, but only as the listing's cheap hint: the history row
for the current revision carries no restore link (D7), because the listing deliberately does not
load revision source and cannot know that an *older* row holds the same text. The confirmation is
the authority, and it has both sources in hand for the diff anyway; the POST re-reads them and
refuses if they now match, because the page can move between the two requests.

Otherwise a restore always appends — including a source that matches an older retained revision,
because A, B, A is three saves and the third is one. There is no conditional save: a restore is not
racing the author's own edit, it is the author's deliberate act on the page, so an intervening save
is exactly what it means to overwrite. The editor's conditional save stays where it is, with
`edit_page` (DEC-192).

### D4 — The seed install is the same rule, and the same shape

A seed is a fixed artefact, so "the page already holds this source" is the whole of what an install
could do. That is D3's comparison, and `MatchesSeedAsync` already makes it: the two actions differ
only in where the source comes from, not in when they are allowed.

When the seed already matches, the listing does not render the action, the confirmation says so and
offers only Cancel, and a POST that arrives anyway answers the same `409` as a restore. That replaces
today's flash-and-redirect: the flash existed because the POST *was* the action, and a refused write
is better described by the confirmation page a person can be sent back to.

Because the action now opens a page rather than posting, it is an `<a>` in the listing — a form and a
submit button were only ever a way to reach the write — and a link that cannot act is worse than an
absent one, so a matching seed renders no action at all rather than a disabled one.

### D5 — The two actions share one shape, and one message

They are the same operation with a different dictionary, and the plan makes that explicit so they
cannot drift:

| Aspect | Both actions |
| --- | --- |
| Confirmation | a `GET` with the diff, the antiforgery field and a Cancel; the `POST` acts (D2) |
| Write | `PageEditor.SaveAsync` under the running runtime, appending a revision (D3) |
| No-op | a confirmation that would change nothing renders no form, and a POST that arrives anyway is refused with `409` and remedies (D3, D4) |
| Message | `Installed {key} from its seed as revision {short}.` / `Restored revision {short} of {key} as revision {new short}.` |
| Outcome | the same classification save uses: nothing extra when clean, otherwise `; it compiled with warnings.` or `; it does not compile, so the page will not render until you fix it.` |
| Log | Information, naming the path, the new revision's hash and, for a restore, the source hash |
| Cache | `no-store` |
| Landing | where the outcome is legible (D6) |

The message is a behaviour change for install, not a rename: today it discards the diagnostics and
says only "Restored {key} from its seed." Installing a seed after an Sdk change can compile with
errors, and the author is told it worked. The classification `SaveAsync` already performs moves into
one helper beside the editor's `SaveOutcome`, and save, save-and-view, install and restore all use
it, so four verbs cannot report the same compiler answer four ways.

### D6 — Both land where the outcome is legible

A restore lands on the editor: it is the only view that shows the restored source *and* the
diagnostics the restore's compile produced, which is the same reasoning that keeps "Save and view" on
the editor when a save has errors. An install lands on `/edit`: the listing's State column carries
the installed page's result and the flash names the revision, and the listing is where the author
started. Each returns to the surface that can show what happened, which is also the surface the
action was started from.

### D7 — The listing loses Recreate, history gains a column, and a tombstone says what it means

The history table gains `<th scope="col">Action</th>` and every row **except the current one** a link,
`/edit/{path}?cmd=restore&hash={hash}`, labelled for a screen reader with the short hash and the key.
The compare form, the Current mark, the compilation state and the creation runtime stay as they are.
A tombstoned path lists the same way and every row is restorable, because none of them is current.

The omission is a hint, not the rule. The listing deliberately does not load revision source, so it
cannot know that an *older* row holds the same text as the page; that row keeps its link and its
confirmation says there is nothing to restore (D3). The current row is simply the case that costs
nothing to recognise and is commonest.

The deleted page's **Recreate** action goes with it. Saving a new page at a key that was used before
is not a special operation: "Create or open" on `/edit` names any key, tombstoned or not, and the
editor it opens is the one that will save the page — so *Recreate* was a second, one-click name for
the ordinary act, and it read like "bring back the old one" once a real restore existed beside it.
A deleted row keeps History, Scrub history and, for a seeded page, Install from seed.

What an author does need told is that the key is not new, and the editor is where they can act on it.
It already says a page was deleted; the note now names the key and the consequence: "{key} was
deleted. Its history is retained, and saving here brings the page back and continues that history.
Until you save, links to {key} answer "not found"." That is the notification for a key with a past —
about old links and what saving will do about them — and it is why nothing in the listing has to
shout about tombstones.

### D8 — `list_revisions` answers the history the editor answers

| | |
| --- | --- |
| Name | `list_revisions` |
| Arguments | `path` (required), `limit`, `before` |
| Gate | `ToolPaths.ResolveRead`, then `RequirePageSource` — a shell file has no revisions |
| Approval | none; `Writes = false`; verb `List` |

The answer is newest first, one row per revision: `hash`, `savedAt`, `creationRuntimeId`,
`imageState` (the same `Describe()` the editor shows), `isCurrent`, and `editorUrl`
(`PageUrls.EditorRevision`). The envelope adds `path`, `deleted` and `truncated`.

It lists a tombstoned path, because that is the answer a caller most often needs, and it refuses a
path with no head at all — "Page {path} was not found." — rather than answering an empty list, which
would be indistinguishable from a page that has never been saved. That is the editor's own rule:
`cmd=history` answers `404` for a missing head.

`limit` defaults to 50 and is refused outside 1–500 (D12). `before` is a 64-hex hash and starts the
answer at the next revision older than it; a malformed hash, or one that is not a revision of that
path, is refused rather than ignored, because a silent restart would make a paging loop look like a
short history.

### D9 — `read_revision` reads one revision's source

| | |
| --- | --- |
| Name | `read_revision` |
| Arguments | `path` (required), `hash` (required), `startLine`, `endLine` |
| Gate | the same two checks as `list_revisions` |
| Approval | none; `Writes = false`; verb `Read` |

The answer is the stored source with the 1-based physical line numbering, the line-range rule and the
budget window `read_page` uses (DEC-112), plus `hash`, `savedAt`, `creationRuntimeId`, the revision's
compile state, `totalLines`, `startLine`, `endLine`, `truncated` and `editorUrl`.

It does not answer `isCurrent`. Whether a revision is the head is `list_revisions`' question, and a
tombstoned path has no current revision — the same flag would be false for every row of its history
and would invite the wrong conclusion.

A malformed hash, a hash belonging to another path, a missing page and a shell file are refusals
whose words name the rule: "A revision hash is 64 lowercase hexadecimal characters; use the hash
list_revisions returned." and "That hash is not a revision of {path}." The spelling is
`RevisionHash.IsCanonical` (DEC-241) — the rule `?v=` already applies on the page endpoint — and the
editor's revision, diff and restore links move to it in this change, so a malformed or wrongly-cased
hash is a named `400` rather than the `404` an uppercase hash used to become. A hash that is
canonical but belongs to another path is still a `404`.

The description says what the call costs: reading a revision sends its source to the provider,
exactly as `read_page` does.

### D10 — `restore_revision` is the assistant's one write to history

| | |
| --- | --- |
| Name | `restore_revision` |
| Arguments | `path` (required), `hash` (required) |
| Gate | `ToolPaths.ResolveWrite` alone: the policy already refuses a reserved or protected key for a write, so a shell file and a locked page need no second check |
| Approval | yes; `Writes = true`; path policy refuses before the prompt; verb `Restore` |

The assistant already restores, badly. `write_page` replaces a page with any source, so a model that
wants a revision back can read it with `read_revision` and hand the whole thing back as `content` —
a lossy round trip through a tool argument, bounded by `MaxToolArgumentChars`, where every escape,
line break and truncation is an opportunity to restore something that is not what was stored.
Refusing the hash-shaped version of the same act removes the faithful one and keeps the unfaithful
one. So the tool exists, and it is the most constrained write the assistant has: the source must
already be a revision of that path, so the model cannot invent content with it.

It applies the editor's rule exactly (D3): a hash whose source the live page already holds is
refused, as are a hash that is not canonical (`RevisionHash.IsCanonical`), a hash from another path,
a missing page and a shell file, and everything else appends. It takes no `expectedRevisionHash`,
because a restore is deliberate — the same reason `write_page` takes none while `edit_page` does. It
answers `{ path, restoredFrom, revisionHash, diagnostics }`, forms the diagnostics with the same
shape the other writing tools use, and is approval-gated under `RequireApproval`.

This reverses the rule the assistant plan and the job recorded — "the editor's restore stays the
owner's" — and the plan says so rather than quietly widening the model's reach: the reason that rule
gave was the model's inability to say what a page used to hold, and the two reads remove it. A scrub
still has no tool and is still the owner's.

### D11 — The line rule has one implementation

`read_page`'s range check and budget window move into `PageToolData` — one function that takes a
source, an optional start and end, and the options, and answers either the projected source and its
line numbers or the refusal sentence. `read_page` and `read_revision` both call it, so the second
tool cannot drift from the first, and a test can prove the same source truncates the same way
through both.

### D12 — Paging is `limit` and a cursor named for its own direction

The two listings run in opposite orders, so one cursor name would have to mean opposite things.
Each tool names its direction:

| Tool | Order | Arguments |
| --- | --- | --- |
| `list_pages` | canonical key, ordinal | `prefix`, `includeDeleted`, `limit`, `after` |
| `list_revisions` | save time, newest first | `path`, `limit`, `before` |

`after` is a key: the answer starts at the first entry whose key sorts after it, compared ordinal,
which is the order the listing is in. `before` is a revision hash: the answer starts at the first
revision older than it. Both are exclusive — the cursor entry is not repeated — and both are
documented in the tool's own description, which is where the model reads them.

`limit` defaults to 50 and its ceiling is 500, shared by the two tools as constants in
`PageToolData` so they cannot drift. `truncated` stays in both answers and means "there is more":
the model asks again with the last key or hash it was given. The 500-row `ListingLimit` constant in
`ListPagesTool` goes; a caller that asks for more than the result budget allows still meets the
envelope's own refusal, which is a retry with a smaller `limit`, not a silent truncation.

### D13 — The history tools sit together

`ToolRegistry` gains the three rows immediately after `diff_page`, so the four tools that name a
revision — compare, list, read, restore — are adjacent for a model that has just been asked to put
one back. That puts a write between two read families, which is the cost; the alternative, filing
`restore_revision` beside `write_page`, separates it from the hashes it consumes and makes a model
that has just read a revision hunt for the tool that uses it. The order is part of the provider's
cached prefix (DEC-231), so it is a deliberate position rather than an append, and the prompt version
bump in D14 is what makes the change visible.

### D14 — The prompt and the manual learn the same words

`PageAuthorPrompt` moves to `page-author-v22` — v21 is the immutable-assets prompt on trunk — and
gains a `## History` section after `## Diff`: the three tools, what a hash is, that `list_revisions`
is where a hash comes from, that `read_revision` sends the source to the provider, that
`restore_revision` appends and is approval-gated, that a restore which would leave the source
unchanged is refused, and that a revision the owner should choose is quoted by hash rather than
described. One sentence covers `limit` with `before` and `after`.

`/help` is the author's manual, so it changes in the same commit: the editor's list says
**Install from seed** and gains **Restore** beside **History**, and the "Assets, seeds and recovery"
section stops calling a seed restore a restore.

## Considered and rejected

| Option | Why not |
| --- | --- |
| `cmd=restore-revision`, leaving the seed on `restore` | The revision is the thing an author restores; the command should be the plain word, and the seed's action is an install |
| Dispatch on the presence of `hash` under one `restore` | One command with two meanings, and the difference lives in a parameter rather than the URL |
| A restore that overwrites the old row | Revisions are immutable; rewriting one would break every hash a link, an archive or the assistant has already quoted |
| A conditional save, refusing a restore whose page changed | The author is deliberately replacing the head with old source; an intervening save is what that means |
| Restoring source the page already holds | It appends a row and changes nothing else: one more entry for a save history already records (D3) |
| Refusing only the current revision, by identity | An older revision can hold the same text, so the append would still be noise and the identity check would miss it (D3) |
| Keeping the assistant unable to restore | `write_page` already restores, by retyping a source read through a bounded argument; refusing the hash-shaped act keeps the unfaithful form of it (D10) |
| Keeping Recreate on a deleted row | Saving at a used key is the ordinary create-or-open act; a second one-click route to the same editor, labelled as if it brought the old page back, is the thing that needed removing once a real restore existed (D7) |
| A `restore_revision` that takes the source as an argument | That is `write_page` with a hash-shaped name; the point of the tool is that the content cannot be invented (D10) |
| No confirmation, a button on the history row | A save is not destructive but it is a compile and a new head; the editor already confirms delete and scrub, and the diff is what makes the rollback reviewable |
| The confirmation showing the source instead of a diff | The revision view already shows the source; what a person needs before restoring is the change |
| Reading a revision through `?v=` in a tool | It now addresses any retained revision, including one with code and a tombstoned one, but it answers by rendering — executing, wrapping and setting cache headers (DEC-233, DEC-238) — where a tool needs the stored source, and its cache promise is the opposite of what a read of source wants to assert |
| `isCurrent` on `read_revision` | False for every row of a tombstoned path; the listing answers the question properly |
| A second query for the history tools | `PageEditor` already exposes both reads and the save; ARCH-16 stays closed |
| No `limit`, just the envelope refusal | A caller cannot ask for a slice, so a long history is unreadable without a fresh tool call per row |
| One cursor name for both listings | The orders run in opposite directions, so a shared name would have to mean "older" in one tool and "later" in the other; `before` and `after` each say what they do |

## Components

| Component | Where | What it is |
| --- | --- | --- |
| `WriteRestoreConfirmationAsync` | `EditorEndpoint` | The metadata, the diff and the form for `cmd=restore` on a GET |
| `RestoreRevisionAsync` | `EditorEndpoint` | Validates the hash, refuses a restore that would change nothing, saves the source, logs, flashes, redirects |
| `WriteInstallConfirmationAsync` | `EditorEndpoint` | The same page for `cmd=install`, with its no-op case |
| `InstallSeedAsync` | `EditorEndpoint` | Today's `RestoreSeedAsync`, renamed and reworded |
| `InstallActionAsync` | `EditorEndpoint` | The listing's `Install from seed` link, or nothing when the seed already matches |
| `HistoryActionAsync` | `EditorEndpoint` | The restore link per history row |
| `EditorInstall`, `EditorRestore` | `PageUrls` | The two URLs, encoded once |
| `ListRevisionsTool`, `ReadRevisionTool`, `RestoreRevisionTool` | `Assistant/Tools/HistoryTools.cs` | The two reads and the one write, each carrying its own spec and run |
| `PageToolData.ProjectSource` | `Assistant/Tools/PageTools.cs` | The one line-range and budget implementation (D11) |
| `PageToolData.DefaultListingLimit`, `MaxListingLimit` | `Assistant/Tools/PageTools.cs` | The shared 50 and 500 (D12) |

## Tests

**The editor, in `Web.Tests/EditorEndpointTests`** — one behaviour each, over a hand-built
`HttpContext` and the in-memory store the fixture already uses:

- the history table offers a restore link for every revision except the current one, including a
  tombstoned path's, where no row is current because the page is deleted;
- the deleted list offers History, Scrub history and, for a seeded page, Install from seed, and no
  Recreate;
- opening a tombstoned page says the key was deleted, that saving brings it back and continues its
  history, and that links to it answer "not found" until then;
- the restore confirmation shows the revision's metadata and the diff from the current source, writes
  nothing and does not compile, and answers `no-store`;
- the confirmation of a tombstoned path's revision says the page is deleted and reads as a pure
  addition;
- a revision whose source the live page already holds — the head's own, or an older one with the
  same text — renders "No changes" and no form, and its POST answers `409` and writes nothing;
- an older revision carrying the same text as the page keeps its link and its confirmation says there
  is nothing to restore — the current row's omission is a hint, not the rule;
- a malformed hash is `400` with the history as a remedy, and so is an uppercase one, which used to
  be a `404` on `cmd=revision` and `cmd=diff`; a missing one is `400`; a hash from another path is
  `404`; a shell file is `404`;
- the restore POST appends that revision's source, answers `303` to the editor, and flashes the new
  hash;
- restoring a revision that matches an **older** retained revision but not the live page appends, so
  the rule above is the live source and not "have I ever stored this text";
- the appended revision was compiled by the running runtime, not the old one;
- a restore of a no-code revision stores it as source with no image and the history says *Stored as
  source*;
- a restore of a deleted path clears the tombstone and the page renders again;
- a restored source that does not compile is stored, and the message says so;
- an invalid antiforgery token writes nothing (the fixture's existing POST rule, asserted for
  `restore`);
- the seed action is `cmd=install` on the page list, is a link to the confirmation, is not offered
  when the stored source already matches, and `cmd=restore` no longer installs a seed;
- the install confirmation diffs the stored source against the seed and its POST appends the seed and
  returns to `/edit`; its message and log name the new revision and the compile outcome;
- a matching seed's confirmation renders no form, and a POST for it answers `409` and writes nothing;
- a seed install on a tombstoned path says it brings the page back.

**The assistant, in `Web.Tests/PageToolExecutorHistoryTests`** — the shape
`PageToolExecutorDiffTests` set:

- the two reads are offered without approval and `restore_revision` with it, each with its declared
  arguments and required sets;
- `list_revisions` answers newest first, marks the head, and gives an editor URL per row;
- it lists a tombstoned path and says `deleted` is true;
- it refuses a shell file, a missing page, a malformed `before` and a `before` from another path;
- `limit` bounds the rows and `truncated` says there is more; `before` continues without a gap or an
  overlap;
- `read_revision` answers the stored source with 1-based lines, an honest `totalLines`, and a
  requested range;
- a source over the budget is windowed exactly as `read_page` windows it, proven by both tools over
  one source;
- it refuses a foreign hash, a malformed hash, a shell file and a missing page;
- `restore_revision` restores a hash's source as a new revision and answers the new hash and the
  diagnostics;
- it refuses a hash whose source the page already holds, a foreign hash, a non-canonical hash
  (uppercase included), a shell file and a missing page;
- `list_pages` honours `limit` and `after`, and still merges shell files and pages into one ordinal
  listing.

**The table, in `ToolSpecsTests`** — eighteen rows in the registry, and the `ChangesPages` cases say
both reads change nothing and `restore_revision` does.

**The prompt, in `PageAuthorPromptTests`** — `page-author-v22` in both version pins, and a section
test that the three history tools are named, that the reads are reads, that the source leaves for the
provider, and that a restore which would change nothing is refused.

**The host, in `Web.IntegrationTests`** — two saves and a restore through the real editor: the
confirmation's token, the `303`, the page serving the older source and the history listing the new
head as Current; and the install confirmation answering `200` on a GET and installing on its POST.

**The browser** — `ARestoredSeedPageComesBackWithItsBundledSource` walks the confirmation it now has,
`RecreatingADeletedPageBringsItBack` becomes the create-or-open walk — name a deleted key's path in
the form, save, and the page serves again, which is the claim D7 makes — and one restore walk proves
the round trip a person makes. Nothing else belongs here: the unit tests and the integration test
already prove the rules.

## Phases

Each phase leaves `make test` green.

### Phase 0 — the seed becomes an install (half a day)

Rename the command and the copy, add `PageUrls.EditorInstall`, turn the page list's form into a link
to the confirmation, write the confirmation and its diff, and move the existing tests and documents
to the new word. Nothing else changes.

### Phase 1 — restore a revision (one day)

`PageUrls.EditorRestore`, the restore column on the history table, the confirmation, the no-change
refusal on the confirmation and the POST, the POST with its validation, logging, flash and landing,
and the editor tests for both views and the write.

### Phase 2 — the assistant's history (one and a half days)

Extract the line projection (D11), add the three tools and their registry rows, add `limit`/`before`
and `limit`/`after`, the shared limits, and the tool tests. `read_page`'s behaviour must not change,
and its existing tests prove it; `restore_revision` is proven to refuse a no-change restore with the
editor's rule, not a second copy of it.

### Phase 3 — the prompt and the record (half a day)

`page-author-v22` and its section, `PageAuthorPromptTests`, `docs/assistant.md`, `docs/testing.md`,
`docs/shell.md`, `docs/storage.md`, `docs/glossary.md`, `/help`, the jobs README, the deletion of
the job, and the two citations. Then `make format-check`.

## Documentation and decisions

| Document | Change |
| --- | --- |
| `/help` (seeded) | **Install from seed** and **Restore** in the editor's list; the recovery section says which word means which, and that Create or open names a deleted key as readily as a new one |
| `docs/shell.md` | The editor's story: a seed installs from its row through a confirmation, and history restores a revision as a new save |
| `docs/storage.md` | The seed sentence names install, the paragraph that describes a disabled restore button describes an install link offered only when the seed would change the page, and the history paragraph says a restore appends and clears a tombstone |
| `docs/assistant.md` | Eighteen tools; the two reads and the one restore, the paging pair, and why none of them uses `?v=` |
| `docs/testing.md` | What the editor, tool and integration tests prove |
| `docs/glossary.md` | **Seed** says installing, and a **Restore** entry says what it is not — not a rewind, not a new revision's identity |
| `docs/jobs/README.md` | The job leaves the table and the rough order; a Closed row points at this plan |
| `deploy/operations.md` | The history paragraph says a selected revision is restored as a new save |
| `docs/plans/immutable-page-assets.md` | Implemented, so its "Not in this plan" section stops promising work: it points at this plan, and keeps the rule that a history read is a store read rather than a `?v=` fetch |

Four decisions are recorded with this plan and numbered when it merges: `install` and `restore` are
different verbs (D1); the two actions share one confirmation-and-outcome shape, and each refuses the
case that would add nothing (D2–D6); a restore appends anything that differs from the live source
(D3); and the assistant's history is three page-scoped tools — two reads and one approval-gated
restore — with direction-named cursors and the `read_page` line rule (D8–D13). The fourth supersedes
the rule the archived assistant plan and the job recorded, that restoring is the owner's alone, and
the merge records it as a decision change rather than a drift.

## Risks

| Risk | Mitigation |
| --- | --- |
| A stale POST to `?cmd=restore` with no hash used to install a seed | It now answers `400` naming the history and the editor, never a silent install |
| A restored revision no longer compiles after an Sdk change | It is stored with its diagnostics and the editor says so; the page answers "Handler failed to compile." until fixed, which is the save rule already (DEC-20, DEC-23) |
| Inserting three tools mid-table changes the provider's cached prefix | Expected of any prompt change; the version bump makes it visible, and the position is chosen once (D13) |
| The page moves between a restore's confirmation and its POST | The POST re-reads the live source and answers the same `409`, so a revision that has become the page's own text is refused rather than appended with |
| `list_pages` now answers 50 rows by default where it answered 500 | `truncated` and `after` make the rest one call away, and the prompt teaches the pair |
| The confirmation renders page source into HTML for one visitor | Both views are `no-store`, owner-only, and bounded by the same page-size caps as the editor |
| Two "put it back" actions confuse an author | Both carry a diff and refuse the no-op, and the words install and restore are used consistently in the URL, the action, the flash and the manual |
| The canonical hash rule changes answers the editor already gave | An uppercase hash on `cmd=revision`, `cmd=diff` or `diff_page` becomes a named `400`/refusal instead of a `404`; `?v=` already refused it, so the editor and the tools were the outliers (DEC-241) |
| Dropping Recreate leaves no one-click way into a deleted key's editor | The create-or-open form is the ordinary way to name any key, and the editor says what saving will do; the history's Restore is the way back to the old source |
| An assistant that may restore is a wider reach than the plan it reverses allowed | The tool can only append source the store already holds for that path, and it is approval-gated and path-policy-checked before the owner is asked; the alternative is `write_page`, which can append anything (D10) |

## Delivered when

- `Web.Tests` proves both confirmations read and never compile, the restore's append, hash validation,
  the no-change refusal on the confirmation and the POST, tombstone clearing, the compile outcome in
  both messages, the seed's new command, all three tools' answers and refusals, the paging pair, the
  eighteen-row table, and the prompt version.
- `Web.IntegrationTests` drives the restore round trip and the install confirmation through the real
  host, and the browser suite walks both.
- `docs/assistant.md` counts eighteen tools and names the two reads and the one restore;
  `docs/testing.md` says what the tests prove; `/help` uses install and restore correctly.
- `docs/jobs/ARCH-07.md` is gone, the jobs README records it closed, and `deploy/operations.md` and
  `docs/plans/immutable-page-assets.md` no longer promise work that exists.
- `make build`, `make test` and `make format-check` pass.

## Outcome

Implemented as planned, with three things the work settled that the plan had left open:

- An install and a restore refuse with one `409` shape, `WriteNothingToDoAsync`, so a stale form and a
  source the page already holds are answered by the same document a person can act on.
- `SavedRevision` is what `PageEditor.SaveAsync` returns, so the editor and `write_page` name the
  revision they stored instead of reading it back; the compile outcome is one `CompileOutcome` and one
  `OutcomeClause`, shared by save, install and restore.
- `ToolSpec.Writes` and the `path` in the success envelope were already what the transcript's
  changed-pages line reads, so `restore_revision` needed no new wiring there.

The masking integration test's `Assistant:MaxContextTokens` fixture moved from 110,000 to 114,000: the
system prompt grew by a section and three tool definitions, and the test's invariant is "eight
`read_page` results fit and the ninth does not", not the number itself.
