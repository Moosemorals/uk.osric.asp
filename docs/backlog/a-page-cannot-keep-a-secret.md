<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot keep a secret

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | blocked |
| Blocked by | `storage-is-not-one-model` |
| Plan | [storage-model](../plans/storage-model.md) |
| Area | Sdk, Web (pages, storage, work), docs |
| Location | `src/uk.osric.asp.Sdk/IPageSubject.cs`, `ISecretStore.cs` (new), `src/uk.osric.asp.Web/Pages/PageStoresScope.cs`, `StoreSecrets.cs` (new), `src/uk.osric.asp.Web/Pages/PageStoresOptions.cs`, `src/uk.osric.asp.Web/Pages/PageChain.cs`, `src/uk.osric.asp.Web/Work/PageJobRunner.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/storage.md`, `docs/pages.md` |
| Spike | — |

> **Absorbed 2026-11-22** — [one storage model](../plans/storage-model.md) delivers this item as its
> secrets slice: a fifth shape, `Secrets`, on the `Page` subject. It is recorded here rather than as a
> plan of its own because it is the same table, the same limits section and the same delete, move and
> scrub paths as the shapes already landing there. The slice is built on its branch, with the shape,
> the table, the `Pages:Stores:Secrets` limits, the delete, move and scrub paths, the archive's refusal
> and the manual, and it waits for the owner.

## Idea

A page that must hold a credential has nowhere to put it. Page source is readable through `/pages`,
the revision history and the export archive, so a key written into the template is published. `Values`
is no better: it is JSON a page reads back, it is carried by `/admin/export`, and it is readable across
page keys, so a key kept there is a key any page can read.

The page therefore cannot act as anything: it cannot sign a request to another server, cannot hold an
API token for a service it talks to, and cannot keep the private half of a key pair whose public half it
publishes. A node whose inbound requests must be verified can store the public keys it is given and not
the key that proves its own identity.

The shape that answers it is a **fifth shape beside values, records, messages and objects**: text a
page reads and writes, which nothing else in the system returns.

## What makes it a secret rather than a value

Five rules, and each one is a difference from `Values` on purpose:

- **No cross-page read.** `Values` lets `/a` read what `/_lib/b` left (`pages-the-subject-is-the-binding`
  calls it *ownership, not protection*, and it is open because showing what another page left is what
  the read is for). A secret is authority rather than data, so the handle answers only its own key's
  names: there is no `TryGetValueAsync(pageKey, key)` beside it.
- **Nothing lists it.** No source read, revision read, listing row, search result or `/admin/export`
  archive carries a secret. `IPageCatalog` never returns one, which is the sentence the owner asked
  for.
- **Opaque text, not JSON.** `SetAsync(key, string)` and a read that answers the text. A JSON shape would
  invite a structured secret and put the store in the one-set serializer's path for no gain.
- **Names, not entries, in a listing.** A listing answers the keys the page holds, never a value and
  never a preview, so a page can offer "rotate this" without the store handing out what it protects.
- **It lives and dies with its page.** Deleted with the page's Page subject, scrubbed with it, re-keyed
  when it moves, and dropped when the page is destroyed. There is no archive path and no import path.

Where it lives is the `Page` subject bound to the layer's own key. That gives two consequences for
free, and they are the ones an author needs:

- **A layout cannot read the content page's secrets.** A layer's `Page` handle is its own key
  (`pages-one-render-three-roles`), so a wrapper's secrets are the wrapper's.
- **A page-owned job can read the starting page's.** A job's `Page` store is the starting page
  (**pages-a-job-gets-three-scopes**), so a worker started by the inbox page holds that page's key to
  sign what it sends — which is the whole point, because the work outlives the request.

Which other subject may have the shape: none. `Visitor` does not, and its absence is a type rather
than a runtime surprise, as `Messages` is absent from `Page` (`pages-the-subject-is-the-binding`).

## The record has to say why this is not the credential

`pages-fetch-credentials` says a page names an operator-configured credential and never sees the value,
and the archived [page credentials](../plans/archive/page-credentials.md) plan rejected a per-page
`PageSecrets` table in these words: *"It also makes the value page-readable, which is the property this
design exists to avoid."*

That rejection stands for what it was about. An **operator's** token for a third party stays
host-held, attached only to its host and only from its page, because the site can show it is the token's
owner and a page bug cannot leak it. A **page's own** key is different: the page is the owner, nothing
can attach it on the page's behalf, and the value has to be readable for the page to sign with it.

The merge therefore records the distinction rather than quietly contradicting the older entry, and
amends `pages-fetch-credentials` with a sentence pointing at it.

## Sketch

1. **`ISecretStore` on `Page`.** `SetAsync(key, value)`, `TryGetAsync(key)`, `DeleteAsync(key)` and
   `NamesAsync()`, each shaped like its `IValueStore` sibling so an author who knows one knows the
   other, and each refusing a `HEAD`, an abandoned render and a passed deadline as every shape does.
2. **`StoreSecrets`.** One table, the Page subject key, the page key, the name, the value and the
   write time; the primary key is the subject, page and name, as `StoreValues`' is. No lifetime
   column — a secret is the page's or it is not there — and no expiry.
3. **The limits.** Its own row in `Pages:Stores` beside the value limits: the longest name, the longest
   value and the count, refused in the statement that writes.
4. **The paths.** Delete, scrub, move and `users remove` take it with the page, exactly as they take a
   value; the archive's writer skips it and its reader refuses an archive that carries one.
5. **The manual and the record.** `/help` gains the shape beside Values, with the rules above in the
   author's words; [storage](../storage.md#values) carries the table and the limits; the merge records
   **pages-a-secret-is-not-a-value** and amends `pages-fetch-credentials`,
   `pages-the-subject-is-the-binding`, `pages-an-archive-is-a-database` and `pages-one-stores-section`.

## Not in scope

- **Encryption at rest.** The shape lands plaintext, as the owner chose on 2026-11-22; the cipher, the
  key and rotation are [`secrets-are-readable-at-rest`](secrets-are-readable-at-rest.md).
- **An editor screen.** The owner edits a page's source, not its data; a page that wants a form to set
  its own key writes one, and the secret is the Store's rather than the revision's.
- **A secret shared between pages.** A library reaches its own with `For(libraryKey)` and hands it out
  through a method; that is the only sharing path, and it is the library's code deciding.

## Open questions

None. On 2026-11-22 the owner chose the secrets shape inside the storage plan, before its release
closes, and accepted plaintext for this step with encryption as a separate item.

**Origin:** the owner's ask 4 of 2026-11-22, whose node could not hold a signing key because page
source and page state are both readable, and their note that the gap had not come up before the
storage rewrite.
