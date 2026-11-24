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
| Blocked by | `a-page-has-no-size` |
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
6. **Memory stays a tool.** Kinds, versions, pinning, limits and FTS5 recall are too much to shoehorn
   into files; one action-dispatched tool replaces four.
7. **A write is planned before it is approved.** The kernel resolves every operand and classifies
   every effect, so one approval covers the line and the changed-pages card still knows what changed.
   A line that writes two pages opens one transaction and commits once.
8. **Built from the page capabilities.** An applet reaches the store through `IPageCatalog` and
   `IPageWrites`, and the site's facts through `ISiteInfo`; a command that cannot be built is a gap in
   the page contract, not a private seam. The gaps it started with have closed: a page can search the
   source now ([`cannot-search-the-store`](closed.md)), so `grep` is a command this plan builds on
   that scan, and `check-without-storing` landed the capability beside the one `check` tool. Nothing
   waits for a page capability except the stored size a `stat` reports, which is
   [`a-page-has-no-size`](a-page-has-no-size.md); the commands are the port's work.

[The plan](../plans/assistant-shell-tool.md) carries the detail, the phases and the risks.

## Not in scope

- **Objects as a path.** `/o` stays a route with mode `000`: `IObjectStore` is bound to one subject
  and cannot list, so a mount needs a capability that does not exist yet, and
  [`object-access-is-only-its-hash`](object-access-is-only-its-hash.md) is still deciding who may read
  a page's objects. [`objects-cannot-be-read-back`](objects-cannot-be-read-back.md) carries it.
- **A POSTing view.** [`view-cannot-submit-a-form`](view-cannot-submit-a-form.md) is its own security
  problem: the run's token reads and never writes, and that is what lets a view run unapproved.
- **The terminal.** [`terminal-cannot-reach-the-shell`](terminal-cannot-reach-the-shell.md) is the
  owner's own way into the same kernel.
- **`mv`.** [`page-cannot-move`](closed.md) has landed; the shell's applet is already there, and this
  item removes the typed `move_page` beside it.
- **A page that runs the shell.** The shell is host code and a page still cannot run a command; what
  changes is that the shell is built from the capabilities a page is offered, so the two cannot drift
  apart.
