<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-043 — The authority to edit the store belongs to one page, and to one save

| | |
| --- | --- |
| Size | L — two contract rules revised, one registration gate removed, a batch write with three policies, a transaction boundary, a report value, and the tests, the manual and the assistant prompt for each. It wants a plan of its own |
| Area | Sdk, Web (pages, editor, work), docs, assistant |
| Location | `src/uk.osric.asp.Sdk/IPageEditor.cs`, `src/uk.osric.asp.Sdk/PageEditResult.cs`, `src/uk.osric.asp.Web/Pages/PageChain.cs`, `src/uk.osric.asp.Web/Pages/PageEditorScope.cs`, `src/uk.osric.asp.Web/Pages/PageEditorOptions.cs`, `src/uk.osric.asp.Web/Editor/PageSaver.cs`, `src/uk.osric.asp.Web/Pages/IPageRevisionStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

Two narrowings, both deliberate when they were written and both in the way now. The owner has decided
on 2026-11-15 that both go.

**The authority is one page's.** `IPageEditor` is registered only for the content key equal to
`Pages:Editor:Key` (`PageChain.cs`), so a handle that can edit any page exists for exactly one page and
nowhere else. The decision behind it — "the editor's authority is the registration, not the handle" —
was right for what it was guarding: a handle with a key parameter handed to every page makes every
stored page a writer of the whole store. The owner's answer is that nothing about editing makes the
editor special: the signed-in visitor is the authority, and the key is not. Two consequences follow:
the editor can be split into sub-pages when it outgrows one file, and the assistant's own page can
offer the same power without a second implementation.

**The save is one per render.** `PageEditorScope.ThrowIfActed` refuses a second edit and
`PageWriterScope` refuses a second self-save. That makes Save all a sequence of requests that cannot
be atomic — a refusal on the third page leaves the first two saved with no way to know that from the
fourth — and it makes a multi-file edit by the assistant N requests with N chances to leave the site
half-changed. `pages-a-page-may-save-itself` says "one render makes at most one save"; the owner has
decided that rule goes, and the capability asks the caller which behaviour it wants instead.

The wider class is "a page that maintains the site": the editor, the assistant's tools, an import, a
bulk restore, a seed harvest and a rename of a folder are one shape — several writes, chosen policy,
one report. Building it for Save all alone would be building it six times.

## Sketch

1. **One write capability, offered to any page.** The registration gate goes: every non-layout render is
   offered the handle, and the handle names a key on every call because it must. `Pages:Editor:Key`
   keeps its meaning as *where the editor is served* — `ctx.EditorPath` still exists for links — and
   stops being an authority. A layout is still never offered it (`pages-layouts-are-markup`).

2. **The authority is the visitor.** Unchanged and non-negotiable: an anonymous render is refused with
   a `UserFacingException` before anything is compiled or stored; a `HEAD` gets the read members and no
   write; an ended render, a reserved key and the editor's own page stay refused. What changes is that
   "which page asked" is no longer part of the check.

3. **A batch is a value and a policy.** `SaveManyAsync(IReadOnlyList<PageEdit> edits, PageSavePolicy
   policy)` where a `PageEdit` is (key, template, expected hash) — the same three inputs a single save
   already takes, so a caller that can do one can do many. Three policies, and the caller names one:
   - **All** — compile every edit, then write the ones that compiled and were not stale in one
     transaction; any refusal writes nothing and the report says which edit refused.
   - **FirstRefusal** — write in order and stop at the first edit that is not saved; the report says
     what was written and what was not attempted.
   - **Continue** — write every edit that can be written and report each refusal beside it; the report
     is the only record of the partial success.

   The report is one row per edit — saved, with its hash; stale, with the head it was rendered from;
   refused, with the sentence; or failed, with its diagnostics — plus the policy that ran and, for All,
   whether anything was committed.

4. **The transaction boundary.** A compile is slow (30–70 ms warm, bounded by
   `Pages:Compile:TimeoutSeconds`) and slots are scarce (`Pages:Compile:Slots` is 1), so an All batch
   cannot hold a write transaction open across compiles. It compiles first — which is
   [IDEA-037](IDEA-037.md)'s work, borrowed — and then opens one transaction and inserts every revision
   in it. If the render's deadline passes while compiling, nothing has been written, which is exactly
   what All promised. A save that has begun is never abandoned
   (`pages-a-page-may-save-itself`), and a batch that has begun committing is not either.

5. **One save is a batch of one.** The existing members stay, with the single-save behaviour preserved,
   and `SaveAsync` becomes the one-edit case of the same algorithm so the conditional save, the stale
   answer and the diagnostics cannot drift between the two. `IPageWriter` keeps its bound-to-this-key
   handle and its single save: a page repairing itself is the recovery path and should stay the simplest
   thing in the contract (`pages-scratch-saves-itself`).

6. **Nothing else moves down.** The batch writes pages; it does not rename
   ([IDEA-042](IDEA-042.md)), search ([IDEA-038](IDEA-038.md)) or check without saving, except as the
   first half of All. A batch that also deletes, installs seeds or restores revisions multiplies the
   policy question by four and is not in the first version — those stay single-action members.

7. **The documentation.** `/help` gains the capability's section, the three policies and their exact
   words (`product-manual-is-a-page`); `PageAuthorPrompt` gains the batch and its policies with its
   `Version` bumped, because the assistant is the second caller and should batch a multi-file edit
   rather than posting it as a sequence; [pages](../pages.md) records the widened authority and what it
   does not change; and the merge writes the revised entries in [decisions](../decisions.md), where
   `pages-the-editor-capability` and `pages-a-page-may-save-itself` each gain the narrowing that was
   removed.

## Open questions

1. **Whether the three policies are three enumerators or a result-then-commit pair.** All and
   FirstRefusal differ in when they stop; Continue differs in what it reports. One enum with three
   names is the smallest vocabulary, and the words are the ones `/help` and the prompt will use, so they
   deserve the most care in the whole job.
2. **What All does with a compile error.** It is not a refusal: the edit compiled and the compiler
   answered a diagnostic, and a page that does not compile is still storable by a single save today. So
   All writes it and reports the diagnostics, and only a *refusal* (stale, reserved, anonymous) stops
   the transaction. Whether "compiles cleanly" should be a fourth policy is the question.
3. **Whether the report is per edit or per key.** One row per key is what a caller wants; two edits to
   one key in one batch must then be refused as a page bug rather than silently merged.
4. **Whether a batch may span a library and its dependents.** It writes both; whether it compiles the
   dependents again in the same batch, or leaves them stale as the lazy path does
   (`page-libraries` D7), is a property of the saves rather than of the batch, so the answer is likely
   "leave them stale, and let [IDEA-040](IDEA-040.md) say which they are".
5. **Whether the editor's own key keeps its delete protection.** The recovery rule says a broken editor
   is repaired from `/admin/restore`, so deleting the page is still refused; the *saving* of it is now
   any page's business. Confirm that the protection is about the key, not about the capability.
6. **Whether `IPageEditor` is still the right name.** It is now "the store's write capability offered to
   a page"; `IPageEditor` says who used to get it. A rename is cheap while nothing outside the
   repository names it, and it is a break for every stored page that does. The answer decides the
   migration note in `/help`.

## Not in scope

- **Anything a page can do without this.** A page repairing itself keeps `IPageWriter`
  (`pages-a-page-may-save-itself`); the store read stays public (`pages-reading-the-store-is-public`);
  a layout gets neither.
- **The shell's own writes.** Import, export, restore and scrub are routes for the reasons
  `pages-page-first` records, and a page that can write does not make them pages.
- **A second transaction model.** One database, one transaction; no outbox, no two-phase commit, no
  queue. `n` is small (`code-n-is-small`).
- **Undo.** A batch that wrote is a batch of revisions, and the history is where a mistake is answered.

**Origin:** the owner's note of 2026-11-15 comparing the site's IDE plan (`/ide-plan`, a stored page that
is not in this repository) with the job list. The IDE plan's Save all and the assistant's multi-file edit
are the two callers that found the one-save rule; the sub-route wish found the one-key rule. The owner's
decision — "decisions are guidelines, not carved in stone; drop it" — is the whole of the mandate, and
the shape of the three policies is the owner's too.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 4 there, and it is the
one slice that wants a plan of its own on its branch before anything is built.
