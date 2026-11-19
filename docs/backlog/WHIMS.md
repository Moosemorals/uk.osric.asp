<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Whims

Ideas too small or too vague to be an item. One or two lines each, with no difficulty, area or plan.
A whim that grows a shape is promoted to an item file and its line is removed; deleting one is free.

| Slug | Whim |
| --- | --- |
| `whim-lsp-for-the-editor` | Language Server Protocol support for the editor: publish the C# compile diagnostics and the script checker's JavaScript diagnostics to [`idea-editor-is-a-textarea`](idea-editor-is-a-textarea.md)'s editor over LSP. |
| `whim-backlog-on-the-site` | The backlog is not on the site: `docs/backlog` and `docs/plans` live in the repository, and the assistant reads the page store, so it cannot see what is already queued and proposes work that is already an item or a plan. A `/backlog` page — the item list rendered from those files by the build and seeded like any other, or written by hand and kept honest — would let the assistant ask "is this already known?" and let the owner share the list with it. |
| `whim-build-timings` | The build reports no timings: `dotnet format`, `make check`, each test suite, `make image` and the ssh deploy steps are measured only by whoever watches the terminal. Record each duration during the build and push it as a JSON blob to an endpoint that updates the site's Prometheus metrics, so the trends are visible on `/status`. |
