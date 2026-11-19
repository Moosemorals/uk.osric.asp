<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot check a template without saving it

| | |
| --- | --- |
| Difficulty | caffeinated-squirrel |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [ide-host](../plans/ide-host.md) |
| Area | Sdk, Runtime, Web (pages, editor), docs |
| Location | `src/uk.osric.asp.Sdk/IPageWrites.cs`, `src/uk.osric.asp.Sdk/PageEditResult.cs`, `src/uk.osric.asp.Web/Pages/PageWritesScope.cs`, `src/uk.osric.asp.Web/Editor/PageSaver.cs`, `src/uk.osric.asp.Web/Pages/PageImageBuilder.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

## Idea

A page's diagnostics exist only as the answer to a save (`pages-a-page-may-save-itself`). To find out
whether a template compiles, the author has to store it: one new revision, one new head, a recompile
of every page that names it as a library, and a stale form in any other window that had it open.
"Check my work" is "add a revision to the history", and the history is the one thing the store keeps
forever.

The gap is not the editor's. Nothing in the contract asks *would this compile* — the same hole runs
through every caller:

- **The editor** cannot underline a mistake while it is being made, and cannot refuse a save that will
  certainly fail.
- **The assistant** writes a page to find out whether it works: `write_page` and `edit_page` compile
  because they store, so a page the assistant is unsure of reaches the store before it is known good.
- **A bulk question** — does the whole site still compile after this library edit, which seeds moved
  under it, what would a release change — has no cheap answer, because each one is a write.

The compiler is fast enough that this is a protocol gap rather than a performance one: generation is
hundredths of a millisecond and a warm compile is 30–70 ms (`Pages:Compile:Slots` is 1 and
`Pages:Compile:TimeoutSeconds` is 5). Asking twice costs less than storing once.

## Sketch

1. **One member, one answer.** `CheckAsync(key, template)` on `IPageWrites`, answering the diagnostics
   the same save would produce and nothing else — no revision, no head, no image, no change to any
   dependent's closure. The value is a report with its diagnostics and whether any is an error, so a
   caller does not have to count severities itself.

2. **It is a compile, not a render.** Generation and Roslyn, with the libraries the template's `using`
   directives reach, exactly as a save compiles them — no load, no handler, no response. A library's
   error therefore appears in the check, which is the case that matters when the library was edited
   last.

3. **The key still decides.** A check of `/blog/new` compiles against the libraries that key can name,
   so the key is normalised and checked exactly as a save's is: reserved keys refused, an
   unaddressable key an `ArgumentException`, a `HEAD` and an ended render page bugs. Whether a check
   needs a signed-in visitor is an open question below; the safe answer is yes, because it is the same
   draft and the same capability.

4. **It costs a compile slot.** A debounced check on every pause is one compile per pause, contending
   with the saves of every other author and with lazy rebuilds. Whether a check takes a compile slot of
   its own, shares the existing one, or is refused when the queue is busy is the budget question; the
   default is to share, because `n` is small and a check that waits is a check that lies about the
   queue.

5. **The diagnostics are the value [IDEA-041](closed.md) shipped.** A check is the second consumer of
   a diagnostic that carries an extent and a source key (`pages-a-diagnostic-names-its-source`), so it
   answers with the page's own `PageDiagnostic` and teaches nothing new.

6. **The documentation** is a capability section in `/help` (`product-manual-is-a-page`), a paragraph
   in `PageAuthorPrompt` with its `Version` bumped so the assistant knows it can check before writing,
   and a line in [pages](../pages.md). The editor's wish list and its `/ide-plan` page stop naming
   "compile without saving" as the host's largest gap.

## Open questions

1. **One member or two.** `CheckAsync` on `IPageWrites` keeps one authority and one shape of answer.
   A second capability (`IPageCompiler`) would let a read-only page check a draft it never saves, which
   is the assistant's case, but it is a second way to ask the same question — the same objection the
   old registration rule raised against a handle that names a key, which the owner has since answered
   (`pages-any-page-may-write-the-store`).
2. **Whether a check is "the action".** A page that checks and then saves makes two compiles in one
   render, and `PageWritesScope` currently counts any write as the one action. If
   [`one-write-per-render`](one-write-per-render.md) replaces that rule with a batch, check-then-save is one batch of one; until
   then the check must not spend the action, or the editor cannot check and save.
3. **Anonymous checks.** A check discloses nothing from the store, but it spends a compile slot, and a
   slot is a shared resource. Signed-in only is the conservative answer; a public check is a denial of
   service with a friendly face.
4. **The budget it is charged to.** A new `Pages:Compile:CheckSlots`, the same slots, or none at all
   (behind the existing queue) — and whether a check may be abandoned at the render's deadline in a way
   a save may not (`pages-a-page-may-save-itself` protects the author's edit; a check has none to
   protect).

## Not in scope

- **Running the page.** A check compiles; it does not render, and a page that throws at render time is
  a runtime error with its own answer (`pages-a-warning-never-stops-a-page` is about the compile).
- **Checking a page the caller cannot name.** The closure is decided by the key, which is the same
  rule a save follows.
- **A repository-wide check.** `make check-pages` already compiles the seeds; a page that checks every
  page in the store is a bulk compile, and it is IDEA-043's batch shape, not this member.

**Origin:** the owner's comparison of the site's IDE plan (`/ide-plan`, a stored page that is not in this
repository) with the job list of 2026-11-15. The IDE plan calls this "the biggest single gap" and its
workaround — save, read, accept the noise — is what every caller does today. The narrower half is
already named in [`vscode-cannot-check-a-page`](vscode-cannot-check-a-page.md) for VS Code; this job is the page side of the same compiler.

The plan that places it is [host support for the IDE](../plans/ide-host.md); this is slice 3 there, because a
check needs the diagnostic value [IDEA-041](closed.md) answers with.
