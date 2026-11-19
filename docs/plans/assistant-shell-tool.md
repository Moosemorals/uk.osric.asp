<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant's shell is the tool

| | |
| --- | --- |
| Status | Proposed 2026-11-19 — the owner set the shape and asked for this plan. Nothing below is built; it waits for a yes |
| Size | Large — a shell kernel, a mount table, a planner, a rewritten applet set, the shell tool rebuilt and the memory tool reshaped, the prompt, and the removal of nineteen tools. The largest change to the assistant since it landed |
| Area | Core (Shell, Assistant), Web (assistant, site), docs |
| Spike | None. The claims about today's shell were read off `trunk` on 2026-11-19: [`ShellRunner`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellRunner.cs), the eleven applets, [`ShellApplet`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellApplet.cs), [`ShellTool`](../../src/uk.osric.asp.Web/Assistant/Tools/ShellTool.cs), [`PageStoreReader`](../../src/uk.osric.asp.Web/Assistant/PageStoreReader.cs) and [`PagePathPolicy`](../../src/uk.osric.asp.Web/Assistant/PagePathPolicy.cs). The tool-count evidence is [the ask tool's shape](../spikes/ask-tool-shape.md) |
| Backlog | [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) is this plan; [`cannot-ask-the-owner`](../backlog/closed.md) was a dependency and has landed; [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md) is superseded; [`cannot-check-a-page`](../backlog/cannot-check-a-page.md), [`nothing-checks-page-assets`](../backlog/nothing-checks-page-assets.md), [`one-write-per-render`](../backlog/one-write-per-render.md) changes what the shell can do when it lands, and [`page-cannot-move`](../backlog/closed.md) has landed, so `mv` is already an applet; [`view-cannot-submit-a-form`](../backlog/view-cannot-submit-a-form.md) and [`terminal-cannot-reach-the-shell`](../backlog/terminal-cannot-reach-the-shell.md) are named here and are not this plan |
| Depends on | `ask_user` and `mv` have landed ([the ask plan](archive/assistant-ask-owner.md), [the move plan](archive/page-move.md)); [`check-without-storing`](../backlog/check-without-storing.md) for `check -l template` |
| Decisions | New names proposed: **code-a-shell-is-the-interpreter**, **assistant-the-shell-is-the-tool**, **assistant-a-command-is-posix**, **assistant-a-write-is-planned-before-it-approves**, **assistant-scratch-is-not-persistent**, **assistant-a-snapshot-is-a-path**, **assistant-patch-carries-the-hash**, **assistant-memory-is-one-tool** and **assistant-man-is-the-reference**; **assistant-a-check-names-its-language** is [`cannot-check-a-page`](../backlog/cannot-check-a-page.md)'s proposed name and is taken here. Relies on `assistant-a-tool-is-one-type`, `assistant-the-shell-is-applets`, `assistant-the-shell-lexes-before-it-judges`, `assistant-the-shell-is-posix`, `assistant-writes-are-revisions`, `assistant-edit-page-is-safe`, `assistant-reads-history`, `assistant-script-checks`, `assistant-reads-the-shell-files`, `assistant-memory-is-the-owners`, `assistant-memory-recall-is-fts5`, `assistant-memory-page`, `assistant-a-view-is-a-read`, `assistant-a-view-is-the-response`, `assistant-tool-results-are-cut-not-refused`, `assistant-old-tool-results-are-masked`, `pages-the-store-is-read-through-one-seam`, `pages-outbound-fetch`, `auth-a-view-token-reads-only` and `code-policy-values-are-configuration`. Edits `assistant-the-shell-is-posix` in place to name the conformance target |

## The short version

The registry holds twenty-two tools and a soft ceiling it already quotes against itself, and eleven
commands already sit inside one `shell` tool: `ls`, `cat`, `grep`, `find`, `head`, `tail`, `diff`,
`stat`, `wc`, `mv` and `help`. Beside it, `list_pages`, `read_page`, `search_pages`, `diff_page`,
`list_revisions`, `read_revision`, `move_page` and the facts `stat` and `head`/`tail` already answer
exist again as typed tools. Two check tools are one validator split by where the text came from. The
prose in [the assistant](../assistant.md:26) says nineteen while the registry holds twenty-two.

This plan makes the shell the tool. It ends with **three** capabilities:

| Tool | What it is |
| --- | --- |
| `shell` | one POSIX-shaped command line over a tree: the page store, `/snapshots`, `/tmp`, the site's own routes, `man`, `check`, and `curl` when the security review lands |
| `ask_user` | the question that waits on the owner, from [the ask plan](archive/assistant-ask-owner.md) |
| `memory` | one tool over the assistant's notes, with an action, because a file cannot carry a kind, a version, a pin and FTS5 without re-inventing all four |

