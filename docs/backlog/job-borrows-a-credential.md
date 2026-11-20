<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A job acts with its starter's credential, and loses it when that credential dies

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [job-borrows-a-credential](../plans/job-borrows-a-credential.md) |
| Area | Sdk, Web (work and authentication), docs, assistant |
| Location | `src/uk.osric.asp.Web/Work/PageJob.cs`, `PageJobRunner.cs`, `PageJobs.cs`, `src/uk.osric.asp.Web/Pages/PageJobsScope.cs`, `PageWritesScope.cs`, `PageWriteTransaction.cs`, `IPageWriteCredential.cs`, `PageJobEnvironment.cs`, `src/uk.osric.asp.Web/Authentication/PageJobAuthority.cs`, `PageJobAuthorityVerifier.cs`, `src/uk.osric.asp.Sdk/IWorkerContext.cs` |

> **Written 2026-11-18** at the owner's direction. Its prerequisite was the page transaction, which
> closed on 2026-11-20: a job can now be offered a writer, and what is left is the credential. The plan
> is written and waits for the owner; nothing is built until it is agreed.

> **The five questions are answered — 2026-11-20.** The owner's answers are what the plan is written
> against, in the order the questions were asked:
>
> 1. **Any key it has permission for, no arbitrary restrictions.** A job gets the same `IPageWrites` a
>    page does over the whole key space the shell routes; only the reserved keys are refused, and the
>    bound save is the starting page whatever else a worker writes.
> 2. **Once per write, and once per commit when transactions land.** A lone write with nothing open
>    checks at its own commit; a write that joins an open transaction, and the transaction itself, are
>    checked once at that commit, not per staged call.
> 3. **Refuse and let the worker decide, and not an exception.** A dead authority answers a value —
>    `Revoked` — and stores nothing; the worker chooses whether to stop, retry or publish what it has.
>    The code is not broken because a sign-in ended, so nothing throws.
> 4. **A handle, not the token.** A job started from a run's view holds the run's id and asks
>    `ViewTokenStore` whether it is still live; the bearer secret never reaches the job.
> 5. **A job may start a job.** No depth or key restriction; a child inherits the same dead-able
>    credential, and its start is checked the same way.

## Idea

A job carries `PageUser Owner` and every check compares the name, so a job's authority is a string
that never expires. A page has the same authority but holds it through a live request: the cookie's
ticket is a row that sign-out deletes, and the account's security stamp is compared on every request.
A job outlives the request that started it, so "the caller" is a snapshot, not a credential — and
that is the whole reason `IPageWrites` is withheld ([`conversation-is-not-a-page`](conversation-is-not-a-page.md) §4). Give a job the
credential and revocation comes with it.

The credential is whichever one the starting render presented: the cookie's ticket key
(`AuthTicket.Key`) or a run's view, held by its run id rather than the bearer secret the request
presented (`ViewToken.Secret`). Both are revocable today, by deleting the row (`users` changes,
sign-out, a new sign-in on the same browser) or by forgetting the view token when the run ends. The
worker never sees either secret or the run id: the capability is constructed bound, exactly as
`PageWritesScope` is bound to `user` today, so there is nothing in stored page code to leak and nothing
new on the page contract.

## Sketch

1. **The credential, not the name.** `PageEndpoint` captures a `PageJobAuthority` from the request —
   owner, security stamp and the credential handle (ticket key, or run id) — `PageChain` hands it to
   every layer's `PageJobsScope`, and `PageJob` holds it for the job's life. `IWorkerContext.User`
   stays the plain `PageUser` it is: a label, not the authority. A job started from a `GET` render is
   possible today, so the run's view scheme is a branch of this and not a later idea.
2. **The check.** One host-side call answers live or dead — `IPageWriteCredential.IsLiveAsync`, which
   the captured authority implements and a render may hold later: the ticket row still exists and the
   account still exists with the same stamp; or the view token still resolves and the stamp matches.
   The job's silence clock is a separate signal (`Action touched`), because a write being activity is
   not a permission.
   The two checks are the ones `OnValidatePrincipal` and `ViewAuthenticationHandler` already make, so
   there is no second definition of "signed in".
3. **The writer.** `PageJobRunner` offers `IPageWrites` bound to the **starting page** through the
   same `PageWritesScope`, which now offers a transaction a page opens and commits
   (`pages-writes-are-transactions`). The first version offers that and the bound self-save; the named
   save follows the same answer the page store already gives.
4. **The clocks.** A write is activity: it re-arms `Pages:Jobs:SilenceSeconds` as `Publish` does, or
   a compile longer than the silence limit cuts the job mid-save. A save that has begun is still
   never abandoned (`pages-a-page-may-save-itself`).
5. **The refusal.** A dead authority answers `Revoked` and writes nothing: every staged operation's
   row says so, a write made inside an open transaction answers `Staged` and then `Revoked` at that
   commit, the bound save and the single writes answer it through their own shapes, and nothing throws —
   the code is not broken because a sign-in ended. The worker decides whether to stop, retry or
   publish what it has, and the job keeps its storage, journal and channel. The job's own child
   `StartAsync` is gated by the same check and refused `Revoked` once the authority is dead. Storage,
   publishing and `ReceiveAsync` are not identity-bearing and keep working: the authority dies, the
   job does not have to.
6. **The records.** The merge writes **pages-a-jobs-authority-is-its-starters-credential** and edits:
   `pages-a-worker-gets-no-response` (the renderer and the layout stay out by shape; the writer comes
   in by authority), `pages-any-page-may-write-the-store` (the authority is the presented credential),
   `pages-jobs-run-a-worker-for-the-page` ("owned by the visitor" becomes "owned by the credential"),
   `auth-sign-in-is-long-lived-and-revocable` (a job's authority ends with the ticket it borrowed, so
   signing out one browser ends only that browser's jobs) and `auth-a-view-token-names-its-run` (the
   run's end is what ends a job started under its view). `/help` and `PageAuthorPrompt` gain the rule,
   with the prompt's `Version` bumped (`product-manual-is-a-page`).

## Not in scope

- **Session, layout, the markdown and diff renderers, composition, the response.** Absent by shape,
  not by distrust: a job has no request and no document, so there is nothing to bind them to.
- **Fetch credentials.** Bound to the worker key, not the caller, so revocation does not apply.
- **Durable jobs and a jobs table.** `pages-a-job-is-not-durable` stands.
- **A configured worker account.** Rejected in [`conversation-is-not-a-page`](conversation-is-not-a-page.md): the authority is the starter's
  credential or nothing, never a configured name.

## What would make it unnecessary

If the write a job needs belonged to an account-scoped store ([`no-account-store`](no-account-store.md)) rather than to
the page store, the job would write rows and not source, and no page-store authority would be
involved.

**Origin:** the owner, 2026-11-18 — "the worker/job gets to write with all the authority of the
caller, and I'd really like to terminate that authority if the caller logs out or otherwise
invalidates their session, so probably going to need to hand the job a token". The withheld writer is
[`conversation-is-not-a-page`](conversation-is-not-a-page.md) §4; the transaction it needed has landed
(`pages-writes-are-transactions`).
