<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-043 — The authority to write the store belongs to every page

| | |
| --- | --- |
| Size | M — two handles become one, one registration gate and one delete protection go, a public type is renamed, and the tests, the manual, the prompt and the record follow. [The plan](../plans/page-writes.md) is on `idea-043` |
| Area | Sdk, Web (pages, editor), docs, assistant |
| Location | `src/uk.osric.asp.Sdk/IPageEditor.cs`, `src/uk.osric.asp.Sdk/IPageWriter.cs`, `src/uk.osric.asp.Sdk/PageEditResult.cs`, `src/uk.osric.asp.Sdk/PageSaveResult.cs`, `src/uk.osric.asp.Sdk/PageKeyResolution.cs`, `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt`, `src/uk.osric.asp.Web/Pages/PageChain.cs`, `src/uk.osric.asp.Web/Pages/PageEditorScope.cs`, `src/uk.osric.asp.Web/Pages/PageWriterScope.cs`, `src/uk.osric.asp.Web/Pages/PageEditorOptions.cs`, `src/uk.osric.asp.Web/Editor/PageSaver.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `src/uk.osric.asp.Web/Pages/Seeds/scratch`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

One narrowing, deliberate when it was written and in the way now. The owner has decided on 2026-11-15
that it goes.

**The authority is one page's.** `IPageEditor` is registered only for the content key equal to
`Pages:Editor:Key` (`PageChain.cs`), so a handle that can edit any page exists for exactly one page and
nowhere else. The decision behind it — "the editor's authority is the registration, not the handle" —
was right for what it was guarding: a handle with a key parameter handed to every page makes every
stored page a writer of the whole store. The owner's answer is that nothing about editing makes the
editor special: the signed-in visitor is the authority, and the key is not. Two consequences follow:
the editor can be split into sub-pages when it outgrows one file, and the assistant's own page can
offer the same power without a second implementation.

Because the gate was the only reason the self-save had an interface of its own, the two interfaces
merge: a page that may save any page can certainly save itself, and one capability with two saves is
smaller than two capabilities that check the same visitor, the same `HEAD` and the same ended render. The owner named
the merged type `IPageWrites`, kept the bound self-save beside the conditional named save, kept the
one-write-per-render rule until the batch lands, and removed the editor key's delete protection.

The wider class is "a page that maintains the site": the editor, the assistant's tools, an import, a
bulk restore, a seed harvest and a rename of a folder are one shape — several writes, chosen policy,
one report. That is the batch, and it is [IDEA-047](IDEA-047.md): this job is the half with no policy
vocabulary in it.

## Sketch

1. **One write capability, offered to any page.** The registration gate goes: every non-layout render
   is offered `IPageWrites`, and the handle names a key on every named call because it must.
   `Pages:Editor:Key` keeps its meaning as *where the editor is served* — `ctx.EditorPath` still exists
   for links, and the shell's remedy links still read it — and stops being an authority. A layout is
   still never offered it (`pages-layouts-are-markup`).

2. **The authority is the visitor.** Unchanged and non-negotiable: an anonymous render is refused with
   a `UserFacingException` before anything is compiled or stored; a `HEAD` gets the read members and no
   write; an ended render and a reserved key stay refused. What changes is that "which page asked" is
   no longer part of the check.

3. **Two saves, one interface.** `SaveAsync(template)` keeps its bound-to-`ctx.Path` unconditional save
   and its `PageSaveResult`, because it is the page repairing itself and should stay the simplest thing
   in the contract (`pages-scratch-saves-itself`). `SaveAsync(key, template, expectedHash)` keeps its
   conditional save and its `PageEditResult`. Both end in `PageSaver`, so they cannot drift into two
   algorithms. Delete, restore and install stay as they are.

4. **The key stops protecting itself.** The editor's own key can be deleted like any other, and
   `/admin/restore` — a shell route — puts the seeded editor back. The seeded `/pages` stops offering
   the action on its own row, so the capability is not where the guard lives.

5. **The rename.** `IPageEditor` and `IPageWriter` become `IPageWrites`; `PageSaveResult` stays, because
   the bound save still answers with it. Every stored page that named the old types stops compiling
   until it is edited; the editor reads source without compiling, so every broken page is repairable
   there, and `/admin/restore` reinstalls the editor if the editor is the broken one.

6. **Nothing else moves down.** A worker gets no write (`pages-a-worker-gets-no-response`); the shell's
   own writes stay routes (`pages-page-first`); the read stays public
   (`pages-reading-the-store-is-public`).

7. **The documentation.** `/help` gains the capability's section, the two saves and their two answers,
   and the key's two lost jobs (`product-manual-is-a-page`); `PageAuthorPrompt` gains the same words
   with its `Version` bumped, because the assistant is the second caller; [pages](../pages.md) records
   the contract; and the merge retires the entry that made the registration the authority, adds
   **pages-any-page-may-write-the-store**, and edits `pages-a-page-may-save-itself`.

## Open questions

None left for this step; the owner answered them on 2026-11-15, and [the plan](../plans/page-writes.md)
records the answers and the two wording questions it still asks.

## Not in scope

- **The batch.** [IDEA-047](IDEA-047.md): several writes, a named policy, one report, and the end of
  the one-write rule.
- **Renaming, searching and bulk restore.** [IDEA-042](IDEA-042.md), [IDEA-038](IDEA-038.md) and the
  single-action members they still use.
- **Removing `Pages:Editor:Key`.** It still decides routing; a host that stopped knowing where the
  editor is would also lose `ctx.EditorPath` and every remedy link.

**Origin:** the owner's note of 2026-11-15 comparing the site's IDE plan (`/ide-plan`, a stored page that
is not in this repository) with the job list. The IDE plan's Save all and the assistant's multi-file
edit are the two callers that found the one-save rule; the sub-route wish found the one-key rule. The
owner's decision — "decisions are guidelines, not carved in stone; drop it" — is the whole of the
mandate. This job was split on 2026-11-15: the widened authority and the merged capability are here, and
the batch is [IDEA-047](IDEA-047.md), "a bit further down the backlog".

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 4 there, and
it has [a plan of its own](../plans/page-writes.md) on its branch.
