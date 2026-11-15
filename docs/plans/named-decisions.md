<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Named decisions

| | |
| --- | --- |
| Status | Proposed 2026-11-15 — waiting for the owner. The record and the policy are drafted; nothing cites the new file yet |
| Size | Large as a reading task, small as a change: one new document of 151 entries, a banner on the old one, and the policy restated in three places. No code, no migration of existing citations |
| Spike | None. The numbers below were counted on `trunk`: 297 body entries and 31 superseded rows in `docs/decisions.md`, 24,362 words; 3,179 `DEC-` citations across 112 files |
| Decisions | The policy itself is the decision this plan records, and it is not numbered: it is `product-decisions-have-names` in the new record, and it replaces the numbering rule it was written under |
| Backlog | A `make decisions` check — unique names, names inside the area list, citations that name an entry — is the obvious next step and is not in this one |

## The short version

`docs/decisions.md` is a list of 297 numbered rows in ten tables, with a 31-row supersession table
under it and a fifth of its citations pointing at numbers whose rows are gone. The numbers are the
problem: they are allocated by counting in private, and the file shows what that costs — two decisions
took 184 on the same day, one of them had to be renumbered with four documents of citations, and
`DEC-319`'s own text records the accident. A number also carries no meaning: `DEC-142` sends a reader
into a file to find out what it says, where a name says it.

So: a decision gets a name. `area-subject`, chosen from its subject, permanent once merged, cited in
backticks with no number. Names collide far less often than numbers because the area bounds the space
and the subject narrows it, and when they do collide the collision is *informative*: two decisions
claimed one subject, so either they are one rule (fold them) or one name was too broad (narrow it).
Either way the merge settles it while nothing on trunk cites the name yet, which is the only moment a
rename is free.

While the list is being rewritten, it is also being condensed: entries describing one mechanism become
one entry, 31 superseded rows are dropped rather than carried, and the arguments that were repeated in
each row now sit once. 297 entries become 151, and 24,362 words become 19,003. The old file stays, with
a banner saying it is frozen — its numbers keep their meaning, so no citation written before today
breaks.

## What a number cost

| Fact | Where |
| --- | --- |
| Two branches took 184 on 2026-11-15, for the assistant's memory and for a self-contained chart; the chart pair moved to 319 and 320, and four documents were edited in the merge | DEC-319, DEC-320 and the last row of the Superseded table |
| Nine numbers are cited but have no row of their own — 36, 55, 89, 109, 113, 117, 128, 211, 221 — removed as superseded and cited 33 times outside the file between them | `comm` of the body's ids against every id the repository mentions |
| A reader who meets `DEC-190` in a doc comment must open the file and scan for it; the number is not the decision | 3,179 citations in 112 files |
| The Superseded table is a second copy of facts the plans and git already hold, and a third of it describes numbers no longer in the body | the table's own rows |
| "Take the next number from trunk" is a merge-time race by construction: the rule's defence is that a human notices | the policy paragraph DEC-232 recorded |

## The design

### A name, and what it is made of

`area-subject`: lowercase ASCII, hyphenated, two to six words, the area first. The areas are fixed and
listed in the record — `product`, `code`, `pages`, `shell`, `auth`, `assistant`, `deploy`, `metrics` —
and a new one is an edit to that list in the change that needs it.

The name states the rule rather than the topic: `pages-public-by-default`, not `pages-authentication`;
`pages-a-warning-never-stops-a-page`, not `pages-diagnostics`. That is a real constraint, not a style
preference: a topic name reaches for a second decision's territory, and the second decision is what
discovers it. It also gives the clash rule something to work with.

The heading is the anchor, so the name is both an id and a link target:
`[pages-public-by-default](../decided.md#pages-public-by-default)`. A citation is the name in backticks,
with no prefix: `pages-public-by-default`.

### Taking a name, and the clash

A name is taken when the work merges, exactly as a number was: the plan proposes the name it will
carry, the merge writes the entry and points the plan at it.

