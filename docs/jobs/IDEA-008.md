<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-008 — Writing pages by hand is the only way to write pages

| | |
| --- | --- |
| Size | Large |
| Area | Web, product, security |
| Location | `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageService.cs` |

## Idea

The editor is a textarea (IDEA-001) and the template language is small. An assistant that turns a
plain-English request — "a page listing my recent posts, newest first" — into a page template,
shows the draft, and saves it through the normal compile-on-save path would be the single biggest
change to how the site gets written. Everything the site already does for a human author, it
already does for a drafted page: compile, store diagnostics, render, roll back badly.

## Sketch

Order matters more than usual here — each step is useful on its own.

1. **A draft surface first.** A second textarea on the editor page: prompt in, draft out, Apply to
   copy the draft into the editor. Nothing saved automatically, no new storage, no new endpoint
   beyond one that takes a prompt. This is the whole feature, minus the polish.
2. **The model call is a shell concern.** `Core` stays pure; a small `IPageDrafting` interface in
   `Web` hides the provider, so tests can use a stub and the site can run with no key at all — the
   button hidden when `Pages:Drafting:ApiKey` is unset.
3. **The prompt carries the constraints, not the model's guesswork.** The system prompt needs
   `IContext`'s surface, the fact that only `<% %>` blocks are C#, the `PageApiPolicy` allow-list,
   and two or three complete example pages. Without those the drafts will use `System.Net` and
   LINQ and fail to compile.
4. **Compile the draft before showing it.** The draft is a page; run it through the existing
   compiler and show the diagnostics next to it, so a bad draft is a bad draft and not a failed
   save.
5. **Bound the loop.** One request, one draft, a token ceiling, and the same render budget a page
   gets. No conversation history and no retry loop until the basics are trusted.
6. **Secret handling.** The key lives in configuration on the server, never in a page, never in the
   response, and never in a log line. The editor is already behind `EditorPolicy`, which is the
   right boundary — a drafting endpoint must not exist outside it.
7. **Depends on IDEA-001** for anywhere to put the draft, and reads best after IDEA-005, because
   drafts of any size want to hoist declarations out of the handler.
8. Tests: the drafting endpoint behind the policy, the no-key state, and a draft that fails to
   compile surfacing diagnostics instead of being saved.
