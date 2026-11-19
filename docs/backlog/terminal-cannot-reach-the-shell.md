<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A person cannot type into the shell the assistant uses

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Core (Shell), Web (assistant, pages), docs |
| Location | `src/uk.osric.asp.Web/Assistant/Shell/`, `src/uk.osric.asp.Core/Shell/`, `src/uk.osric.asp.Web/Pages/Seeds/` |
| Spike | — |

## Idea

The shell is the assistant's tool: one command line over the page store, `/snapshots`, `/tmp` and the
site's own files, with the same path policy and the same revisions underneath. The owner would like to
use it too — a terminal in a page, signed in as the owner, typing the same commands.

The kernel is already the right shape for it. An applet takes a `ShellContext` with an input, an
output, the mounts, the registry and the options, and writes to the output rather than returning a
tool result ([`ShellApplet`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellApplet.cs)); the kernel
is one pipeline, not one tool call. What is missing is the other end: a transport, a screen, a line
editor, and a full-screen mode for the commands that want one.

`vi`/`vim` is the hard part, and the work is started: `/vim.js` already holds a modal keymap for the
editor, and a shell `vi` can be an applet with the same keymap over `/tmp` and page keys. It does not
have to be a faithful Vim; it has to be the subset a person types to change a line and leave.

## Sketch

1. **A transport, one owner.** A WebSocket or an SSE stream under the reserved `/admin` or a new
   reserved key, authenticated as the owner, bounded per connection. Not a page capability.
2. **A screen, client-side.** A seeded terminal asset that renders the output, sends a line, and
   handles the line editor; no npm, native ES modules, the same rule as every other seeded script.
3. **Line mode first.** `ls`, `cat`, `grep`, `diff`, `patch`, `man`, `check` and the redirects work as
   they do for the assistant; a command's output is the same bounded text.
4. **Full-screen mode next.** An applet that asks for the whole screen, not a line: a mode the kernel
   knows, so `vi` can take the keyboard and hand it back.
5. **`vi`, not Vim.** Motion, insert, delete, write and quit, over the same keymap `/vim.js` uses;
   the gap between it and Vim is stated in its `help` notes.
6. **One policy.** The same path policy, the same revisions, the same approval for a write: the
   owner typing is not a second writer, and a page the shell does not route is still the only thing
   an edit can reach.
7. **The assistant's path stays first.** The kernel's outcome and the tool result's shape are
   unchanged; the terminal is a second caller, not a second kernel.

## Not in scope

- **A general SSH server or a shell on the machine.** The shell reads the page store and three mounts; it never
  reaches the host filesystem or a process.
- **Multiple users.** One owner, one terminal, the same rule the assistant has.
- **A faithful Vim.** The subset a person uses to edit a page, with its limits in `help`.
