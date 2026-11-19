<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A render may write a batch of pages, and the caller names the policy

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | blocked |
| Blocked by | `check-without-storing` |
| Plan | [ide-host](../plans/ide-host.md) |
| Area | Sdk, Web (pages, editor, work), docs, assistant |
| Location | `src/uk.osric.asp.Sdk/IPageWrites.cs`, `src/uk.osric.asp.Web/Pages/PageWritesScope.cs`, `src/uk.osric.asp.Web/Editor/PageSaver.cs`, `src/uk.osric.asp.Web/Pages/IPageRevisionStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` |

> **Re-prioritised 2026-11-18** by the owner. The rule is the friction the assistant meets on every
> multi-file edit, and it is the gate in front of a job writer ([`job-borrows-a-credential`](job-borrows-a-credential.md)), so it moves
> from the bottom of the order to just after [`check-without-storing`](check-without-storing.md), whose compile-without-store its
> `All` policy borrows. Its own terms are unchanged.

## Idea

`pages-a-page-may-save-itself` says "one render makes at most one save", which makes Save all a
sequence of requests that cannot be atomic: a refusal on the third page leaves the first two saved with
no way to know that from the fourth. It is also what makes a multi-file edit by the assistant N
requests with N chances to leave the site half-changed. The owner has decided on 2026-11-15 that the
rule goes, and the capability asks the caller which behaviour it wants instead.

This job was split out of [IDEA-043](closed.md) on the same day. 043 widens the authority and merges
the two write handles; this is the half that needs a vocabulary — three policies, a transaction
boundary and a report — and it is "a bit further down the backlog".

The wider class is "a page that maintains the site": the editor's Save all, the assistant's multi-file
edit, an import, a bulk restore, a seed harvest and a rename of a folder are one shape — several
writes, chosen policy, one report. Building it for Save all alone would be building it six times.

## Sketch

1. **A batch is a value and a policy.** `SaveManyAsync(IReadOnlyList<PageEdit> edits, PageSavePolicy
   policy)` where a `PageEdit` is (key, template, expected hash) — the same three inputs a single save
   already takes, so a caller that can do one can do many. Three policies, and the caller names one:
   - **All** — compile every edit, then write the ones that compiled and were not stale in one
     transaction; any refusal writes nothing and the report says which edit refused.
   - **FirstRefusal** — write in order and stop at the first edit that is not saved; the report says
     what was written and what was not attempted.
   - **Continue** — write every edit that can be written and report each refusal beside it; the report
     is the only record of the partial success.

   The report is one row per edit — saved, with its hash; stale, with the head it was rendered from;
   refused, with the sentence; or failed, with its diagnostics — plus the policy that ran and, for All,
   whether anything was committed.

2. **The transaction boundary.** A compile is slow (30–70 ms warm, bounded by
   `Pages:Compile:TimeoutSeconds`) and slots are scarce (`Pages:Compile:Slots` is 1), so an All batch
   cannot hold a write transaction open across compiles. It compiles first — which is
   [`check-without-storing`](check-without-storing.md)'s work, borrowed — and then opens one transaction and inserts every revision
   in it. If the render's deadline passes while compiling, nothing has been written, which is exactly
   what All promised. A save that has begun is never abandoned; a batch that has begun committing is
   not either.

3. **One save is a batch of one.** `SaveAsync` becomes the one-edit case of the same algorithm, so the
   conditional save, the stale answer and the diagnostics cannot drift between the two. The bound
   self-save (`SaveAsync(template)`) keeps its simplest shape, because a page repairing itself is the
   recovery path.

4. **Nothing else moves down.** The batch writes pages; it does not rename ([`page-cannot-move`](page-cannot-move.md)),
   search ([`cannot-search-the-store`](cannot-search-the-store.md)) or check without saving, except as the first half of All. A batch
   that also deletes, installs seeds or restores revisions multiplies the policy question by four and
   is not in the first version — those stay single-action members.

5. **The documentation.** `/help` gains the capability's section, the three policies and their exact
   words (`product-manual-is-a-page`); `PageAuthorPrompt` gains the batch and its policies with its
   `Version` bumped, because the assistant is the second caller and should batch a multi-file edit
   rather than posting it as a sequence; [pages](../pages.md) and [storage](../storage.md) record the
   contract and the transaction; and the merge writes **pages-a-batch-names-its-policy** and edits
   `pages-a-page-may-save-itself`, which loses "one render makes at most one save".

## Open questions

1. **Whether the three policies are three enumerators or a result-then-commit pair.** All and
   FirstRefusal differ in when they stop; Continue differs in what it reports. One enum with three
   names is the smallest vocabulary, and the words are the ones `/help` and the prompt will use, so they
   deserve the most care in the whole job.
2. **What All does with a compile error.** It is not a refusal: the edit compiled and the compiler
   answered a diagnostic, and a page that does not compile is still storable by a single save today. So
   All writes it and reports the diagnostics, and only a *refusal* (stale, reserved, anonymous) stops
   the transaction. Whether "compiles cleanly" should be a fourth policy is the question.
3. **Whether the report is per edit or per key.** One row per key is what a caller wants; two edits to
   one key in one batch must then be refused as a page bug rather than silently merged.
4. **Whether a batch may span a library and its dependents.** It writes both; whether it compiles the
   dependents again in the same batch, or leaves them stale as the lazy path does ([page libraries](../plans/archive/page-libraries.md#d7--the-closure-is-recorded-with-the-image-and-nothing-recompiles-eagerly)),
   is a property of the saves rather than of the batch, so the answer is likely "leave them stale, and
   let [`library-edit-cost-is-invisible`](library-edit-cost-is-invisible.md) say which they are".
5. **Whether the assistant may batch across the site.** Its edit-safety rules are per key; a batch that
   writes ten pages is ten decisions in one request. The capability allows it, and the assistant's own
   policy may be stricter — that is the assistant's prompt, not the contract.

## Not in scope

- **The widened authority and the merged capability.** [IDEA-043](closed.md), which lands first.
- **Renaming, searching and checking.** [`page-cannot-move`](page-cannot-move.md), [`cannot-search-the-store`](cannot-search-the-store.md),
  [`check-without-storing`](check-without-storing.md).
- **Bulk delete, install and restore.** Single-action members, and their own policies when they need
  one.
- **A second transaction model.** One database, one transaction; no outbox, no two-phase commit, no
  queue. `n` is small (`code-n-is-small`).
- **Undo.** A batch that wrote is a batch of revisions, and the history is where a mistake is answered.

**Origin:** split out of [IDEA-043](closed.md) by the owner on 2026-11-15 ("yes to splitting out batch
save to a separate problem, that's a bit further down the backlog"). The shape of the three policies is
the owner's, from the same note, and the IDE plan's Save all
([the programme](../plans/ide-host.md)) is the caller that found the rule.