That is the end state. The interim state is five: `fetch` and `view` stay typed until `curl` has a
security review, because the read-only view token is what makes an unapproved view safe.

Five things are load-bearing:

1. **The shell is the preferred surface.** A tool that has an obvious POSIX equivalent is not a tool;
   it is a command. The exceptions are the ones that cannot be honest as a command: a wait
   (`ask_user`) and a store whose records are not lines (`memory`).
2. **The commands are POSIX, not shell-flavoured.** The target is POSIX.1-2008 plus the GNU options
   models actually emit, and a flag nobody implements is refused by name. A flag that is silently
   ignored is worse than a refusal, because the model cannot tell the difference between "no matches"
   and "I did not understand `-v`".
3. **Pipes and redirects are the point.** Without them the shell is twenty-two tools with a grammar
   and no composition; with them the model splits a read before it becomes a tool result, writes a draft
   to `/tmp`, and diffs against a snapshot.
4. **A write is planned before it is approved.** A single `shell` tool cannot carry `NeedsApproval`
   per call, so the kernel resolves every operand, classifies every effect, and the approval is the
   owner answering for the effects rather than for the line.
5. **The store's guarantees do not move.** A page write is still `PageEditor`'s, still one revision,
   still compiled before it is saved, still path-policy checked, still listed in the changed-pages
   card (`assistant-writes-are-revisions`, `assistant-edit-page-is-safe`).

### What the owner decided

- The shell is preferred, and commands are POSIX-true. **Shell** means this interpreter and nothing
  else; the site's own routes and views are the **site's** (D0).
- Pipes, redirects and a scratch `/tmp` are in scope; `/tmp`'s lifetime and bounds are refined below.
- Memory stays a tool; it is too big to shoehorn into files.
- A `view` that POSTs is its own problem, filed as [`view-cannot-submit-a-form`](../backlog/view-cannot-submit-a-form.md).
- `mv` has landed ([the move plan](archive/page-move.md)); the shell's applet stays and the typed
  `move_page` goes with the rest.
- A terminal emulator so the owner can use the shell is wanted later; filed as
  [`terminal-cannot-reach-the-shell`](../backlog/terminal-cannot-reach-the-shell.md), and this plan
  only refuses to make it harder.

## What exists when this starts

- **The registry and its ceiling.** [`ToolRegistry.Default`](../../src/uk.osric.asp.Web/Assistant/Tools/ToolRegistry.cs:38)
  holds twenty-two tools, `ask_user` (with `WaitsForOwner` on `ToolSpec`) and `mv` having landed on
  2026-11-19; [`TheRegistryHoldsEveryTool`](../../test/uk.osric.asp.Web.Tests/ToolSpecsTests.cs:71)
  pins the count; the spike called the twenty it read "already over OpenAI's soft limit", and there
  are twenty-two now.
- **A shell with eleven applets and no composition.** `ShellRunner` registers `cat`, `diff`, `find`,
  `grep`, `head`, `help`, `ls`, `mv`, `stat`, `tail` and `wc`; it refuses pipes, redirects, separators and
  background jobs as "not there yet" ([`ShellRunner.cs:109`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellRunner.cs:109)).
  It is read-only, and a save to a site file answers "Not permitted".
- **A lexer that already quotes.** `assistant-the-shell-lexes-before-it-judges` holds: quoting,
  escapes and the first operator are read before any command judges its words
  ([`ShellLine`](../../src/uk.osric.asp.Core/Shell/ShellLine.cs)).
- **Applets that are one type each.** `assistant-the-shell-is-applets` holds: usage, description,
  notes, syntax and behaviour live on the applet, and `help` renders them
  ([`ShellApplet`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellApplet.cs)).
- **Departures from POSIX.** `grep` always prints `key:line: text` and makes `-n` a no-op
  ([`GrepApplet.cs:21`](../../src/uk.osric.asp.Web/Assistant/Shell/Applets/GrepApplet.cs:21)); `cat -n`
  restarts numbering for each key ([`CatApplet.cs:37`](../../src/uk.osric.asp.Web/Assistant/Shell/Applets/CatApplet.cs:37));
  `ls` has `-l`, `-R` and `-r` only; `wc` has `-l` only; `diff` has no `-c`, `-q` or `-i`; `find`
  has no `-print`.
- **The read seam and the policy.** `PageStoreReader` answers the tree, sources, one key, a history
  and one revision; `PagePathPolicy` refuses a reserved or unwritable path before the owner is asked.
- **The safety net around a write.** `PageEditor` saves one revision, compiles it, and refuses a
  no-op; the transcript's changed-pages card reads the result's write facts.