A clash is a signal. Numbers collide because two people counted in private, and the collision says
nothing about the two decisions. Names collide because two decisions claimed one subject, and the
collision says everything:

- **One rule.** Fold the second text into the entry already there, and leave the plan citing it once.
  A merge is not the place for two paragraphs about one rule — that is how the old file reached 297.
- **Two rules.** The one landing second takes a narrower name, and every citation in that branch is
  updated in the same merge. That is safe precisely then: nothing on trunk cites either name yet.

A name is never disambiguated with a number. `pages-api-policy-2` is the numbered scheme wearing a
name, and a decision that needed a `2` needed a narrower name to begin with.

### Keeping the list short

Three rules in the record's own preamble, each aimed at a way the old file grew:

- **One rule, one entry.** A change that qualifies an entry edits it. An entry that stops being true
  is edited or deleted rather than kept beside its replacement — the reasoning is in the plan that
  proposed it and the history is in git, so a superseded table is a third copy of a changed fact.
- **Name the rule and the reason; leave the value where it is used.** A limit or a name is referred
  to by the configuration key or options type that holds it, with the shipped value quoted only where
  the value *is* the decision. Two homes for a constant means one is wrong.
- **Keep the entry short.** What is decided, why in a sentence, and the alternative rejected where the
  choice would look arbitrary otherwise.

### The retired list

A name that was used and then removed is recorded under `## Retired` with what replaced it, so a later
author cannot take a name that has meant something else. It starts empty: the numbering scheme was a
way of writing decisions down rather than a decision with a name, and the entries already superseded
when the new record was written stay in the frozen file, where their numbers still mean what they
meant.

## What was folded, and where everything went

Every row of the old file is accounted for below. An entry with several ids absorbed them; the entries
that were superseded before today are not listed, because they were not in the body. This table is the
plan's, not the record's: it is how a reviewer checks that nothing was lost in the rewriting, and it is
not a numbering scheme creeping back in.

### Product

| Name | Old rows |
| --- | --- |
| product-a-page-that-edits-itself | DEC-01 |
| product-one-owner | DEC-03, DEC-04 |
| product-no-contributions | DEC-05 |
| product-work-is-jobs | DEC-07, DEC-08 |
| product-plan-before-building | DEC-09 |
| product-steps-wait-for-the-owner | DEC-281 |
| product-manual-is-a-page | DEC-173 |
| product-decisions-have-names | DEC-232, replaced |
| product-commits-are-local | DEC-73 |

### Code

| Name | Old rows |
| --- | --- |
| code-functional-core-imperative-shell | DEC-10 |
| code-nothing-public-without-a-caller | DEC-11, DEC-166 |
| code-minimal-endpoints | DEC-12 |
| code-shapes | DEC-13 |
| code-n-is-small | DEC-14 |
| code-system-first | DEC-15 |
| code-no-speculative-code | DEC-27, DEC-29 |
| code-vertical-slices | DEC-90 |
| code-policy-values-are-configuration | DEC-282 |
| code-validation-at-the-boundary | DEC-167 |
| code-names-say-what-they-are | DEC-122, DEC-147 |
| code-one-gate | DEC-16, DEC-72, DEC-74, DEC-75 |
| code-analyzers-are-strict | DEC-290 |
| code-tests-are-written-first | DEC-76, DEC-77 |
| code-a-wedged-suite-fails-the-gate | DEC-78 |
| code-browser-suite-is-opt-in | DEC-120, DEC-179 |
| code-licence-is-mit | DEC-79 |

### Pages

