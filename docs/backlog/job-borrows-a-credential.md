<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A job acts with its starter's credential, and loses it when that credential dies

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | high |
| Status | open |
| Blocked by | — |
| Plan | — |
| Area | Sdk, Web (work and authentication), docs, assistant |
| Location | `src/uk.osric.asp.Web/Work/PageJob.cs`, `PageJobRunner.cs`, `PageJobs.cs`, `src/uk.osric.asp.Web/Pages/PageJobsScope.cs`, `PageWritesScope.cs`, `PageJobEnvironment.cs`, `src/uk.osric.asp.Sdk/IWorkerContext.cs` |

> **Written 2026-11-18** at the owner's direction. Its prerequisite was the page transaction, which
> closed on 2026-11-20: a job can now be offered a writer, and what is left is the credential. Nothing
> is built until a plan answers the open questions below.

## Idea

A job carries `PageUser Owner` and every check compares the name, so a job's authority is a string
that never expires. A page has the same authority but holds it through a live request: the cookie's
ticket is a row that sign-out deletes, and the account's security stamp is compared on every request.
A job outlives the request that started it, so "the caller" is a snapshot, not a credential — and
that is the whole reason `IPageWrites` is withheld ([`conversation-is-not-a-page`](conversation-is-not-a-page.md) §4). Give a job the
credential and revocation comes with it.

The credential is whichever one the starting render presented: the cookie's ticket key
(`AuthTicket.Key`) or a run's view secret (`ViewToken.Secret`). Both are revocable today, by deleting
the row (`users` changes, sign-out, a new sign-in on the same browser) or by forgetting the view
token when the run ends. The worker never sees either secret: the capability is constructed bound,
exactly as `PageWritesScope` is bound to `user` today, so there is nothing in stored page code to
leak and nothing new on the page contract.

## Sketch

1. **The credential, not the name.** At start, `PageJobsScope` captures a `PageJobAuthority` from the
   request — owner, security stamp and the credential handle (ticket key, or view secret) — and
   `PageJob` holds it for the job's life. `IWorkerContext.User` stays the plain `PageUser` it is: a
   label, not the authority. A job started from a `GET` render is possible today, so the run's view
   scheme is a branch of this and not a later idea.
2. **The check.** One host-side call answers live or dead: the ticket row still exists and the
   account still exists with the same stamp; or the view token still resolves and the stamp matches.
   The two checks are the ones `OnValidatePrincipal` and `ViewAuthenticationHandler` already make, so
   there is no second definition of "signed in".
3. **The writer.** `PageJobRunner` offers `IPageWrites` bound to the **starting page** through the
   same `PageWritesScope`, which now offers a transaction a page opens and commits
   (`pages-writes-are-transactions`). The first version offers that and the bound self-save; the named
   save follows the same answer the page store already gives.
4. **The clocks.** A write is activity: it re-arms `Pages:Jobs:SilenceSeconds` as `Publish` does, or
   a compile longer than the silence limit cuts the job mid-save. A save that has begun is still
   never abandoned (`pages-a-page-may-save-itself`).
5. **The refusal.** A dead authority throws `UserFacingException` in the existing house style
   ("The account that started this job is no longer signed in. Nothing was saved."); a worker that
   lets it escape fails the job with that reason on the journal. The job's own child `StartAsync` is
   gated by the same check. Storage, publishing and `ReceiveAsync` are not identity-bearing and keep
   working: the authority dies, the job does not have to.
6. **The records.** The merge writes **pages-a-jobs-authority-is-its-starters-credential** and edits:
   `pages-a-worker-gets-no-response` (the renderer and the layout stay out by shape; the writer comes
   in by authority), `pages-any-page-may-write-the-store` (the authority is the presented credential),
   `pages-jobs-run-a-worker-for-the-page` ("owned by the visitor" becomes "owned by the credential"),
   and `auth-sign-in-is-long-lived-and-revocable` (a job's authority ends with its own ticket, not
   with a stamp rotation, so other devices keep theirs). `/help` and `PageAuthorPrompt` gain the rule,
   with the prompt's `Version` bumped (`product-manual-is-a-page`).

## Open questions

1. **Which keys a job may write.** Any unreserved key is what the page store already allows; only the
   starting page is the smallest first version. The bound self-save is the starting page either way.
2. **Check cadence.** Once per batch is cheap and closes most of the window; per edit is stricter and
   costs one indexed read each time. A write is the only place the answer changes anything.
3. **What a dead authority does to the job.** Refuse the action and let the worker decide, or end the
   job with the reason. The first terminates the authority; the second terminates the work.
4. **The view-scheme handle.** A job started from a run's view would hold a live bearer secret in
   memory. It never leaves the process, but whether it is the secret itself or an in-process handle
   minted from it is the plan's decision.
5. **Whether a job may start a job at all** once a child's authority is checked the same way. Today it
   can; a child would inherit the same dead-able credential, which is probably right but is the plan's
   to confirm.

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
[`conversation-is-not-a-page`](conversation-is-not-a-page.md) §4; the transaction it needed has landed (`pages-writes-are-transactions`).
