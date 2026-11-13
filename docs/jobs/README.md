<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Jobs

One file per distinct job — something the site gets wrong (`SEC-`, `BUG-`, `OPS-`, `ARCH-`) and
something it could usefully become (`IDEA-`). Each file has a short ID (`SEC-03`, `IDEA-005`, ...)
to use in commits, branches and discussion. A whim is smaller than a job and shares one file,
[WHIMS.md](WHIMS.md).

Two measures, and neither is a plan. **Severity** is the reviewer's assessment of risk, and is blank
for an idea because an idea is not something that has gone wrong. **Size** is how much work it looks
like: `S` a sitting, `M` a day or so, `L` big enough to want a plan of its own. The size is the
useful column, because a good few of these are an hour's work and the order at the bottom spends its
first effort there.

The defect and operation jobs were raised by the architecture review and the code-health scan of
2026-10-30; BUG-13 came out of the BUG-06 fix, ARCH-11 came out of the BUG-05/BUG-13 planning, and
BUG-14 came out of the metrics dashboard planning, and BUG-18 came out of the editor's
save-and-view work. The `IDEA-` jobs were added on 2026-11-01, OPS-06
and IDEA-013 came out of the naming-consistency planning, and every status below was checked against
the code on 2026-11-09. IDEA-014 to IDEA-019 came out of the shell-versus-page review of 2026-11-07:
what is left in the shell is mostly authority a page does not have rather than markup a page could
not write. IDEA-020 came out of the import and export planning, and records why a destructive reset
is not part of that flow. IDEA-022 came out of the [page-features
planning](../plans/archive/page-features.md) of 2026-11-08, and is worth doing now that storage and
fetch have left `SimpleContext`. ARCH-14 came out of the nullable-pages discussion of 2026-11-08 and
carried a [plan of its own](../plans/archive/page-diagnostics-and-visibility.md). ARCH-15 came
out of the assistant shell-file work of 2026-11-09 (DEC-198). IDEA-023 came out of the
[assistant-memory planning](../plans/archive/assistant-memory.md) of 2026-11-09: that plan gave the assistant
a memory and left the page that shows it to its owner behind, and [its own plan](../plans/archive/assistant-memory-page.md)
has now built it. IDEA-024 and IDEA-025 came out of [the page-first plan](../plans/page-first.md) of
2026-11-09 (DEC-210): the rule that anything a page can serve is a page, and the two things the shell
still does because a page cannot (an archive, and a live run). IDEA-026 came out of the
[capability-naming plan](../plans/archive/capability-naming.md) of 2026-11-10 (DEC-228): it is the
release that removes the deprecated `Features` alias once the page store has no page that needs it.
ARCH-16 came out of the [tool-types work](../plans/archive/assistant-tool-types.md) of 2026-11-11
(DEC-231): making a tool one type showed that the page tools and the shell answer the same questions
about the page store through two implementations. IDEA-028 came out of the [page-editor
planning](../plans/page-editor.md) of 2026-11-13: the editor can become a page only if a broken
editor has a way back, and one button that restores every seed is that way. IDEA-030 came out of
that plan's fourth phase: retiring `/edit` and moving the editor's key into configuration showed
that a dozen views each assemble their own navigation.

