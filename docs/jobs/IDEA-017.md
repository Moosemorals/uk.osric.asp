<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-017 — A page cannot edit another page, so the editor cannot become one

> **Still the recovery path.** The layout work (DEC-149) did not move the editor into a page, and it
> must not: the shell's own document is deliberately never the stored layout, so a broken layout
> cannot lock the owner out. A page editor would still need its own recovery story.
>
> **The cheap half is unblocked.** A page can now read the page store through
> `ctx.Features.GetRequired<IPageCatalog>()`: list heads, read a page's source, list its history and
> read a revision (DEC-168–DEC-173, [the plan](../plans/archive/page-store-read.md)). Moving the listing,
> history and revision views into pages is what is left; the save, delete, scrub and restore stay in
> the shell.

| | |
| --- | --- |
| Size | Large |
| Area | Sdk, Web, product |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs` |

## Idea

The editor is the largest shell endpoint and the most page-shaped thing in it: a listing, a form, a
history, confirmations and navigation. It is also the one place the shell exercises the authority it
withholds from pages, which is why moving it is a product decision rather than a refactor.

Two obstacles, and they are independent:

- **Authority.** Editing another page means writing any key, plus delete, scrub and restore. The
  assistant already holds this through `PageToolExecutor`, bounded by `PagePathPolicy` (reserved and
  protected paths refused, an optional writable prefix) and by per-call approval (DEC-111). A page
  editor would be the second holder of that authority and needs the same policy, enforced by the
  shell.
- **Addressability.** A page key is an exact path, so one page cannot answer `/edit/{**path}`, and
  every `/edit/...` key is reserved and can never be stored or served. A page-based editor therefore
  needs a different URL or a different routing model.

## Sketch

1. Decide the URL before anything else. `/editor?path=/about` keeps "one page is one key" (DEC-18)
   and makes the edited path a query value the page validates with the same rules `PagePath` already
   applies. Teaching the catch-all to hand a subtree to one page is the other option, and it changes
   what a page key means for every page, not just this one.
2. Take the cheap half first. IDEA-015 has landed: a page can render the listing, history, revision
   and confirmation views through `IPageCatalog`, and the shell keeps `/edit` for save, compile,
   delete and scrub. The shell loses markup, not authority — and there is no bootstrap risk, because
   the save still lives where it always did.
3. The full editor needs IDEA-015, a write seam widened from IDEA-016's self-save to a named key,
   the assistant's path policy, and IDEA-014 for the save message. Antiforgery is already handled:
   `PageEndpoint` refuses an authenticated `POST` without a valid pair before the page runs.
4. Weigh the bootstrap trap before shipping it. The editor is how a page that does not compile gets
   fixed. If the editor is a page, then a broken compiler, a broken editor page or a bad save locks
   the owner out of the one tool that could repair it. Keep a shell fallback — a minimal save form,
   the command line, or the assistant — or keep the save in the shell.
5. The script policy is a prerequisite for the editing UI: the editor is the only response that
   widens CSP to `script-src 'self'` for `/editor.js` (`SecurityHeaders.AllowScripts`), and a page
   cannot set that header today. Whatever gives pages a script opt-in is being settled separately.
6. Reuse `PageEditor.SaveIfRevisionAsync` when a form carries the revision it was rendered from, so
   two tabs cannot silently overwrite each other; it exists for the assistant and has the right
   shape.
7. Tests: a page cannot save a reserved key; an anonymous page cannot write; a stale revision does
   not overwrite a newer save; a deletion from a page tombstones rather than removes, as the editor's
   does.

What I would not do yet: the full editor. The views are worth moving because they are markup; the
save is worth keeping in the shell because it is the recovery path for everything else.