| Name | Old rows |
| --- | --- |
| pages-the-contract-is-the-seam | DEC-30, DEC-88 |
| pages-names-follow-the-pipeline | DEC-114 |
| pages-a-key-is-a-path | DEC-17, DEC-18, DEC-91 |
| pages-root-is-a-page | DEC-06 |
| pages-compile-on-save | DEC-19, DEC-25, DEC-26 |
| pages-diagnostics-speak-template-space | DEC-22 |
| pages-a-warning-never-stops-a-page | DEC-23, DEC-178 |
| pages-a-compiler-fault-is-ours | DEC-20 |
| pages-diagnostic-codes-are-permanent | DEC-21 |
| pages-one-log-category | DEC-24 |
| pages-a-page-is-nullable | DEC-177 |
| pages-a-page-has-a-state | DEC-165, DEC-258, DEC-276 |
| pages-a-page-may-declare-types | DEC-92 |
| pages-the-shape-check | DEC-273 |
| pages-the-api-policy | DEC-35, DEC-37, DEC-38, DEC-124, DEC-130, DEC-257 |
| pages-load-isolation | DEC-39 |
| pages-there-is-no-sandbox | DEC-40, DEC-41 |
| pages-the-execution-budget | DEC-42, DEC-43, DEC-44 |
| pages-health-and-restarts | DEC-45, DEC-46 |
| pages-page-io-is-async | DEC-123, DEC-292 |
| pages-the-response-is-buffered-until-a-flush | DEC-31, DEC-34, DEC-293, DEC-297 |
| pages-answer-get-post-head | DEC-33 |
| pages-headers-are-an-allow-list | DEC-32 |
| pages-a-stored-page-is-never-cached | DEC-136, DEC-236 |
| pages-capabilities-are-one-seam | DEC-142, DEC-143, DEC-228, DEC-254 |
| pages-page-storage | DEC-125, DEC-126, DEC-127 |
| pages-outbound-fetch | DEC-129, DEC-216 |
| pages-fetch-streams-lines | DEC-307, DEC-308 |
| pages-fetch-credentials | DEC-230 |
| pages-public-by-default | DEC-02, DEC-132 |
| pages-identity-is-a-plain-value | DEC-133 |
| pages-redirect-to-login | DEC-134 |
| pages-a-signed-in-post-is-checked | DEC-135 |
| pages-content-types | DEC-144, DEC-197 |
| pages-scripts-are-same-origin-modules | DEC-138, DEC-248, the script half of DEC-93 |
| pages-an-asset-is-a-page-with-no-code | DEC-239, DEC-240, DEC-244 |
| pages-a-versioned-url-promises-immutable | DEC-238, DEC-241, DEC-242, DEC-243, DEC-245, DEC-246, DEC-247 |
| pages-one-base-sheet | DEC-155, DEC-217 (DEC-116 superseded) |
| pages-a-layout-is-a-capability | DEC-148 |
| pages-one-request-one-chain | DEC-149, DEC-150, DEC-157 |
| pages-layouts-are-markup | DEC-158, DEC-194 |
| pages-the-document-is-the-shells | DEC-107, DEC-154, DEC-156, DEC-288 (DEC-121 superseded) |
| pages-markdown | DEC-163, DEC-164 |
| pages-diff | DEC-218, DEC-219, DEC-220 |
| pages-the-store-is-read-through-one-seam | DEC-168, DEC-172, DEC-274, DEC-279, DEC-280 |
| pages-reading-the-store-is-public | DEC-169 |
| pages-a-listing-carries-facts | DEC-170, DEC-171, DEC-275 |
| pages-the-store-is-a-tree | DEC-277, DEC-278 |
| pages-a-page-may-save-itself | DEC-189, DEC-190, DEC-191, DEC-192, DEC-193, DEC-195 |
| pages-scratch-saves-itself | DEC-196 |
| pages-the-editor-is-a-page | DEC-259, DEC-265, DEC-266, DEC-268 |
| pages-the-editor-capability | DEC-260, DEC-261, DEC-262 |
| pages-delete-is-a-tombstone | DEC-263 |
| pages-install-and-restore | DEC-174, DEC-249, DEC-250, DEC-251, DEC-267 |
| pages-scrubbing-is-not-editing | DEC-264 |
| pages-the-session | DEC-233, DEC-234, DEC-235 |
| pages-build-info | DEC-199 |
| pages-page-first | DEC-175, DEC-210 |
| pages-an-archive-is-a-database | DEC-139 |
| pages-a-page-is-one-of-three-kinds | DEC-283 |
| pages-a-library-is-named-by-a-using | DEC-284, DEC-285, DEC-286, DEC-287, DEC-289 |
| pages-a-worker-is-a-kind | DEC-310, DEC-311, DEC-318 |
| pages-jobs-run-a-worker-for-the-page | DEC-312, DEC-313 |
| pages-a-job-is-not-durable | DEC-314, DEC-315 |
| pages-a-viewer-waits-on-a-job | DEC-316 |
| pages-a-worker-gets-no-response | DEC-317 |
| pages-a-stream-is-served-alone | DEC-294 |
| pages-the-first-flush-commits | DEC-295, DEC-296, DEC-298, DEC-299 |
| pages-a-stream-is-bounded | DEC-303, DEC-304, DEC-305 |
| pages-a-stream-reports-its-own-end | DEC-300, DEC-306, DEC-309 |

