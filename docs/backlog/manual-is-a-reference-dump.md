<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The author manual is a reference dump whose contents list has drifted

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Web (seeds), docs |
| Location | `src/uk.osric.asp.Web/Pages/Seeds/help`, `test/uk.osric.asp.Web.Tests/PageSeedsTests.cs` |
| Spike | — |

## Idea

`/help` is the author manual and the source of truth for what a page can do
(`product-manual-is-a-page`), and it has become a reference ordered by API surface. A reader who wants
to do a thing — take a form and save it, show a live figure, accept a POST from another server — has
to assemble the answer from four sections that are filed under the names of the capabilities involved.

The index is a hand-kept copy of the headings, and it has already drifted from them:

- the contents calls a section *Saving the page you are* while the heading is *Writing the page store*;
- *Tables a page declares* is a section and is missing from the contents;
- the contents lists *Private pages and forms* before *Storage* and *Messages*, while the document has
  them the other way round.

That is exactly the failure the manual warns authors about: a second copy of a fact, kept by hand. It
is 1,137 lines, and much of the later half restates what `/admin/reference` now carries for every
capability, so a reader has two places to look and no way to know which is current.

The owner asked for a review and a rewrite on 2026-11-22. The shape it needs is the one the manual
teaches: the thing an author wants to do first, in the order they meet it, and the reference second,
with the index derived from the headings or checked against them rather than typed twice.

## Sketch

1. **The order is the author's, not the API's.** A first pass over what a page is, then the tasks an
   author meets in the order they meet them, then the reference. A capability gets its detail where
   its task is, not in a catalogue.
2. **The index is checked.** A test reads the seed, takes every `id` the contents links to and every
   `<h2 id>`, and fails when one list has an entry the other does not, or when their order differs.
   Generating the list from the headings in the template is the alternative; the test is the smaller
   change and it survives a hand-written order.
3. **The reference is one place.** A section that only restates a signature links to
   `/admin/reference` and says what the signature cannot: why, when and what it costs.
4. **The rewrite carries the current contract.** The `EditorPath` retirement
   ([`the-site-is-one-type`](the-site-is-one-type.md)), the request fidelity
   ([`a-page-sees-its-request`](a-page-sees-its-request.md)), the fetch rate
   ([`fetch-is-a-rate`](fetch-is-a-rate.md)) and the page-owned job
   ([`a-page-can-own-a-job`](a-page-can-own-a-job.md)) each update the manual in their own change; this
   item is the pass that makes the result read as one document.
5. **It stays a page.** The manual is a seeded page the owner can edit, so the rewrite is the seed and
   an install, not a new route.

## Not in scope

- **The assistant's prompt.** `update-the-prompt` is a different document, with its own budget and
  audience.
- **The API reference.** `/admin/reference` is generated from the source comments and stays generated.
- **Teaching a capability that does not exist.** A section is written from what the contract does, not
  from what an item plans.

## Open questions

Wait for the plan: whether the index is generated in the template or checked by a test, and whether the
rewrite keeps one page or splits the reference into a second seeded page.

**Origin:** the owner's instruction of 2026-11-22 to schedule a review and rewrite of `/help`, after
the capability work in this programme would otherwise change it one section at a time.
