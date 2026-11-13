<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-017 — A page cannot edit another page, so the editor cannot become one

> **Still the recovery path.** The layout work (DEC-149) did not move the editor into a page, and it
> must not: the shell's own document is deliberately never the stored layout, so a broken layout
> cannot lock the owner out.
>
> **The recovery path is now its own job.** [IDEA-028](IDEA-028.md) adds a shell page that restores
> every seed from the bundled source, so a broken page editor can be repaired without the editor
> working. That is the answer to the bootstrap trap below, and it is why the plan can remove the
> shell editor rather than keep a permanent fallback.
>
> **The cheap half is unblocked.** A page can now read the page store through
> `ctx.Capabilities.GetRequired<IPageCatalog>()`: list heads, read a page's source, list its history and
> read a revision (DEC-168–DEC-172, DEC-175, [the plan](../plans/archive/page-store-read.md)). The
> listing, history and revision views have moved; the named-key save, delete and restore move with
> them, and scrub stays in the shell.
>
> **The plan is written.** [The page-editor plan](../plans/page-editor.md) takes the chosen route
> (`/pages?edit=/about`), adds the named-key capability, retires `/edit` to a `404` until somebody
> writes a page there, and leaves scrub in the shell for a separate decision.

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
  the editor owns that subtree today. A page-based editor therefore needs a different URL. The plan
  uses `/pages?edit=` and then retires `/edit` entirely: it leaves the reserved list, so it is an
  ordinary missing page (`404`) until somebody writes a page there.

## Sketch

1. Decide the URL before anything else. The seeded `/pages` becomes the editor: it already lists the
   store and shows a page's history and source, so `?edit=/about` adds the form and `?page=/about`
   keeps the read. This keeps "one page is one key" (DEC-18) and makes the edited path a query value
   the page validates with the same rules `PagePath` already applies. Teaching the catch-all to hand
   a subtree to one page is the other option, and it changes what a page key means for every page,
   not just this one.
2. Take the cheap half first. IDEA-015 has landed: a page can render the listing, history, revision
   and confirmation views through `IPageCatalog`, and the shell editor keeps `/edit` for save,
   compile, delete and scrub until the page takes over. The shell loses markup, not authority — and
   there is no bootstrap risk, because the save still lives where it always did.
3. The full editor needs IDEA-015 and a write seam widened from IDEA-016's self-save to a named key.
   Antiforgery is already handled: `PageEndpoint` refuses an authenticated `POST` without a valid
   pair before the page runs. Both IDEA-015 and IDEA-016 have landed, so what is left is the key:
   `PageSaver` takes a `PagePath` already, `IPageWriter` is the handle that pins it to `ctx.Path`,
   and `PageWriterScope`'s authority rules — a signed-in visitor, no `HEAD`, one save per render —
   are the ones a named-key seam inherits (DEC-189–DEC-196). The seam is a new capability offered
   only to the editor page, not a widened `IPageWriter`; the assistant's protected-path policy is
   for the assistant, not the owner.
4. The bootstrap trap is answered by IDEA-028. The editor is how a page that does not compile gets
   fixed, so a broken editor page must not be the end of the story. A shell page that restores every
   seed from the bundled source is the way back; the shell editor can then go, rather than staying
   forever as a fallback.
5. The script policy is settled: a page opts in with `ctx.AllowScripts()`, and `/editor.js` is a
   seeded module like any other. The page editor must write the same `.editor`, `#gutter` and
   `.diagnostics` markup the module already expects.
6. Reuse the conditional save when a form carries the revision it was rendered from, so two tabs
   cannot silently overwrite each other. Move it from the request-scoped `PageEditor` to the
   singleton `PageSaver`, so a save that has begun survives the render's end (DEC-191).
7. Tests: a page cannot save a reserved key; an anonymous page cannot write; a stale revision does
   not overwrite a newer save; a deletion from a page tombstones rather than removes, as the editor's
   does.

What stays out of this job: scrub. The page editor saves, tombstones, restores a revision and
installs a seed; permanently removing history remains a shell confirmation at `/admin/scrub/{path}`
until it has a decision of its own. The page links to it for tombstones and never performs it.