### Shell and auth

| Name | Old rows |
| --- | --- |
| shell-redirect-is-one-helper | DEC-28 |
| shell-nobody-is-handed-a-blank-failure | DEC-131 |
| shell-security-headers-are-one-set | DEC-93 |
| shell-the-markup-is-semantic | DEC-108, DEC-115 |
| shell-accessibility-is-asserted | DEC-110 |
| shell-the-menu-is-written-twice | DEC-151 |
| shell-admin-is-a-page | DEC-152 |
| shell-the-admin-tools | DEC-255, DEC-256 |
| shell-sign-out-is-a-post | DEC-153 |
| shell-the-cookie-names | DEC-237 |
| shell-the-reference | DEC-302 |
| auth-cookie-not-identity | DEC-47 |
| auth-passwords | DEC-48, DEC-49, DEC-50, DEC-51 |
| auth-sign-in-is-long-lived-and-revocable | DEC-52, DEC-53, DEC-54 |
| auth-antiforgery | DEC-56 |
| auth-the-key-ring-is-persisted | DEC-57 |
| auth-sign-in-is-rate-limited-per-client | DEC-118 |

### Assistant

| Name | Old rows |
| --- | --- |
| assistant-always-on | DEC-111 (optional half), DEC-301 |
| assistant-writes-are-revisions | DEC-111 |
| assistant-a-run-is-not-resumed | DEC-111 |
| assistant-knows-the-guard | DEC-137 |
| assistant-deadlines-bound-silence | DEC-119 |
| assistant-a-run-pauses-on-budget | DEC-215 |
| assistant-budgets-are-one-snapshot | DEC-141, DEC-159, DEC-160, DEC-161, DEC-162 |
| assistant-the-request-budget-is-tokens | DEC-200, DEC-204 |
| assistant-old-tool-results-are-masked | DEC-201, DEC-202 |
| assistant-usage-is-accumulated | DEC-203 |
| assistant-memory-belongs-to-the-assistant | DEC-291 |
| assistant-memory-is-the-owners | DEC-184, DEC-187, DEC-188 |
| assistant-memory-is-written-by-tools | DEC-185 |
| assistant-memory-recall-is-fts5 | DEC-186 |
| assistant-memory-page | DEC-205, DEC-206, DEC-207, DEC-208, DEC-209 |
| assistant-a-tool-is-one-type | DEC-231 |
| assistant-reads-the-shell-files | DEC-198, DEC-253 |
| assistant-reads-history | DEC-252 |
| assistant-edit-page-is-safe | DEC-269, DEC-270, DEC-271, DEC-272 |
| assistant-script-checks | DEC-145, DEC-146 |
| assistant-line-numbers-are-physical | DEC-112 |
| assistant-the-shell-lexes-before-it-judges | DEC-222 |
| assistant-the-shell-is-posix | DEC-223, DEC-224, DEC-225, DEC-227 |
| assistant-the-shell-is-applets | DEC-226 (DEC-221 superseded) |

### Deploy and metrics