- **The view.** `ViewGuard` refuses anything but `GET`/`HEAD` with `405` and `Allow: GET, HEAD`
  ([`ViewGuard.cs:57`](../../src/uk.osric.asp.Web/Authentication/ViewGuard.cs:57));
  `auth-a-view-token-reads-only` is why `view` needs no approval.
- **The prompt.** `PageAuthorPrompt.Version` is `page-author-v44`.

## Goals

1. The model is offered three tools, and every capability it loses has a command that behaves the way
   the model already expects that command to behave.
2. A command's accepted options are the POSIX ones plus the GNU options models emit; everything else
   is refused by name, and `help <command>` is the single list that cannot drift from the applet.
3. A line composes: `|`, `>`, `>>`, `<`, `2>`, `&&`, `||`, `;`, `$?`, globs, quoting and heredocs.
4. `/tmp` is scratch a run may write without touching the store, bounded, per run and never persisted.
5. `/snapshots` makes history a path, so `ls`, `cat`, `diff` and `cp` answer what three typed tools
   answer today, and a restore is `cp` from a revision.
6. An edit is a patch made against a named revision, so the atomic check `edit_page` has survives the
   move to a command.
7. Every write still goes through `PageEditor`, every result is still bounded and cut rather than
   refused, and the approval card still shows what will change.
8. The terminal the owner wants later reads the same kernel through a stdin and an output; an applet
   never learns it is talking to a model.

## Non-goals

- **A POSTing view.** It is [`view-cannot-submit-a-form`](../backlog/view-cannot-submit-a-form.md),
  and it is a security decision before it is a tool: the run's token reads and never writes, and that
  is what lets a view run unapproved.
- **`mv`.** [The page move plan](archive/page-move.md) has landed it; this plan keeps the applet and
  removes the typed tool beside it, and does not change the move itself.
- **Memory as files.** The owner's answer, and the record's: kinds, versions, pinning, caps and FTS5
  recall are five things a directory does not carry.
- **The terminal UI.** [`terminal-cannot-reach-the-shell`](../backlog/terminal-cannot-reach-the-shell.md)
  owns the transport, the page and `vi`.
- **Background jobs, subshells, command substitution, variable assignment and job control.** Refused
  as "not there yet", the way separators are refused today.
- **Full POSIX.** The target is the options models emit; a command that accepts all of POSIX is a
  much larger promise than this change needs.
- **More than one page write per call.** [`one-write-per-render`](../backlog/one-write-per-render.md)
  is the blocker, and v1 refuses a plan that names two pages.
- **A page capability.** The shell is host code for the assistant. A page that runs shell commands is
  a different decision and a different threat model.
- **A provider change.** One tool with one string argument is the shape code-interpreter tools already
  have, so the existing OpenAI-compatible endpoint is unchanged.

## The design

### D0 — The words: a shell is the interpreter, the site is the site

Two things have shared one word. The **shell** is the assistant's fake command interpreter: the
kernel, the applets and the `shell` tool, under `Core/Shell` and `Web/Assistant/Shell`. The **site's
code** is everything the site answers itself: `/login`, `/metrics`, the admin routes, the document
their views are served in, and the menus built from them.

The rename is mechanical and mostly `git mv`:

| Today | Becomes |
| --- | --- |
| `Web/Shell/ShellCatalogue` | `Web/Site/SiteCatalogue` |
| `ShellRoute`, `ShellEntry`, `ShellMenu`, `ShellSection`, `ShellAudience`, `ShellMatch`, `ShellPage` | `SiteRoute`, `SiteEntry`, `SiteMenu`, `SiteSection`, `SiteAudience`, `SiteMatch`, `SitePage` |
| `Html/ShellDocument` | `Html/SiteDocument` |
| `ShellLink`, `IPageCatalog.ShellLinks()` | `SiteLink`, `IPageCatalog.SiteLinks()` |
| `body.shell`, `ShellDocument.ShellClass` | `body.site`, `SiteDocument.SiteClass` |
| "shell file", "shell page", "shell route", "shell document" in prose | "site file", "site page", "site route", "site document" |
| `docs/shell.md` | `docs/site.md` |
| `ShellDocumentTests`, `ShellDocumentFragmentTests`, `ShellCatalogueTests`, `ShellMenusTests`, `ShellViews`, `ShellMemoryStore` | `SiteDocumentTests`, `SiteDocumentFragmentTests`, `SiteCatalogueTests`, `SiteMenusTests`, `SiteViews`, `SiteMemoryStore` |
| "functional core, imperative shell" in [the architecture](../architecture.md) | "functional core, the site's code" |

Three things do **not** move:

- **The `shell` decision area and the names beginning `shell-`.** A decision name is permanent, and
  the area is part of its name; they are names rather than prose, and the glossary says so.
- **`Core/Shell` and `Web/Assistant/Shell`.** That is the interpreter, the one sense the word keeps.
- **Archived plans and the frozen record.** They are history; rewriting them would falsify it.

