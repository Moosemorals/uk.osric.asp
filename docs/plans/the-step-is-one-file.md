<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The step is one file

| | |
| --- | --- |
| Status | Proposed 2026-11-24 — written from the owner's four answers the same day: whims become items with no shape, work that wants a plan becomes an item, the old plan archive freezes, and the existing live plans are left alone. Nothing below is built; it waits for a yes |
| Size | Large — the record's rules and its checker, eight lines promoted to items, a citation sweep, CONTRIBUTING and the instructions, with seven live plans deliberately left where they are |
| Area | `docs/backlog` (the index, eight new items, a new archive), `docs/plans` (the index and the archive's README), `docs/README.md`, `docs/decisions.md`, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, `tools/check-backlog` |
| Spike | None. The counts were read off trunk on 2026-11-24: 27 open items in 27 order rows, 21 closed rows, 8 whims, 7 live plans and 87 archived plans, with `make backlog` reporting `27 items in 27 rows, 191 citations resolved`. The drift below was read in [the plans index](README.md) and [counting-bytes](counting-bytes.md) |
| Backlog | This plan is the whole of the work, and the last written this way: the record is what changes, so there is no item file ([the precedent](archive/backlog-record.md)). The rule it proposes applies to the changes after it, not to itself |
| Depends on | Nothing new. `make backlog` already runs in `make check` (`code-the-gate-checks-the-backlog`), and every name below is taken from the record as it stands |
| Decisions | New names proposed: **product-a-step-is-one-file** and **product-a-whim-is-an-item**. Edited in place at the merge: `product-work-is-jobs` (the `Plan` row and what a close does), `product-plan-before-building` (a plan is a section of the item's file) and `product-a-merged-plan-is-archived` (the file is what archives; the name is kept, because a name is permanent). Relies on `product-a-difficulty-is-not-a-duration`, `product-decisions-have-names`, `product-steps-wait-for-the-owner` and `code-the-gate-checks-the-backlog` |

## The short version

An item is a file from the moment it is worth writing down until it merges, and it is the only file.
A plan is a section of it. A whim is an item that has no answers yet. The record loses a stage
(`WHIMS.md`), a document kind (the separate plan), a cross-link and an unchecked index, and gains an
archive of finished work.

- **A whim is an item with no shape.** It is a file with a title, two lines under `## Idea` and an
  em dash where the work has no answer: Difficulty, Utility, Area, Location. Its `Status` is
  `unshaped`, and it is not in the order, because the order spends utility and difficulty and it has
  neither. [WHIMS.md](../backlog/WHIMS.md) is deleted.
- **A shaped item is an item.** Filling those four in and adding its row to [the order](../README.md#the-order)
  is the refinement the record already describes; nothing else about the file changes.
- **A plan is a `## Plan` section of the same file.** The item is `planned` while that section is
  there, and the order row's status links the item's own `#plan`. There is one link, not two, and it
  cannot point at the wrong file.
- **The merge archives the file.** It moves to `docs/backlog/archive/` and its row in
  [closed.md](../backlog/closed.md) keeps the slug and links it. Today a closed item is deleted and
  only git remembers it; after this the record keeps what was asked as well as what was done.
- **The seven live plans stay where they are** until each is next touched, because the owner chose
  the small change; this plan states what "next touched" means so the transition has an end.

## What exists today

| | |
| --- | --- |
| The item | A file with a header of Difficulty, Utility, Status, Blocked by, Plan, Area, Location and Spike, an H1, and an `## Idea` section |
| The plan | A second file in [docs/plans](README.md) with its own header — Status, Size, Area, Spike, Backlog, Depends on, Decisions — and the reasoning under it |
| The link, item → plan | The item's `Plan` row names the plan file |
| The link, plan → item | The plan's `Backlog` row names the item file, and `make backlog` fails unless both directions resolve |
| The whim | A row in [WHIMS.md](../backlog/WHIMS.md), with a `whim-` slug and no file, deliberately uncounted and unordered |
| Promotion | Write an item file, delete the whim's line — and write a plan file beside it if the work wants one |
| The live plans | Seven: assistant-shell-tool, build-metrics, counting-bytes, email, email-configuration, ide-host, outbound-headers |
| What the plan index says | Six of them. [counting-bytes](counting-bytes.md) has been live since 2026-11-24 and is not in [the table](README.md), because nothing checks it |
| A plan whose item is a whim | [build-metrics](build-metrics.md)'s `Backlog` row links `whim-build-timings` and says its line goes at the merge — the promotion the record describes, skipping the item the record requires |

The duplication is not hypothetical. The item for the next step,
[`tool-surface-is-one-shell`](../backlog/tool-surface-is-one-shell.md), and its plan,
[assistant-shell-tool](assistant-shell-tool.md), restate the same twenty-tools fact, the same evidence
links, the same area and the same spike in two headers and two openings. What must be built out of
each — what the owner is shown and who chooses — is no longer a second document's worth of work. The
split earned its keep while a plan was a programme of its own; the plans left to write are sections,
and the cross-link is the only thing the second file adds.

## The flow

| Stage | The file | What the gate checks |
| --- | --- | --- |
| **Unshaped** | H1, `## Idea`, `—` in Difficulty, Utility, Area and Location, `Status \| unshaped` | not in the order, and nothing else was filled in |
| **Shaped** | those four answered, `Status \| open`, a row in the order | the row agrees with the header and sits below its blockers |
| **Planned** | a `## Plan` section, `Status \| planned`, the row's status links `#plan` | the section is the only thing that makes it planned; a plan file and a section together is an error |
| **Archived** | the file moves to `docs/backlog/archive/`; the closed row keeps the slug and links it | the slug still resolves from every citation |

An item goes back from planned to open when its plan is refused and the section is taken out again;
nothing archives until the merge. Unshaped is not a waiting room for work the owner has started — a
shaped item may sit `open` for a year, and that is the order's business, not this rule's.

## An item, before and after it has a shape

Unshaped, in full — the smallest thing the record will hold:

```markdown
# A page cannot take a file

| | |
| --- | --- |
| Difficulty | — |
| Utility | — |
| Status | unshaped |
| Blocked by | — |
| Area | — |
| Location | — |
| Spike | — |

## Idea

`PageRequest` carries query, form, headers and cookies as text, so a part's bytes can only be
spooled before the render sees them. The limit is a policy value and the stream is handed to the
object store rather than a temp root.
```

Shaped, the same file with the four answers and its row in the order. Planned, the same file with
`## Plan` after `## Idea` and the `Status` row changed to `planned`. Its `Idea` section is not
rewritten when the plan is written: the gap stays as it was asked, and the plan is what is proposed
against it. That is the difference this change makes to a plan — it gains a neighbour rather than a
copy.

## What the check adds

| Rule | What `make backlog` reports |
| --- | --- |
| `unshaped` is a status the record derives: Difficulty or Utility is `—`, and both must be — | an item with one answered and not the other |
| An unshaped item is not in the order | a row naming it, and the row it would have taken |
| An unshaped item cannot be blocked or planned | a `Blocked by` or a `## Plan` on a file with no Difficulty |
| Area and Location may be `—` while unshaped and must be answered once shaped | a shaped item missing either |
| `planned` is derived from a `## Plan` section, or a `Plan` row that links a live plan | an item that says planned and has neither, and one that has both |
| The `Plan` row is required by nothing: it exists only while a plan is a file in `docs/plans/` | a `Plan` row that says `—`, and one that links the item itself |
| Archived items are frozen, like archived plans | a citation inside `docs/backlog/archive/` that no longer resolves |
| `whim-` is no longer a word the record uses | any backticked `whim-` slug anywhere, which is what proves the sweep is finished — a kinded citation is checked, and an idea's is checked only when it is a link, so the old prefix gets its own rule until the seven citations are links |

The order table's status cell keeps its one shape. A planned row says
`[planned](backlog/the-slug.md#plan)` instead of linking a plan file; every other state stays plain.

## The eight lines that become items

| Whim | Item | Title |
| --- | --- | --- |
| `whim-lsp-for-the-editor` | `editor-has-no-language-server` | The editor has no language server |
| `whim-backlog-on-the-site` | `nothing-shows-the-backlog` | Nothing on the site says what is already queued |
| `whim-build-timings` | `build-reports-no-timings` | The build reports no timings |
| `whim-mcp-server-tools` | `tools-are-a-closed-list` | The assistant cannot reach a tool the host does not hold |
| `whim-page-file-upload` | `page-cannot-take-a-file` | A page cannot take a file |
| `whim-page-file-download` | `page-cannot-send-bytes` | A page cannot give bytes out |
| `whim-a-worker-waits-for-a-message` | `worker-cannot-wait-on-the-queue` | A worker cannot wait on the durable queue |
| `whim-assistant-reads-objects` | `objects-cannot-be-read-back` | The assistant cannot read an object it was given |

Each keeps its two lines as the `## Idea` and its paragraph of detail where the line already had one;
`page-cannot-take-a-file` and `page-cannot-send-bytes` are long enough to read as half a plan, and
that text moves rather than shrinks. None is shaped here: Difficulty, Utility, Area and Location are
the owner's judgement, and eight answers invented in a process change are eight rows in the order
nobody asked for.

The rename is part of the promotion, because an item's slug is permanent once it is on trunk and a
`whim-` prefix would say forever that the work has no shape. Seven citations point at a whim today —
three open items, three live plans and the plans index — and each becomes a link to the item, which
is also what makes `make backlog` check it: an idea's slug has no kind for the token check to
recognise.

## What the seven live plans do

They stay. Each migrates in the change that next touches it, and "touches" means one of three things:
its item is shaped, its plan is revised, or its work merges. The migration is mechanical — the plan's
sections become the item's `## Plan`, the `Plan` row goes, and the plan file is deleted — and it is
done then because that change already has the plan in hand.

Until then the check allows a `Plan` row that links a live file in `docs/plans/`, which is the only
thing that keeps a plan outside an item legal. When the last of the seven lands, that allowance, its
row and the `Plans()` check are deleted in the change that does it; the plan says so here so the
allowance has an owner rather than becoming a permanent second scheme.

[build-metrics](build-metrics.md) is the case worth naming: it is planned against a whim, which the
new rule forbids. It stays as it is until it is next touched, and its plan already says the whim's
line goes at the merge; under the new rule the merge also shapes the item the line became.

## What moves, file by file

| File | Change |
| --- | --- |
| `docs/backlog/WHIMS.md` | deleted; eight files take its rows |
| `docs/backlog/*.md` (8 new) | the promoted items, each with a header, `## Idea` and its title |
| `docs/backlog/README.md` | `Whims` becomes `An item before it has a shape`; `Plan` stops being a required row; `—` becomes a legal value for the four fields an unshaped item has not answered; `Status` gains `unshaped`; the order says unshaped work is not in it; the closed section says the file archives; the check's list gains the eight failures |
| `docs/backlog/archive/README.md` | new: what the folder is and that it is frozen |
| `docs/backlog/closed.md` | new rows link their archived file; the existing 21 rows are left alone |
| `docs/plans/README.md` | rewritten: nothing new is written here; the seven that remain are named, and this also removes the table that is already missing [counting-bytes](counting-bytes.md) |
| `docs/plans/archive/README.md` | a line saying the folder is frozen as of the merge and new archived work goes to the backlog |
| `docs/README.md` | the `Plans` contents line and a sentence under the order: unshaped items exist and are not rows |
| `docs/decisions.md` | at the merge: two entries written, three edited in place |
| `CONTRIBUTING.md` | one branch rule — `idea/<slug>` to write an item or its plan, `kind/<slug>` to build it, `plan/<file>` only for a plan written before this change; the merge archives the file and writes the closed row |
| `.github/copilot-instructions.md` | `Plans and decisions` describes the file and its archive, and drops the plan file's move |
| `tools/check-backlog/Program.cs` | the eight rules above; `Whims()` and `WhimRow()` deleted; `docs/backlog/archive/` joins `Frozen()`; the run line counts unshaped items |
| the three items and three plans cited above | a `whim-` citation becomes a link to the item |

No `/help` change: none of this is something a page author can do. No `src/` change, so no suite
proves anything new and no browser test is written for a record.

## What proves it

- `make backlog` green on the migrated record, and its line naming the unshaped count; `make check`
  for the licence header on each of the eight items and the new archive's README, and for the
  citations the rename retargets.
- Eight deliberate breakages, each refused with the message the table above promises: a shaped item
  removed from the order; an unshaped item given a row; an unshaped item with a `## Plan`; a shaped
  item with `Difficulty —`; an item with `Status planned` and no section; an item with a section and
  a `Plan` link; a `Plan` row saying `—`; a `whim-` slug left in a live item.
- The archive resolves: a citation of an item that closed after the change still finds its file, and
  a link into `docs/backlog/archive/` that names a file the folder does not have fails.
- The check has no suite of its own — the record is its fixture — so the breakages above are run on
  a scratch copy during the change and reported at the merge. This change does not add a suite for
  the same reason [the previous one](archive/backlog-record.md) did not.

## What it is unsure of

- **Two schemes while the old plans live.** The allowance for a `Plan` row is a second shape, and the
  seven live plans are what keeps it. It has an end (the last of the seven lands) and a name
  (`Plans()`), and the risk is that the end is forgotten. The count line in `make backlog` could name
  the grandfathered items so the number is visible; this plan does not propose it, because a number
  that only goes down is a reminder nobody reads.
- **Two archives.** `docs/plans/archive/` freezes and `docs/backlog/archive/` begins, rather than the
  87 files moving. Moving them would retarget every link in [the decisions](../decisions.md) for no
  gain, and freezing is what [the old decision record](../archive/README.md) already did; the cost is
  that a reader looking for an old plan has two places to try.
- **The eight slugs are proposed, not decided.** A slug is permanent once merged, and naming the gap
  is the owner's judgement. `tools-are-a-closed-list` and `objects-cannot-be-read-back` are the two
  that could read differently: one says what the registry is rather than what the assistant cannot
  do, and the other is close to [`object-access-is-only-its-hash`](../backlog/object-access-is-only-its-hash.md)
  without being it.
- **A `## Plan` section is a heading, not a schema.** Nothing checks that a plan says what exists,
  what changes and what proves it, and the plan file's header (Size, Spike, Depends on, Decisions)
  has no home in an item's header. This plan proposes that the plan opens with its own small table
  under the section, and does not propose a check for it: the previous plan's header was a
  convenience, and a mandatory one would be a form to fill in.

## Not in this change

- Shaping the eight new items, or ordering them. Each is a step of its own, and the order is the
  owner's judgement.
- Migrating the seven live plans, or the 87 files in [the plan archive](archive/README.md).
- The `/backlog` page: it is `nothing-shows-the-backlog`, one of the eight, and it waits its turn.
- Adding a suite for `make backlog`.