| ID | Severity | Size | Job |
| --- | --- | --- | --- |
| [SEC-03](SEC-03.md) | Medium | L | Page code has no process boundary |
| [ARCH-13](ARCH-13.md) | Low | M | A page cannot stream its response |
| [ARCH-16](ARCH-16.md) | Low | M | The assistant asks the page store the same questions twice |
| [OPS-03](OPS-03.md) | Medium | M | The gate exists but nothing runs it unasked |
| [OPS-05](OPS-05.md) | Medium | M | No outbound notifications when the site breaks |
| [IDEA-001](IDEA-001.md) | — | L | The editor is a plain textarea, though it now has a line gutter, linked diagnostics and a state on every row of the page list |
| [IDEA-005](IDEA-005.md) | — | L | Pages cannot share code: the layout half landed, a named declaration a second page can use is still deferred |
| [IDEA-017](IDEA-017.md) | — | L | A page cannot edit another page, so the editor cannot become one |
| [IDEA-020](IDEA-020.md) | — | M | Import only adds and overwrites, so an archive cannot reset a site |
| [IDEA-022](IDEA-022.md) | — | S | `SimpleContext` is one class doing five jobs |
| [IDEA-024](IDEA-024.md) | — | L | `/admin/import` and `/admin/export` are shell code because an archive is not a form |
| [IDEA-025](IDEA-025.md) | — | L | The assistant's conversation is a stream and a set of host actions, not a page |
| [IDEA-027](IDEA-027.md) | — | S | A page cannot leave a message for a page it sends a visitor to |
| [IDEA-030](IDEA-030.md) | — | M | Every view writes its own navigation: the shell, the admin, the assistant and the seeds each assemble the same links |

An `IDEA-` file says `## Idea` and `## Sketch` where a defect says `## Problem` and
`## Fix outline`; both carry the same table under the title, with `Size` in place of `Severity`.

## Whims

A whim is an idea that has not acquired a shape. It gets an ID (`WHIM-01`) and one or two lines in
[WHIMS.md](WHIMS.md), never a file of its own, and it is not counted, sized or ordered here.
Promoting a whim means writing an `IDEA-` file and removing its line; deleting one is free.

## Help for authors

The author manual is the seeded page `/help`, linked from the editor and the page list. A job or
plan that adds or changes something a page author can do — an `IContext` member, a capability, a
layout slot, an editor action, a content type or a response rule — updates `/help` in the same
change. The update belongs beside the tests and the architecture notes in the plan, not in a
follow-up.

## The programme