The new name is **code-a-shell-is-the-interpreter**: the word names one thing, and a name already in
the record keeps its history rather than being disambiguated with a suffix.

### D1 — Three tools, and the registry is still the only list

`ToolRegistry.Default` becomes `shell`, `ask_user`, `memory`, in that order. `assistant-a-tool-is-one-type`
is unchanged: a tool is still one type carrying its name, description, schema and the code that runs
it. What changes is that `NeedsApproval` is no longer a boolean on the type: a capability whose
approval depends on its arguments declares a verdict instead. `WaitsForOwner`, which `ask_user`
added, asks a different question — whether the call stops for the owner at all — and stays as it is.

- `shell`: the kernel decides from the line's plan.
- `memory`: the three write actions are gated under `RequireApproval`, `search` is not.
- `ask_user`: never gated; a question is not an approval (`assistant-a-run-waits-on-one-owner-interrupt`).

`ToolSpecsTests` keeps its invariants and pins three.

### D2 — One tree, four mounts

| Path | Answers | Writable |
| --- | --- | --- |
| `/` | the page store's keys; a folder exists when a key lives below it | yes, through `PageEditor`, with approval |
| the site's own routes (`/login`, `/assistant`, `/admin/...`) | the site's records of their methods, access and purpose, never source | no |
| `/snapshots/<key>/<hash>` | every retained revision, including a tombstoned key's | no |
| `/tmp` | the run's scratch | yes, no approval |

`/tmp` and `/snapshots` join the site's reserved keys — `ShellCatalogue.Reserved` today,
`SiteCatalogue.Reserved` after D0 — so no page can claim either prefix, in the stored keys or in a
seed. A mount is one type — it resolves a key to a source — and the kernel asks
the mount table, not the store, so the page store never learns about `/tmp`.

Path resolution stays `PagePath`'s: a key is normalised, `IsAddressable` is required, and a mount
prefix is stripped before `PageStoreReader` sees it. A shell operand that is not a key in any mount is
refused as a missing path, not as a bad argument.

### D3 — The commands, and the conformance rule

The rule, stated once and tested per applet: **the accepted options are the ones `help` prints; every
other flag is refused by name with the accepted set in the sentence.** The target is POSIX.1-2008 plus
the GNU options models emit, because that is what the training corpus contains. `help` is rendered
from the applet registry, so the list cannot drift (`assistant-the-shell-is-applets`).

| Command | Must accept |
| --- | --- |
| `ls` | `-l -a -A -d -1 -i -s -t -r -R`; `-i` prints the revision hash; the `-l` fields are this store's own and are documented |
| `cat` | `-n -b -s -A -e -t`; `-n` numbers continuously across operands, per POSIX |
| `head`, `tail` | `-n N`, `-c N`, `tail +N` |
| `grep` | BRE by default, `-E -F -i -v -w -x -n -l -c -q -s -r -h -H -e`; POSIX framing: the line alone for one operand, `key:line:` for many or `-r`, `-n` adds the number |
| `find` | `-name -path -type -print`, and GNU's `-maxdepth -mindepth` |
| `diff` | normal format by default, `-u -U N -c -q -i -w -b -r`; a page side carries `@hash` (D6) |
| `patch` | `-p0 -R -i FILE -N`; one page target; applies to a copy, compiles, saves once |
| `cp` | `cp [-r] src dst` — a restore, a duplicate, a scratch copy |
| `rm` | `-f -r`; `-i` refused, because there is no stdin to ask on; `rm` tombstones |
| `wc` | `-l -w -c -m` |
| `stat` | this store's fields: key, mode, size, hash, saved time, compile state |
| `echo`, `printf` | for scratch files |
| `sed` | filter only; `-i` is refused with "use patch" |
| `man` | `man <type>`, `man -k <word>`; the page contract's XML documentation |
| `help` | the commands, or one command's usage and notes |
| `check` | `check [-l javascript\|template] <path\|->` — the one command with no POSIX ancestor |
| `sort`, `uniq`, `cut`, `tr` | phase 2, once pipes land; they are what makes pipes pay |
| `df` | `df -h /tmp` — what scratch is left |
| `mkdir`, `rmdir`, `touch` | refused: folders are implicit in a key tree, so these have nothing to do |

`mv` is `mv src dst`; it landed with [the move plan](archive/page-move.md), the applet stays, and the
typed `move_page` is removed here.

Long listings and `stat` keep the facts the typed tools carried — permission in the first column, the
compile state, the hash — because those facts are what `assistant-reads-the-shell-files` and
`pages-a-page-has-a-state` promise. What goes is the `key:line: text` framing `grep` invented and the
per-file counter `cat -n` invented; the prompt teaches `grep -n` and a single operand when a number is
wanted.

