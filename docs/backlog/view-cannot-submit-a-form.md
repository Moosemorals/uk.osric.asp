<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A run can look at the site but cannot submit one of its forms

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | medium |
| Status | open |
| Blocked by | — |
| Area | Auth, Core (Assistant), Web (assistant, pages), docs |
| Location | `src/uk.osric.asp.Web/Authentication/ViewGuard.cs`, `src/uk.osric.asp.Web/Authentication/ViewToken.cs`, `src/uk.osric.asp.Web/Assistant/Tools/ViewTool.cs` |
| Spike | — |

## Idea

`view` GETs a path on this site as the run's owner and answers the response as the server produced it,
including the redirect chain. That is how a run sees what it built. It cannot submit anything: a
second visit is a fresh first visit with no cookie jar, and a form's POST is answered `405` with
`Allow: GET, HEAD` before the page runs ([`ViewGuard`](../../src/uk.osric.asp.Web/Authentication/ViewGuard.cs)).

The gap matters because a page that writes is a page whose form has to be tried. A run that can read
the editor and cannot press its Save button cannot answer "did the form I just wrote work?". It can
only read the source and guess.

This is not a tool-shape question. `auth-a-view-token-reads-only` is what makes an unapproved view
safe: the token names the run, reaches nothing but `Site:Origin`, and cannot write. A POST can write
to the database through page code, so it is a write wearing a read's clothes, and it needs three
things the view deliberately lacks:

- a credential that may write, or a second scope on the same token;
- the approval gate, because the run would be changing the store;
- the antiforgery token a page's state-changing form expects, which the run must fetch and carry.

The last one is the awkward part: the run would read the form, extract the field, and post it back,
which is a small workflow rather than a single call.

## Sketch

1. **Decide the credential first.** A write-scoped token per run, minted the way the read token is,
   or a separate grant the owner gives at the approval. The read token's guarantee is not weakened.
2. **Decide the method gate.** A `POST` (and any other unsafe method) is an approval, not a read; a
   `GET`/`HEAD` stays unapproved.
3. **Decide the write scope.** Posting to the editor is one thing; posting to any page is another,
   because a page's POST runs page code. The item's answer says which paths a run may reach.
4. **Carry the token.** A form's antiforgery field is read from the previous `GET`; the result says
   where it came from rather than silently dropping it.
5. **Bound the body.** The request body comes from `/tmp` or the call, under the same size limit the
   response has, and the result is the response as the transport produced it.
6. **Keep the read path unchanged.** A view that cannot POST still needs no approval, and that is the
   property to preserve.

The same question is a shell command once [`tool-surface-is-one-shell`](tool-surface-is-one-shell.md)
lands: `curl -X POST -d @/tmp/body /some/path`. The security split — on-site as the owner, off-site
anonymous and approved — has to be settled either way, and that plan defers it to a review.

## Open questions

- **May a run POST at all, or only to the editor's own actions?** The narrow version tests the site's
  forms; the wide version runs any page's state-changing code, which is a bigger grant.
- **Which credential?** A write-scoped view token is the smallest change; a per-run "submit" grant
  asks the owner more explicitly but is a second thing to revoke.
- **Does an approved POST carry the antiforgery token automatically?** The run can read the form, but
  a card that says "POST with this body" is easier to approve than one that says "POST, and the page
  will tell you what it wanted".
