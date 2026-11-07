<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-18 — Save and view answered a bare 400, and errors were blank

| | |
| --- | --- |
| Severity | Medium |
| Area | Editor, error responses |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, `src/uk.osric.asp.Web/Errors/`, `src/uk.osric.asp.Web/Program.cs` |

## Problem

Adding a "Save and view" button to the editor produced an empty 400 page, and Save stopped working
with it. Two independent defects met at the same response:

1. **The command never arrived.** The editor's form posts to `action="?"`, so a button's
   `name="cmd"` is a form field and the query string is empty. `TryGetCommand` read only
   `Request.Query`, so it returned `true` with no command; `PostCommandAsync` had branches for the
   literal strings `"save"` and `"save-and-view"`, matched neither, and fell through to
   `StatusCode = 400` with no content type and no body. Save had worked only because the old code
   treated a missing command as save.
2. **The markup was not markup.** The new button ended with `</save>` rather than `</button>`. An
   unknown end tag with `button` current is ignored, so the button stayed open: its label was empty
   and the sign-out form and `<script>` were parsed inside it.

The blank 400 was not unique to the editor. Every human surface in the shell could answer with a
status and no body — the editor, the admin restore actions, the login form's antiforgery rejection,
the access-denied event, the rate limiter, routing's own 404/405 — and the page 404, 400, 413 and
504 answers were `text/plain`.

## Fix

- `EditorEndpoint.ReadCommandAsync` reads `cmd` from the form for a POST and from the query for a
  link, rejects a request that names two different actions, and allows `save` and `save-and-view`.
  A POST with no command is still a save, so cached pages and an implicit submission keep working.
- `save-and-view` redirects to the page only when the save compiled; with errors it returns the
  author to the editor and the flash says why, because the page itself would answer 500.
- `</save>` became `</button>`, and the editor's nav labels became "Page list" / "View file".
- `Errors/ErrorResponses.cs` writes every failure once: the document shell for a browser, an RFC 9457
  problem document for a program, chosen by `Accept`. Both forms name the same code, so a person's
  report and a program's log agree. `UseStatusCodePages` is the guarantee for any
  status no endpoint described, and the recoverable cases recover themselves — a rejected sign-in
  form is re-rendered with a fresh token.

## Verification

`make test`: the save, save-and-view, redirect and flash cases in `Web.Tests` and
`Web.IntegrationTests`; `ErrorPageTests` covers a missing page, an owner's create link, an
anonymous reader not being offered the editor, a problem document for `Accept: application/json`,
an unknown editor action, and a refused form that promises nothing was saved.