### D4 — The kernel composes

`ShellLine` grows from "words and the first operator" to a pipeline: stages joined by `|`, each with
its own redirects, and lists joined by `&&`, `||` and `;`. The kernel runs a stage with an input and
an output, the way `ShellOutput`'s comment already anticipates
([`ShellOutput.cs:10`](../../src/uk.osric.asp.Web/Assistant/Shell/ShellOutput.cs:10)), and an applet
still knows nothing about where its output goes.

- `>` truncates a file, `>>` appends, `<` reads, `2>` redirects stderr.
- A heredoc (`<<'EOF'`) is a whole-file write and the write path for a page that is short enough to
  retype.
- `&&` continues on success, `||` on failure, `;` always; `$?` is the last status.
- Globs (`*`, `?`, `[abc]`) expand in an operand and nowhere else.
- `$(...)`, backticks and assignment are refused as "not there yet", because there is no expansion
  and the refusal names the missing thing rather than the punctuation.
- Exit status: `0` success, `1` no match, `2` usage, `126` refused by policy, `127` not a command. A
  policy refusal is a tool **failure**; a command that ran and found nothing is a successful call
  with a non-zero `exitCode`.
- Every stage's output is bounded, and the pipeline's total is bounded; past a bound the result is
  cut and `truncated` says so (`assistant-tool-results-are-cut-not-refused`).

### D5 — A write is planned before it is approved

The kernel does not execute a line before it has resolved it. `ShellPlan` is the result:

```
line → stages → words → expanded operands → effects
effects = Read(path) | Write(path, kind) | Network(url)
```

The planner lives in `Web` because it reads the tree; `Core` keeps the grammar and the classification
of a word into a path or a literal. Then:

1. `PagePathPolicy` answers for every page write first. A reserved or unwritable path refuses before
   the owner is asked, as it does today.
2. One approval covers the line, and its card lists the **effects**: path, verb, and a diff preview
   for a patch or a whole write under `Assistant:ApprovalPreviewChars`.
3. `AlwaysApprove` still plans — for the changed-pages card and for the policy — it simply does not
   wait.
4. `RequireApproval` waits once per call, not once per write. The run still waits on one thing at a
   time (`assistant-a-run-waits-on-one-owner-interrupt`).
5. **v1 writes at most one page per call.** A plan that names two refuses and names
   [`one-write-per-render`](../backlog/one-write-per-render.md). The result's `writes` is an array so
   the day the blocker lands is a removal of the refusal, not a shape change.
6. A write that would leave the source byte-identical refuses and stores nothing
   (`assistant-edit-page-is-safe`).

The planner is not the authority. Every page write still goes through `PageEditor`, which checks the
policy and compiles the source; a planner bug can produce a wrong card or an unnecessary prompt, not
an unauthorised write.

### D6 — A patch carries the hash it was made from

`edit_page` is safe because it names the revision it edited. A command has no place for that
argument, so it goes where a patch already has one: the header.

```
--- a/notes@9f2c1a
+++ b/notes
```

`diff` writes that header for a page side, and `patch` reads it. A page target without `@hash` is
refused and points at `stat`; a hash that is not the live head refuses with both hashes; a hash on a
`/tmp` target is not required. The patch applies to a copy, compiles, and saves once, so a rejected
patch leaves the store untouched.

`cp /snapshots/notes/9f2c1a /notes` is the restore; it refuses when the live page already holds that
source. A `rm` is a tombstone, not a scrub; there is still no scrub tool.

### D7 — `/tmp` is scratch, and it is not a store

- **Per run, in memory.** Created when the run starts, gone when it ends, kept across a budget pause
  that continues the same run, lost on a restart.
- **Reserved.** `/tmp` is a prefix no page may claim, so a scratch file can never shadow a page.
- **Bounded.** `Assistant:ScratchMaxBytes` and `Assistant:ScratchMaxFiles`, both required; a write
  past either fails with the setting named. `df -h /tmp` reports the remainder.
- **Invisible.** Never served, never rendered, never exported, never in the changed-pages card, and
  never listed by a page's catalogue.
- **Unapproved.** A write under `/tmp` is not a store write, so it neither asks the owner nor counts
  as a change.

The model gets a place to build a diff, sort a list, or keep a draft across steps without spending a
revision on it. What it does not get is a place to leave something for the next run: the next run
starts empty, and the prompt says so.

### D8 — `/snapshots` is history as paths

`ls -l /snapshots/notes` lists the revisions newest first, with hash, save time, compile state and
head mark; `ls -l /snapshots` lists every key that has one; `cat` reads one; `diff` compares two or a
revision against the live page; `cp` restores. That is `list_revisions`, `read_revision` and
`restore_revision`, in commands the model already knows, and `assistant-reads-history` keeps its
promise: the same reads, the same refusals, the same hash rules.

