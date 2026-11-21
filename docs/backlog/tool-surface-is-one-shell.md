<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant's tool surface is one shell, one question and one memory

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | blocked |
| Blocked by | `check-without-storing`, `cannot-search-the-store`, `a-page-cannot-list-folders` |
| Plan | [assistant-shell-tool](../plans/assistant-shell-tool.md) |
| Area | Core (Shell, Assistant), Shell, Web (assistant, site), docs, build |
| Location | `src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs`, `src/uk.osric.asp.Web/Assistant/Shell/`, `src/uk.osric.asp.Core/Shell/`, `src/uk.osric.asp.Shell/` |
| Spike | — |

## Idea

The registry holds twenty tools, and a contract test pins the count at the soft ceiling the
provider guidance quotes. Eleven commands a model already knows by another name sit inside `shell`:
`ls`, `cat`, `grep`, `find`, `head`, `tail`, `diff`, `stat`, `wc`, `mv` and `help`. Beside it,
`list_pages`, `read_page`, `search_pages`, `diff_page`, `move_page`, `list_revisions`, `check` and
`restore_revision` exist again as typed tools. `tool-arguments-say-nothing` found the same pressure
from the other side — every schema a paragraph of prose in the provider's cached prefix — and closed
with the closed `enum` and the described arguments that one check needed.

The shell is the better shape. A model has seen `grep -rn`, `cat -n` and `diff -u` more often than it
has seen this site's tool schemas, and one tool with a grammar can carry a dozen capabilities without
spending a slot each. What it needs is to be a real shell: POSIX commands, pipes, redirects, a
scratch directory, and history as paths rather than three more tools.

## Sketch

1. **One shell tool, and two more.** `shell`, `ask_user` and `memory`. Everything with an obvious
   POSIX equivalent is a command, not a tool.
2. **The commands are POSIX.** POSIX.1-2008 plus the GNU options models emit; a flag that is not
   implemented is refused by name, never ignored silently.
3. **Composition.** `|`, `>`, `>>`, `<`, `2>`, `&&`, `||`, `;`, `$?`, globs, quoting and heredocs.
4. **A scratch mount.** `/tmp` is per run, in memory, bounded and never persisted, so a draft costs
   no revision.
5. **History as a mount.** `/snapshots/<key>/<hash>` makes `ls`, `cat`, `diff` and `cp` answer what
   three history tools answer today; an edit is a patch that carries the hash it was made from.
6. **Objects as a read-only mount.** `/o/<sha>` answers a page's content-addressed bytes — the same
   rows the site's `/o/{sha}` route serves — so `ls -l /o` says what a page's images are, `stat` says
   the content type and the size, and `cat` reads a text object while refusing a binary one by its
   content type rather than printing bytes into the transcript. Read-only, because an object is named
   by its hash and writing one is not a path operation. `/o` is reserved by the site's route table
   because the site answers the route, so the mount needs no reservation of its own, and the bytes are
   read through the subject handles [`storage-is-not-one-model`](storage-is-not-one-model.md) adds.
7. **Memory stays a tool.** Kinds, versions, pinning, caps and FTS5 recall are too much to shoehorn
   into files; one action-dispatched tool replaces four.
8. **A write is planned before it is approved.** The kernel resolves every operand and classifies
   every effect, so one approval covers the line and the changed-pages card still knows what changed.
9. **Built from the page capabilities.** An applet reaches the store through `IPageCatalog` and
   `IPageWrites`; a command that cannot be built is a gap in the page contract, not a private seam.
   The three gaps it starts with are the three items that block it.

[The plan](../plans/assistant-shell-tool.md) carries the detail, the phases and the risks.

## Not in scope

- **A POSTing view.** [`view-cannot-submit-a-form`](view-cannot-submit-a-form.md) is its own security
  problem: the run's token reads and never writes, and that is what lets a view run unapproved.
- **The terminal.** [`terminal-cannot-reach-the-shell`](terminal-cannot-reach-the-shell.md) is the
  owner's own way into the same kernel.
- **`mv`.** [`page-cannot-move`](closed.md) has landed; the shell's applet is already there, and this
  item removes the typed `move_page` beside it.
- **More than one page write per call.** The page transaction landed on 2026-11-20
  (`pages-writes-are-transactions`), so a plan that names two pages is one commit; v1 still refuses one
  until this item's shell is built on it.
- **A page that runs the shell.** The shell is host code and a page still cannot run a command; what
  changes is that the shell is built from the capabilities a page is offered, so the two cannot drift
  apart.