| Name | Old rows |
| --- | --- |
| deploy-one-small-host | DEC-58, DEC-84 |
| deploy-the-image | DEC-59, DEC-60, DEC-82, DEC-85, DEC-229 |
| deploy-loopback-and-caddy | DEC-61, DEC-62 |
| deploy-a-release-is-gated | DEC-63, DEC-71 |
| deploy-quadlets-are-the-source-of-truth | DEC-69 |
| deploy-caps-and-swap | DEC-64, DEC-83, DEC-86, DEC-87 |
| deploy-the-firewall-and-the-backups | DEC-65, DEC-67, DEC-70 |
| deploy-no-outbound-alerting | DEC-66 |
| deploy-the-domain-is-public | DEC-68 |
| metrics-prometheus-only | DEC-80, DEC-81 |
| metrics-logs-are-one-json-line | DEC-140 |
| metrics-one-panel-per-instrument | DEC-180, DEC-183, DEC-212, DEC-214 |
| metrics-a-series-is-identified-without-colour | DEC-181, DEC-182, DEC-319 |
| metrics-a-chart-stands-alone | DEC-213, DEC-320 |

## What changed in the writing, not only in the filing

- **Superseded rows are gone**, all 31 of them. Two were folded into live entries (`DEC-116`'s sheet
  order into `pages-one-base-sheet`, `DEC-121`'s no skip link into the document entry); the rest were
  the record of a change rather than the rule, and the plans and git already hold it.
- **Duplicates are merged.** `DEC-178` restated `DEC-23`; `DEC-276` restated `DEC-258`; `DEC-300` said
  "no stream metric yet" and `DEC-306` added one; `DEC-292` restated `DEC-123`. Each is now one clause
  in one entry.
- **Rejected alternatives are kept where they explain the shape** — the post-emit scan, the allow-list,
  `IAsyncEnumerable`, the durable job journal, keeping the render slot — and dropped where the old row
  was recording an argument with a design nobody proposed again.
- **Every entry still names where it is recorded**, so the chain from rule to detail survives the
  rewrite. The links were copied from the old rows and are relative to `docs/`, as before.

## The old file

`docs/decisions.md` gets one edit: a banner under its heading saying it is frozen, that its numbers keep
their meaning, and that new decisions are recorded in `decided.md`. Nothing else changes, so every
existing `DEC-` citation and every `#anchor` link into it still resolves. That is deliberate: the
alternative — migrating 3,179 citations in 112 files, dozens of them inside archived plans that describe
what was true at the time — would rewrite history to no reader's benefit.

Citations are migrated opportunistically instead: a file touched for another reason may have its `DEC-`
numbers replaced with names, and the change that does it says so. `docs/plans/archive/` is never
migrated.

## What this step does not do

- **No check.** A `make decisions` target or a small tool that fails on a duplicate name, a name outside
  the area list, or a citation that names no entry would make the clash rule mechanical instead of
  remembered. It is the next step, and this one is the record to check.
- **No migration of existing citations**, for the reason above.
- **No deletion of the old file.** It is frozen, not removed.

## Open questions for the owner

1. **The file's name.** `docs/decided.md` is the proposal — short, and it reads as a record rather than
   a rule book. It is permanent once merged, so this is the one decision that is worth a minute.
2. **The area list.** Nine areas: `product`, `code`, `pages`, `shell`, `auth`, `assistant`, `deploy`,
   `metrics` — and no `storage`, whose decisions are all a page's or the assistant's. Is that the right
   grain, or is `pages` (70 of the 151) doing too much work?
3. **The condensation.** 297 entries to 151, and 24,362 words to 19,003. Every entry is now a heading
   with a rule under it rather than a row with a paragraph in a cell. If that is not far enough, the
   next pass is to drop the entries whose rule is stated in full by the linked document and pinned by a
   test; say so and it is a different, shorter record.
4. **The check, now or later.** The policy is a convention until something enforces it. I would land
   the record first and the check next, so the check is written against a file that is already right.