`cat` on a snapshot numbers physical lines with `-n`, so `assistant-line-numbers-are-physical` still
holds; a line range is `head`/`tail`/`sed -n` and no longer an argument to a tool.

### D9 — `man`, `help` and `check`

`man` reads the page contract's XML documentation — the corpus `reference` reads today — and answers
a type's summary, remarks and members. `man -k cookie` searches names and summaries. It reads
`uk.osric.asp.Sdk` only, and refuses a type from another assembly by name
(`shell-the-reference`). The human page at `/admin/reference` is unchanged.

`help` is the command list and one command's usage; it is rendered from the applet registry, so a
command that is not in the registry cannot be in the help.

`check -l javascript <path|->` is today's two script tools, one command; `check -l template` calls
`check-without-storing`'s compile when it lands, and answers its diagnostics. The language set is
closed and `help check` names it; a language with no checker is refused by name. The proposed name
**assistant-a-check-names-its-language** is taken here.

### D10 — Memory stays a tool, and becomes one

One tool, `memory`, with an `action`:

| Action | Arguments | Approval |
| --- | --- | --- |
| `remember` | `kind`, `content` | gated under `RequireApproval` |
| `update` | `id`, `expectedVersion`, `content` | gated |
| `forget` | `id` | gated |
| `search` | `query`, `limit` | none |

The owner is the signed-in conversation's, never an argument (`assistant-memory-is-the-owners`). The
cap is `Assistant:MaxMemoriesPerOwner`; the refusal names it. Pinning stays the memory page's, and no
action has an argument for it (`assistant-memory-page`). Recall is unchanged: FTS5, once per run, on
the first step, frozen for the rest of it (`assistant-memory-recall-is-fts5`).

The store and the page are unchanged. What changes is the surface: four tools become one, and the
approval gate learns that an action can decide whether a call asks.

### D11 — The network waits for its own review

`curl` is the command the shell should end with:

- to an absolute `http`/`https` URL on another host, anonymous and approved, as `fetch` is;
- to a path on `Site:Origin`, as the run's owner with the read token, unapproved, as `view` is;
- `-X GET|HEAD` only in this plan; `-X POST` is the item;
- the response is the transport's own: status, every header, the body, redirects (`assistant-a-view-is-the-response`).

That is one command with two threat models, and the gate decides by host. It is also the place a token
could leak: a cross-origin redirect must not carry it, and the method decides whether approval is
asked. So `fetch` and `view` stay typed until a security review of that split lands. The interim is
five tools; the end state is three.

### D12 — The result and the transcript

```json
{
  "exitCode": 0,
  "stdout": "…",
  "stderr": "",
  "truncated": false,
  "writes": [{ "path": "/notes", "hash": "…", "kind": "edit" }]
}
```

The model reads `exitCode`, `stdout` and `stderr`; a refusal is a tool failure with the accepted form
in it. The transcript's changed-pages card reads `writes`, so
`assistant-writes-are-revisions` survives a write that happened inside a pipeline. Old shell results
are masked like any other tool result (`assistant-old-tool-results-are-masked`), and the result is cut
at the tool cap rather than refused (`assistant-tool-results-are-cut-not-refused`).

### D13 — Discovery and the prompt

The shell's description is the cached prefix's cost, so it is one paragraph: the commands that pay,
the fact that it is POSIX-shaped, and "run help". The prompt gains a **Working the shell** section
with four idioms, and moves to `page-author-v45`:

1. read one page with `cat -n`, a tree with `ls -R` or `find`, a phrase with `grep -rn`;
2. pipe before you read: `cat /notes | head -40`, `grep -rn cookie / | wc -l`;
3. edit with `stat`, then `diff`/`patch` against the hash; write a short page whole with a heredoc;
4. keep a draft in `/tmp`, and compare against `/snapshots`.

A refusal names the accepted form and `help`, so the accepted form is one call away. The prompt says
that a flag the shell does not know is refused rather than ignored, which is the one thing a model
that has used a real shell may not expect.

### D14 — The terminal is a client, not a rewrite

An applet takes a `ShellContext`: mounts, input, output, registry, options and budget. It must not
take an `AssistantTool`, an `HttpContext`, an owner or a tool result. The kernel returns an outcome
with an exit status, so a terminal can wrap the same kernel with a WebSocket or an SSE stream, a page
asset and a screen, and `vi`/`vim` can be an applet or a client-side editor over the same keymap
`/vim.js` already holds. This plan does not build that; it only keeps it possible.

## What it touches

