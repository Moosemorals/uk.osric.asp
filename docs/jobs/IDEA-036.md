<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-036 — A seed keeps no record of where it came from, so a deploy cannot say what it would change

| | |
| --- | --- |
| Size | L — it wants a plan of its own: a migration, two values on the page row, a change to the page contract, a new shell action, and an archive format version |
| Area | Core, Sdk, Web (storage, pages, admin, seeds), docs |
| Location | `src/uk.osric.asp.Core/Pages/`, `src/uk.osric.asp.Sdk/PageEntry.cs`, `src/uk.osric.asp.Sdk/IPageEditor.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageStore.cs`, `src/uk.osric.asp.Web/Pages/PageSeedInstaller.cs`, `src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs`, `src/uk.osric.asp.Web/Pages/PageCatalogScope.cs`, `src/uk.osric.asp.Web/Admin/RestoreEndpoint.cs`, `src/uk.osric.asp.Web/Admin/PageArchiveFormat.cs`, `src/uk.osric.asp.Web/Pages/Seeds/pages`, `src/uk.osric.asp.Web/Pages/Seeds/help` |

## Idea

A seed is a snapshot of the current truth per page, identified by nothing. Nothing in the store records
that a page came from a seed, so "the release moved this page" and "the author edited this page" are
the same observation: the bytes differ. Answering the question needs a third input, and there is
exactly one place it can be recorded — on the page's row, in the same write as the revision.

The consequence today is that no deploy can say what it would change:

- **The start install** creates a seed only when the key has no head row, and reports it nowhere.
- **`/admin/restore`** lists every seed and its state, and writes each changed, missing or tombstoned
  one in a single POST (`shell-the-admin-tools`) — but "changed" means "the bytes differ", so it cannot tell a page the
  release moved from a page the owner edited, and it offers the same button for both.
- **The page list** decides whether to offer *Install from seed* by comparing the live source with the
  seed source as strings. Two inputs, one bit of output, and it costs two reads per seeded row per
  render — one of them charged against `Pages:Read:MaxSourceCharsPerRender`, which the ~160 kB pack
  already spends a third of.
- **Nothing brings a seed back.** The owner edits a seeded page in production — that is what the site
  is for — and the pack in the repository falls behind the store with no route home except copying
  source out of the editor by hand.

An unattended upgrade, an honest overwrite prompt and a seed that can be harvested all need the same
one fact, so they are one job.

## Sketch

Record, per key, the content hash of the seed the page's content is anchored to (**R**), written only
by a seed write and left alone by every other. Compare it with the hash this release ships (**S**) and
the live content hash (**H**):

> **An automatic write requires H = R and S ≠ R.**

Everything else follows from that one invariant: six verdicts with their own words, the `unknown`
sentence that refuses to claim more than it knows, `retired` that never presents as an install, a bulk
apply that writes only what the gate allows, and an *adopt* action that records the store's content as
the new anchor — which is what makes a harvested pack reconcilable.

It also needs one fact the store does not have today: the revision hash is an identity (source, runtime
id, save instant and revision id), so it can never equal a seed's content hash. A plain content hash
has to sit on the head row beside R, written in the same statement as the revision.

The plan is [seed provenance](../plans/seed-provenance.md).
