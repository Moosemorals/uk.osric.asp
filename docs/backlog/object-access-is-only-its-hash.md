<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# An object's only access control is that you have to know its hash

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | blocked |
| Blocked by | `storage-is-not-one-model` |
| Plan | — |
| Area | Sdk, Web (pages, shell), docs |
| Location | `src/uk.osric.asp.Web/Shell/ObjectEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageObjectStore.cs`, `src/uk.osric.asp.Web/Pages/SqliteStoreObjects.cs`, `src/uk.osric.asp.Sdk/IObjectStore.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md`, `docs/shell.md` |
| Spike | — |

## Idea

The object shape lets a page keep bytes, and `/o/{sha}` serves them. What decides who may read one is
three checks and nothing else:

- the row's subject is any `page:%`, **or** one of the reader's own subjects
  (`session:`, `browser:`, `account:`);
- the row has not expired;
- the digest in the URL is canonical.

So a Page object is world-readable to anyone who knows, or guesses, 64 hexadecimal characters:
anonymous, unauthenticated, unthrottled, with no referrer or hotlink check and no way to revoke it
except deleting the whole page — and a `public, max-age=31536000, immutable` URL outlives even that in
any cache that took it. A Visitor object is properly bound to the reader, but "bound" means "whatever
cookie jar presented it", and the `b` cookie is a year of device identity.

That is the model [the storage plan](../plans/storage-model.md) chose, and
`pages-an-object-is-content-addressed` records it as such. It is not a bug in the slice. It is the
smallest useful boundary, and it is the boundary only because nothing else has been designed: there is
no way to say *this object is for the owner*, or *only the page that wrote it may serve it*, or *this
one is temporary*, or *take that URL back*. A page that accepts bytes from a visitor and puts them in a
Page object has made them public, and nothing in the contract says so at the point it happens.

## Why this is a surface rather than a flag

Every knob here interacts with something that is already load-bearing:

- **Content addressing.** The digest is the identity, so a URL cannot be rotated: revoking one address
  means the bytes need a second address. Dedupe and revocation pull in opposite directions.
- **Caching.** `public, immutable` is the whole point of a page object's URL, and it is exactly
  incompatible with any restriction that can later change. A restricted object must be `private` and
  revalidated, which gives back the CDN.
- **The subject binding.** `pages-the-subject-is-the-binding` says reads are open across page keys and
  writes are bound. Objects inherit that, which is why any page can open any page's bytes — one
  decision, currently the most permissive part of the shape.
- **The cookie model.** `s` is `Strict`, `a` and `b` are `Lax`, and `Cross-Origin-Resource-Policy:
  same-origin` is what stops a cross-site embed. Any new rule has to say what it assumes about those,
  or it will be defeated by a top-level navigation.
- **The observer.** Today a foreign Visitor object is a `404`, so the route cannot confirm existence.
  A stricter rule must keep that property; a `403` for one class of caller is an oracle.
- **Guessing.** The digest is a fingerprint. For low-entropy bytes — a short note, a known image — an
  observer can hash a guess and learn whether *some* page holds it. Identical bytes under a page and a
  visitor mean the page's row wins and the answer is `public`, so a private file can be confirmed
  public without attacking anything.

## What a design would settle

1. **Who may read a Page object.** Everyone (today), any signed-in visitor, only the page whose
   render asks, or a named set.
2. **How a read is authorised.** The reader's cookie subject (today); a signed URL whose signature is
   minted by the host for a page to link; a per-object access row; or a declaration on the owning page.
3. **Revocation.** Delete the page, delete the row, or a second address. A page cannot mint a URL
   today, and a private object's URL is guessed only by its own reader, so the answer may be "delete,
   and accept the cache".
4. **Caching against restriction.** Which objects may be `public, immutable`, and whether a restricted
   object is `private, no-store` or `private` with a validator.
5. **Abuse.** A rate limit on `/o` (none today), a per-subject bytes-served bound, and whether Caddy
   gets a hotlink rule.
6. **Management.** Whether the owner can list and delete a page's objects, and where — the editor, a
   shell route, or nothing.
7. **The manual.** `/help` must say what a page subject's object *is* — today it implies public, but
   not what that costs an author who puts a visitor's bytes there.

## Open questions

The questions are put before this is planned, because the answer to the first changes the shape of
every other.

1. **Is a Page object public by default, or does the author opt in?** Today public is the default and
   there is no opt-in. Opting in would make the common case explicit and the unsafe case impossible by
   accident, at the cost of every `<img src="/o/…">` needing a second call.
2. **Should a page read another page's objects at all?** The cross-page read is deliberate for values
   and records; for bytes it is the most permissive rule in the shape, and it may be that a page should
   only open what it wrote.
3. **Does a Visitor object ever become shareable** — a link one visitor can hand another — or is "the
   reader" final?
4. **Is revocation needed beyond deleting the page**, given the year-long cache? If it is, the URL
   needs a second, non-content component, and dedupe needs rethinking with it.
5. **Is a page allowed to mint a sharable URL?** A capability to sign one is a new `Sdk` surface, and
   it is the only way a page can put an object somewhere temporary.
6. **What bounds `/o`?** Per address, per subject, per page, or none.
7. **Is the guessable-content confirmation worth closing**, or accepted as a property of content
   addressing?

A question listed here that the first three do not force — 6 and 7 — may wait for the plan rather than
the answers, because neither changes the shape.

## Not in scope

- **Uploads and downloads.** [`whim-page-file-upload`](WHIMS.md) and
  [`whim-page-file-download`](WHIMS.md) are the two ends of a byte stream, not who may read one.
- **Secrets.** A Page secret is already unreadable through any other shape
  ([`a-page-cannot-keep-a-secret`](a-page-cannot-keep-a-secret.md)); this item is about bytes that are
  meant to be served.
- **An object in the archive.** The archive carries Page values and nothing else, and that stands.
- **A transform or an image pipeline.** Re-encoding needs a capability that does not exist, and it
  would put the bytes behind a second policy.

**Origin:** the owner's question of 2026-11-23, after the object slice was built: *"are there any
access restrictions on objects, other than needing to know the hash?"* — and, on hearing that a Page
object is public to anyone with the digest and that `/o` has no rate limit, *"access control is a big
surface that I don't want to half-ass"*. The item exists so the surface is designed once rather than
grown a knob at a time.