| Where | What changes |
| --- | --- |
| `src/uk.osric.asp.Core/Shell/` | `ShellLine` grows a pipeline and lists; a heredoc; `$?`; exit-status values. No I/O |
| `src/uk.osric.asp.Web/Assistant/Shell/` | the kernel, the mount table, the planner, the rewritten applets, `/tmp` and `/snapshots` |
| `src/uk.osric.asp.Web/Assistant/Tools/` | `ShellTool`; `MemoryTool` replaces four; `fetch`/`view` stay for now; nineteen tools deleted or replaced |
| `src/uk.osric.asp.Web/Assistant/ToolRegistry.cs`, `ToolSpec.cs` | three tools; `NeedsApproval` becomes a per-call verdict |
| `src/uk.osric.asp.Web/Assistant/PagePathPolicy.cs`, `PageEditor` | unchanged in authority; the planner calls the policy first |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | `page-author-v45`, the shell section |
| `src/uk.osric.asp.Web/Shell/`, `src/uk.osric.asp.Web/Html/ShellDocument.cs`, `src/uk.osric.asp.Sdk/IPageCatalog.cs` | D0's rename: `SiteCatalogue`, `SiteDocument`, `SiteLink`, `SiteLinks()` |
| `src/uk.osric.asp.Web/Configuration/` | `Assistant:ScratchMaxBytes`, `Assistant:ScratchMaxFiles`, `Assistant:ShellMaxStages`, `Assistant:ShellMaxPipelineBytes`, `Assistant:ApprovalPreviewChars` |
| `docs/assistant.md`, `docs/site.md`, `docs/glossary.md`, `docs/architecture.md` | the tool list, the mounts, the planner, the result shape, and D0's words |
| `docs/decisions.md` | the eight new names and the edit to `assistant-the-shell-is-posix` |
| `docs/backlog/` | this item, two new items and the dispositions below |
| `test/…Web.Tests/` | the applets, the kernel, the planner, the mounts, the approval, the memory tool, the prompt |
| `test/…Web.IntegrationTests/` | one end-to-end: a run writes, patches and restores through the shell |
| `/help` | no change: the shell is the assistant's, not a page capability |

## Phases

Each phase is a commit or a short series, and each ends with its tests green.

0. **The words.** D0's rename: `Web/Shell` to `Web/Site`, `ShellDocument` to `SiteDocument`,
   `ShellLinks` to `SiteLinks`, the `body` class, the prose in the live documents, and `docs/shell.md`
   to `docs/site.md`. No behaviour changes; the tests move with their types. Tests: the renamed suites
   pass unchanged, and a grep for the old words in the live tree finds only the decision area, the
   `shell-` names, `Core/Shell`, `Web/Assistant/Shell` and the site's visible "Site tools" label.
1. **The kernel.** Pipeline, redirects, heredoc, lists, `$?`, bounds and exit status, with the ten
   applets unchanged. Tests: two stages, a redirect to `/tmp`, a failing stage under `&&` and `||`,
   a heredoc, an unquoted operator refused, a cut pipeline.
2. **The commands.** POSIX-fix `cat`, `grep`, `ls`, `find`, `head`, `tail`, `diff`, `wc`, `stat`;
   add `cp`, `rm`, `echo`, `printf`, `patch`, `sed`, `man`, `df`. Tests: one per documented flag
   changing output, one per undocumented flag refused.
3. **The mounts.** `/tmp` and `/snapshots`, reserved keys, the mount table, the `df` and `ls`
   answers. Tests: a page cannot claim `/tmp` or `/snapshots`; a snapshot is read-only; scratch is
   gone on the next run.
4. **The planner and the approval.** `ShellPlan`, the effect card, the path policy first, the
   one-write rule, the no-op refusal. Tests: a read never asks; a scratch write never asks; a page
   write asks once with its effects; two page writes refuse and name `one-write-per-render`.
5. **The shell tool and the removal.** `ToolRegistry` holds `shell`, `ask_user`, `memory`; the
   `ToolSpecsTests` count moves to three; the typed reads, writes, move, history, checks and reference
   are deleted. Tests: every removed capability has a command case.
6. **Memory as one tool.** The action dispatch, the per-action approval, the four actions against the
   existing stores. Tests: each action; approval under both modes; a stale version refused.
7. **The prompt, the docs and the measurement.** `page-author-v45`, the descriptions, the manual
   pages, the definitions' token count before and after, and a live run of the common tasks.
8. **`curl`.** Only after the security review: the host split, the read-token rule, the redirect
   rule, the method rule. Then `fetch` and `view` are removed and the count is three.

## Tests

- **The applets.** Every documented option changes the output; every undocumented option is refused;
  `help` prints the applet's own usage and notes.
- **The kernel.** Quoting and escapes; `*`, `?` and `[abc]`; `$?`; `&&`, `||`, `;`; a heredoc; a
  redirect to each mount; a stage that fails; a bound that cuts.