The order is rough on purpose — the first few are the point, and the rest move as they are worked —
but each step is one unit of work, on one branch in one worktree
([how](../../CONTRIBUTING.md#working-on-it)). Each step stops at a gate: it says what changed, what
proved it and what it is unsure of, and nothing merges until the answer is yes. Small and
self-contained comes first, because these are the ones that never get scheduled otherwise. A step
is `done` when its merge is in, and `in flight` while its branch is open.

| # | Step | Size | Needs | Gate | Status |
| --- | --- | --- | --- | --- | --- |
| 0 | **Workflow** — the branch and worktree policy, its `make` targets and this programme | S | — | merge | done |
| 1 | [ARCH-15](#closed) — delete `Assistant:ProtectedPaths`; the assistant may write every page the shell does not route | S | — | merge | done |
| 2 | [IDEA-026](#closed) — remove the `Features` alias; the data gate was waived, deliberately (below) | S | — | merge | done |
| 3 | [IDEA-028](#closed) — `/admin/restore`, over the seed read and the saver ARCH-07 left behind | M | — | merge, and a manual restore of a broken page | done |
| 4 | [IDEA-019](#closed) — repeated request values, and `Encoding` and cryptography in the API policy; the cookie point is dropped because IDEA-014 gives a page per-visitor state | S | — | merge | done |
| 5 | [Page editor](../plans/page-editor.md) — update the plan against what has landed, then phases 0–5 | L | 3 | the plan, then every phase | — |
| 6 | [ARCH-16](ARCH-16.md) — one read seam for the assistant's tools and its applets | M | 5 (rework) | merge | — |
| 7 | [ARCH-13](ARCH-13.md) — `IContext.FlushAsync`, and BUG-17's failed-request log line with it | M | — | merge | — |
| 8 | [IDEA-025](IDEA-025.md) — the assistant as a page; it gets its own plan first | L | 6, 7 | the plan, then every phase | — |
| 9 | [Page first](../plans/page-first.md) — re-scope against 5–8: DEC-210 and DEC-211 are recorded, but the audit and the memory page are not; build the page, or archive the plan behind IDEA-024 and IDEA-025 | L | 5–8 | the plan, then build-or-close | — |

Steps 1 and 2 are independent and may run together, as may 6 and 7. Everything else touches the same
files — the editor, `/pages`, `SimpleContext`, the assistant prompt — and is sequential. A long plan
merges phase by phase so a regression is bisectable, and its branch goes when its last phase is in.

Page first is less unstarted than its status row says: DEC-210 and DEC-211 are recorded and the
rule is agreed, but [architecture](../architecture.md) has no audit table, `/memory` is still a shell
route, and DEC-211 still names `ctx.Features` — the rename it has to follow is step 2's.

Step 6 follows 5 rather than needing it: the editor's listing is going to carry the compile state,
and that is the state the assistant's one seam should answer with too. Step 7 is also the home of
BUG-17's one loose end — the failed-request log line a mid-run reload leaves behind.

**IDEA-026 had no data gate.** Its sketch made the query the gate, and the decision was to remove
the alias without running it. The cost of being wrong is a stored page that no longer compiles:
it answers "Handler failed to compile." with an ordinary compiler error until its source is edited or
its seed restored. [IDEA-028](#closed) (step 3) is the way back, and reverting the one merge is
the other.

Not in this programme: [OPS-03](OPS-03.md) is the obvious next candidate once the list above is
empty; [SEC-03](SEC-03.md) and [OPS-05](OPS-05.md) wait for a reason to exist;
[IDEA-001](IDEA-001.md) wants a third-party editor; [IDEA-005](IDEA-005.md) is a compile-time
question of its own; [IDEA-020](IDEA-020.md) records why a destructive reset is not built;
[IDEA-022](IDEA-022.md) is the `SimpleContext` split, which step 7 touches; and
[IDEA-027](IDEA-027.md) is the flash a page cannot leave for another page.

## Closed

Resolved jobs; the files were deleted and their history is in git.

| ID | Fix |
| --- | --- |
| [IDEA-019](../decisions.md) | Every request value survives: `QueryValues` and `FormValues` keep each name's values in order, and the API policy admits encoding, SHA-2 hashing, HMAC signing, a random source and a constant-time compare. DEC-257 |
| [IDEA-028](../decisions.md) | `/admin/restore` lists every seed's state, restores each changed, missing or tombstoned one in a single POST and reports every outcome; a matching seed and a page the owner made are untouched. DEC-256 |
| [IDEA-026](../decisions.md) | The `Features` alias is gone: `IPageFeatures`, `IContext.Features`, the `PAGE005` diagnostic and the `[Obsolete]` attributes are removed, and a page that still says `ctx.Features` no longer compiles. DEC-254 |
| [ARCH-15](../decisions.md) | The assistant's only read-only keys are the shell's own routes: `Assistant:ProtectedPaths`, its `IsProtected` branch, the empty configuration row and the prompt's sentence are gone, so every page the shell does not route is the assistant's to write. DEC-253 |
| [ARCH-07](../plans/archive/revision-restore.md) | A page's history restores: the editor confirms a revision or a seed install with a diff and saves the chosen source as a new revision, refusing the one that would change nothing; the assistant gains `list_revisions`, `read_revision` and an approval-gated `restore_revision`, and both listings page with `limit` and a cursor. See [the plan](../plans/archive/revision-restore.md) |
| [ARCH-02](../pages.md#page-contract) | Pages are a restricted runtime: a plain `PageRequest` snapshot, buffered output applied after the handler, allow-listed headers, `GET`/`POST`/`HEAD` only, and a 1 MiB form cap |
| ARCH-03 | Renders and compiles each have a slot and a deadline, an abandoned render is logged and counted, and `/heath` reports it. See [the plan](../plans/archive/page-execution-budget.md). SEC-03 is deliberately still open |
| ARCH-04 | `LoadedPage` unwraps the reflection wrapper with `ExceptionDispatchInfo`, so the shell sees the page's own exception |
| ARCH-05 | `PagePath` normalises every key in `Core`, the shell redirects to the canonical URL with `308`, and `Pages:ReservedPaths` keeps page keys off the site's own routes |
| ARCH-06 | The `/edit` listing shows live pages and tombstones, offers new-page navigation, and does not load revision bodies. See [the plan](../plans/archive/editor-tools-and-immutable-assets.md) |
| ARCH-08 | `Web.IntegrationTests` drives the real host against a throwaway database: routing, middleware, the error branch, startup migrations and the editor round-trip |
| ARCH-09 | The `Specs` suite only restated compiler unit tests, so it and Reqnroll were deleted |
| ARCH-10 | Delete tombstones a page and keeps its revisions; scrub removes them with bounded SQLite cleanup. See [the plan](../plans/archive/editor-tools-and-immutable-assets.md) and [deployment storage](../../deploy/operations.md#storage-and-migrations) |
| ARCH-11 | The authenticated editor generated C# from a saved template without compiling it; the generated-source viewer was later retired with DEC-174 |
| ARCH-12 | `<%= %>` writes an escaped value and `<%== %>` a raw one, and an empty expression is `TEMPLATE002`. See [the plan](../plans/archive/template-value-output.md) |
| ARCH-14 | Pages compile nullable-enabled with an oblivious scaffold, a warning never stops a page, a page has one of five states, and the assembly surface is enforced by `Microsoft.CodeAnalysis.PublicApiAnalyzers`. See [the plan](../plans/archive/page-diagnostics-and-visibility.md) |
| BUG-01 | `UseExceptionHandler` uses an inline branch, so a throwing `POST` returns 500 |
| BUG-02 | `ErrorEndpoint` sets 500 itself; `/error` is no longer a public route |
| BUG-03 | The 404 response sets `text/plain; charset=utf-8` |
| BUG-04 | Compilation diagnostics are HTML-encoded and wrapped in `<pre>` |
| BUG-05 | Compile diagnostics carry a `TemplatePosition` mapped from `#line` directives; an unmapped error is a compiler fault and a `500`. See [the plan](../plans/archive/compiler-diagnostics.md) |
| BUG-06 | A template that does not parse is a value, not an exception: a `TEMPLATE001` diagnostic is stored as a compile failure, so the editor shows it and the page answers "Handler failed to compile." |
| BUG-07 | `SavePageAsync` is one `INSERT … ON CONFLICT`, so concurrent saves for a new key leave one row |
| BUG-08 | The reference set is built from `TRUSTED_PLATFORM_ASSEMBLIES` at startup, so a missing value stops the host instead of compiling every page against nothing |
| BUG-09 | A page's log entries are filed under `uk.osric.asp.Pages` with the page's key on a `Path` scope (DEC-24) |
| BUG-10 | `make format-check` passes |
| BUG-11 | Stale `*.feature.cs` files were deleted; Reqnroll 3 generates in memory |
| BUG-12 | `Web` no longer references the Roslyn Workspaces packages; central transitive pinning fixes the Design-time conflict |
| BUG-13 | `PageDiagnostic` carries severity and a warning-only page stores an image, renders, and reports "Saved with warnings." |
| BUG-14 | A redirect keeps its query and fragment: `Redirects.To` encodes each part with the type that understands it. See [the plan](../plans/archive/site-navigation.md) |
| BUG-15 | A page key containing `?` or `#` is refused by both the page and the editor rather than normalised onto another key |
| BUG-16 | Every view is one document: `ShellDocument` writes the head, `<main id="main">` and the foot, and `AccessibilityTests` applies twelve rules to every view over rendered markup (DEC-110). See [the plan](../plans/archive/semantic-html-and-accessibility.md) |
| BUG-17 | A long think is not cut off by its own duration: `Assistant:RequestTimeoutSeconds` is re-armed by every event the provider sends, a stall fails as `provider_timeout` with its own sentence, a message that stops at the output limit is stored `truncated`, and the transcript renders the run's own reason after the reload (DEC-119). The one thing left is the failed-request log line a mid-run reload leaves behind, which is [ARCH-13](ARCH-13.md)'s |
| BUG-18 | Save and view reads its command from the form for a POST and the query for a link, and a failed save stays on the editor with its diagnostics. Every human surface answers with a document: `Errors/ErrorResponses.cs` writes the shell or an RFC 9457 problem document by `Accept`, and `UseStatusCodePages` covers every status no endpoint described (DEC-131). See [the shell](../shell.md#error-responses) |
| OPS-02 | The health endpoint and its monitoring: `/heath` pings storage and reports abandoned renders, the quadlet probes it and restarts after three failures, and `/status` draws what Prometheus scraped. The alerting it named was always [OPS-05](OPS-05.md)'s (DEC-45). See [deployment](../../deploy/operations.md#resource-limits-and-restarts) |
| OPS-06 | `/status` is rebuilt from the instruments: one panel each, the type decides the chart, a dense grid of self-contained drawings, and a chart endpoint for a link. See [the plan](../plans/archive/status-page-rebuild.md) and DEC-180–DEC-185 |
| DOC-01..05 | Instructions, architecture, deployment, testing and `.editorconfig` match the code |
| IDEA-002 | A page reaches any `http` or `https` host through `ctx.Capabilities.GetRequired<IHttpFetch>()`; every failure is a value (DEC-216, DEC-129, DEC-143). See [the plan](../plans/archive/page-fetch-and-storage.md) |
| IDEA-023 | `/assistant/memory` lists every memory the owner holds, grouped by kind with the pinned ones first, each row a form that saves content, kind and pin under the version it was rendered from, with a count against the cap and a forget that asks on its own page first (DEC-205–DEC-209). See [the plan](../plans/archive/assistant-memory-page.md) |
| IDEA-003 | A page keeps state through `ctx.Capabilities.GetRequired<IPageStorage>()` in a `PageState` table with per-page caps (DEC-125, DEC-126, DEC-127, DEC-142). See [the plan](../plans/archive/page-fetch-and-storage.md) |
| IDEA-004 | The page contract is its own reference-free assembly, `uk.osric.asp.Sdk`. See [the plan](../plans/archive/page-sdk-split.md) |
| IDEA-006 | `/site.css` is a seeded page rather than a static file, so the site's look is edited like any other page. See [the plan](../plans/archive/non-html-pages.md) |
| IDEA-007 | `/rocket.svg` is a seeded page served as `image/svg+xml`. See [the plan](../plans/archive/non-html-pages.md) |
| IDEA-008 | Implemented as the authenticated page-author assistant, off by default, with bounded page-store tools (DEC-111). See [the plan](../plans/archive/ai-page-assistant.md) |
| IDEA-009 | Pages leave and return as a SQLite archive through `/admin/export` and `/admin/import` (DEC-139). See [the plan](../plans/archive/page-import-export.md) |
| IDEA-010 | A retained revision is an **asset**: `?v=<hash>` on a page's own URL selects it, a page with no C# is never compiled and is served from source, a versioned asset promises `immutable` with the hash as a strong `ETag`, an unversioned response stays `no-store`, and the shell tags its own links (DEC-238–DEC-248). Runtime-change cleanup of obsolete images and symbols is deferred, not part of this job. See [the plan](../plans/archive/immutable-page-assets.md) |
| IDEA-011 | Pages opt into the existing sign-in with `ctx.User`, `ctx.RedirectToLogin()` and `ctx.AntiforgeryField` (DEC-132–DEC-137). See [the plan](../plans/archive/page-authentication.md) |
| IDEA-012 | The page handler is asynchronous, and `Task`'s starting and blocking members are denied by name (DEC-123, DEC-124). See [the plan](../plans/archive/async-page-contract.md) |
| IDEA-013 | The site's vocabulary is [the glossary](../glossary.md): one entry per term, with the term it is not and the code that owns it |
| IDEA-016 | A page saves its own source through `ctx.Capabilities.GetRequired<IPageWriter>()`, bound to `ctx.Path` and answering with the revision hash and the compiler's diagnostics. **Authority is asymmetric**: the read is public (DEC-169) and the write requires a signed-in visitor, refused inside the capability before anything is compiled — IDEA-016 point 3 named that rule and put it in `SimpleContext`, which is where it is not. `PageSaver` and `IPageRevisionStore` move the shell's save rather than copy it, so there is still one writer (DEC-192), a started save finishes even when the render gives up (DEC-191), and a layout is offered the read and not the write (DEC-194). The seeded `/scratch` is a page that edits itself. See [the plan](../plans/archive/page-self-save.md) and DEC-189–DEC-196 |
| IDEA-018 | `Sdk/PageDocument` owns the document and `ShellDocument` and the seeded layout are adapters over it (DEC-156) |
| IDEA-021 | The assistant can check the JavaScript it writes without running or storing it (DEC-145, DEC-146). See [the plan](../plans/archive/assistant-script-validation.md) |
| OPS-01 | `UseForwardedHeaders` trusts `X-Forwarded-Proto`/`X-Forwarded-Host` from any peer, which the loopback-only publish makes safe; HSTS emits |
| OPS-04 | `appsettings.json` raises `Microsoft.EntityFrameworkCore` to `Warning`, and Development puts SQL back at `Information` |
| PERF-01 | A page is compiled when it is saved, the image is stored beside the source, and `TemplateHash` and `RuntimeId` decide whether it is still usable. A page with no usable image is compiled on its next request |
| SEC-01 | The editor requires a local account: `Users` table, `users` command, cookie with a security stamp. See [the plan](../plans/archive/editor-authentication.md) |
| SEC-02 | Login, logout and editor POSTs validate an antiforgery token and answer `400` before reading the form; the save message lives in the session and saves answer `303` |
| STYLE-01 | `EditorEndpoint` validates through a `SaveAsync` helper instead of `goto` |
| STYLE-02 | `IStorage.ListFilesAsync` returned a list; the query was later removed as unused with ARCH-06 |
| STYLE-03 | `Token` and `TokenType` have their own files; `Token` is a `readonly record struct` carrying a `TemplatePosition` |
| STYLE-04 | Exception tests assert with `await Assert.ThrowsAsync`/`Assert.CatchAsync`; the convention is in [testing](../testing.md) |
| IDEA-014 | A page keeps per-visitor state through `ctx.Capabilities.GetRequired<IPageSession>()`: `TryGetValueAsync` on its own path and on another page's, `SetAsync`, `DeleteAsync`, `KeysAsync` and `AllKeysAsync`, with the write bound to the caller's path and the read open. The session is minted on the first write, travels in the shell's `s` cookie, is bounded per visitor (`Pages:Session`), slides, sweeps on a timer and clears on sign-out; pages are `no-store` because a body can be per-visitor, and `context.Session` is gone. See [the plan](../plans/archive/page-session.md) and [storage](../storage.md#page-sessions) |
| IDEA-015 | A page reads the page store through `ctx.Capabilities.GetRequired<IPageCatalog>()`: `ListAsync(prefix)`, `ReadAsync(key)`, `RevisionsAsync(key)` and `ReadRevisionAsync(key, hash)`, with `PageEntry`, `PageSource` and `PageRevisionInfo` as the values it may name. The listing returns every head marked deleted, reserved, HTML and no-code asset; a prefix is a subtree; `Pages:Read` bounds the rows and one render's source, and both refuse rather than truncate. The read is as public as the page that calls it — **point 3 of the job, which put the gate in the shell, was overruled** in favour of the ordinary `ctx.User` guard in the page's own source (DEC-169) — and the shell keeps every write (DEC-175). The seeded `/recent` is a public listing and `/pages` a guarded source view. See [the plan](../plans/archive/page-store-read.md) and DEC-168–DEC-172, DEC-175 |
