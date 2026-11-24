<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A secret a page keeps is plaintext in the database and in every backup

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Web (storage, configuration), deploy, docs |
| Location | `src/uk.osric.asp.Web/Pages/PageStoresOptions.cs`, `StoreSecrets` (new), `src/uk.osric.asp.Web/appsettings.json`, `deploy/operations.md`, `docs/storage.md` |
| Spike | — |

## Idea

A page that must hold a credential — a signing key, a remote API's token — cannot put it in page source
or in page values: both are readable through `/pages` and the whole revision history. The shape that
answers that is on trunk — `ctx.Stores.Page.Secrets`, which keeps opaque text that
`IPageCatalog` never returns, that no revision, search or archive carries, and that is deleted and
scrubbed with the page (`pages-a-secret-is-not-a-value`).

That shape is plaintext at rest. It is the same trust boundary as the password hashes and the session
rows already in `storage.db`, and the owner chose plaintext for the shape so it could land before the
storage release closes. But a signing key is different in kind from a password hash: a copy of the
database is a copy of the site's voice, and the database has a nightly snapshot, a pulled offsite copy
and the operator's own account.

This item records the gap and leaves the mechanism to its plan: a key held by the host (configuration,
a file, or the container's secret), a cipher, an envelope that can be rotated without re-encrypting
every row by hand, a migration for rows already written, and a line in the operations notes about what
a backup now contains. Until it lands, the shape's documentation says its rows are plaintext, and the
owner decides what may be kept there.

## Sketch

1. **The key is not the database's.** A key stored beside the ciphertext is a lock beside its key. The
   plan chooses between a required configuration value, a mounted secret and a key file with the
   container's own ownership, and says what a host without one does (refuse to start, rather than
   write plaintext silently).
2. **The cipher is authenticated.** AES-GCM is in the BCL and needs no package; a nonce per row and
   the subject and key in the associated data, so a row moved to another key does not decrypt.
3. **Rotation is a story, not a hope.** A key identifier on the row, decrypt-with-the-old and
   re-encrypt-with-the-new for new writes, and one sweep for the rest.
4. **A backup is still a backup.** Decryption needs the key, so the offsite copy is useless without it,
   and the key's own backup is the operator's problem — said plainly in
   [operations](../../deploy/operations.md#backups).
5. **The shape's docs change.** The secrets section of `/help` and
   [storage](../storage.md) say where the ciphertext is and what a copy of it means.

## Not in scope

- **The shape itself.** That landed as the storage model's secrets slice; this item encrypts what it
  stores.
- **A page managing keys.** A page writes and reads its own opaque text; the cipher is the host's.
- **Other stored data.** Passwords stay hashed and sessions stay tokens; this is the page's secrets.

## Open questions

Wait for the plan. Whether the key is configuration, a mounted file or both is the owner's call, and it
decides whether a missing key stops the site at startup.

**Origin:** the owner's answer of 2026-11-22 on the secrets shape: ship the shape plaintext and make
encryption its own later item.