- **The mounts.** `/tmp` is per run and reserved; `/snapshots` is read-only and answers a tombstone;
  a page key that looks like a mount is refused.
- **The planner.** Reads, scratch writes, page writes, network; the policy refuses before the owner;
  the card lists effects; one write in v1.
- **The patch.** A matching hash saves once; a stale hash refuses with both; a missing hash points at
  `stat`; a no-op refuses; a rejected patch changes nothing.
- **The memory tool.** Four actions; the owner from the conversation; the version check; the cap;
  search is not gated.
- **The prompt.** It names the shell, the four idioms and the version; it does not name a deleted
  tool.
- **The count.** Three tools, and every capability the deleted tools answered is reachable through
  the shell.

## Risks

- **The planner misclassifies a write.** The highest severity, and the mitigation is structural: the
  planner is not the authority, `PageEditor` re-checks the path policy and compiles. A bug costs a
  wrong card or an unnecessary prompt, not a bad save.
- **`patch` is hard for a small model.** The live suite decides. Heredoc writes cover short pages,
  and the escape hatch is a typed `apply_patch`-shaped tool — one slot, and the measurement says
  whether it is needed.
- **A fat description replaces twenty-two clean ones.** The definitions' token count is measured before
  and after; if the shell's description and `help` cost more than the tools it removes, the answer is
  to shrink `help`, not to raise the count.
- **Step cost.** A fold that needs `help` on most tasks is a bad trade. The live suite counts steps,
  not only passes.
- **Heredocs cost tokens.** A long page written whole is expensive; the prompt says to patch.
- **Scratch pressure.** `/tmp` is RAM. The caps are required configuration and a full scratch fails
  like a full disk.
- **The cached prefix moves.** Every phase that changes the definitions invalidates it; the plan
  lands the definition changes in as few steps as it can.
- **Conformance creep.** Every new option is a row in `help` and a test. A command that grows all of
  POSIX is a different plan.
- **`check -l template` is another branch.** The applet lands with the capability, not before it; a
  missing one is refused by name in the interim.

## Backlog items and what happens to them

| Item | Disposition |
| --- | --- |
| [`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md) | this plan |
| [`tool-arguments-say-nothing`](../backlog/tool-arguments-say-nothing.md) | superseded: with three tools there is no long prefix to describe; closes at this merge |
| [`cannot-check-a-page`](../backlog/cannot-check-a-page.md) | the JavaScript half lands as `check`; the template half waits on [`check-without-storing`](../backlog/check-without-storing.md); rewritten at this merge |
| [`nothing-checks-page-assets`](../backlog/nothing-checks-page-assets.md) | grows `check`'s language set; unchanged |
| [`one-write-per-render`](../backlog/one-write-per-render.md) | v1 refuses a second page write and names this item; not closed here |
| [`page-cannot-move`](../backlog/closed.md) | landed 2026-11-19; the shell's `mv` is already there, and the typed `move_page` goes with the rest |
| [`cannot-ask-the-owner`](../backlog/closed.md) | a dependency; landed 2026-11-19 |
| [`cannot-search-the-store`](../backlog/cannot-search-the-store.md) | the page capability; the shell's `grep` already reads the same seam; unchanged |
| [`vscode-cannot-check-a-page`](../backlog/vscode-cannot-check-a-page.md) | editor-only; unchanged |
| [`editor-is-a-textarea`](../backlog/editor-is-a-textarea.md) | the terminal's `vi` may share `/vim.js`; unchanged |
| [`conversation-is-not-a-page`](../backlog/conversation-is-not-a-page.md) | the assistant's own UI; unchanged |
| [`listing-does-not-date-itself`](../backlog/listing-does-not-date-itself.md) | a shell listing is one shot; unchanged |
| [`view-cannot-submit-a-form`](../backlog/view-cannot-submit-a-form.md) | new, and not this plan |
| [`terminal-cannot-reach-the-shell`](../backlog/terminal-cannot-reach-the-shell.md) | new, and not this plan |

At the merge the record is updated in the same change: `tool-surface-is-one-shell` moves to
[closed.md](../backlog/closed.md), `tool-arguments-say-nothing` closes, `cannot-check-a-page` is
rewritten to what is left, and the order's rows and reasons follow.

## Close-out

- The nine names are written into [the record](../decisions.md) with their "Recorded in" lines, and
  `assistant-the-shell-is-posix` is edited in place to name the conformance target.
- [The assistant](../assistant.md), [the shell](../shell.md) and
  [the architecture](../architecture.md) describe three tools, the four mounts, the planner and the
  result.
- [The backlog](../backlog/README.md) carries the two new items and the dispositions above.
- `make check` is green, including `make decisions` and `make backlog`.
- `/help` is unchanged, because nothing here is a page capability.
