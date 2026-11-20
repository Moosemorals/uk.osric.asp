<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A job acts with the credential that started it

| | |
| --- | --- |
| Status | Merged 2026-11-20 — built on *idea/job-borrows-a-credential* and merged into trunk with `make check` green. The recorded name is `pages-a-jobs-credential-is-borrowed`, and this plan is frozen with the work: a statement here the code has since overtaken is expected rather than a defect |
| Size | Medium: one captured authority, one verifier, one credential seam, a writer on a job's capability list, one outcome on five enums, one refusal on job calls, three docs, `/help`, the prompt and its version, one decision and five edits |
| Area | Sdk, Web (work, authentication, pages), docs, assistant, tests |
| Spike | None. Every claim below was read off `trunk` at `4428e4a` on 2026-11-20 |
| Backlog | [`job-borrows-a-credential`](../../backlog/closed.md) was this step, and it closed with this plan |
| Decisions | `pages-a-jobs-credential-is-borrowed`, recorded with this plan. It revises `pages-a-worker-gets-no-response`, `pages-any-page-may-write-the-store`, `pages-jobs-run-a-worker-for-the-page`, `auth-sign-in-is-long-lived-and-revocable` and `auth-a-view-token-names-its-run`, and relies on `pages-writes-are-transactions`, `pages-a-page-may-save-itself`, `auth-a-view-that-does-not-verify-is-refused`, `pages-a-job-is-not-durable`, `product-manual-is-a-page` and `product-steps-wait-for-the-owner` |

> **Reconciled with the landed transaction — 2026-11-20.** The page transaction merged as `4428e4a`
> with a frame rather than a second set of write members: `IPageTransaction` carries only
> `CommitAsync` and `RollbackAsync`, and a write made on `IPageWrites` while it is open joins it and
> answers `Staged` (`pages-writes-are-transactions`). The plan is written against that shape — the
> class is `PageWriteTransaction`, the gate is `PageTransactionGate`, and the check lives in the
> commit. The item was `planned` rather than blocked once that landed.

## The short version

A job outlives the request that started it. Its owner is a `PageUser` — a name — and a name never
expires, which is why the store's writer is withheld from a worker today. This step hands the job the
**credential** the starting render presented instead of the name alone, and makes every write ask
whether it is still live. It is the question the cookie's `OnValidatePrincipal` and the view's
`ViewAuthenticationHandler` already ask on every request, asked once more at the commit.

- **A borrowed credential, held by the host.** `PageJobAuthority` is the owner, the account's security
  stamp and one handle: the cookie's ticket key, or a run's id. The endpoint captures it once per
  render, `PageJob` keeps it for the job's life, and it is never handed to page code — the writer is
  constructed bound, exactly as `PageWritesScope` is bound to the visitor today.
- **A handle, not a secret.** A sign-in's handle is the ticket key the host already keeps in the
  ticket row; a view's is the run id, not the bearer secret. Neither is reachable from a worker, and
  neither is written anywhere new.
- **The writer, bound to the starting page.** The job's capability set gains `IPageWrites`, built from
  the same `PageWritesScope` a render uses, with the `PageEditor` the host resolves from a scope of
  the job's own. Every key the store allows is writable, as it is for a page; the bound save is the
  starting page.
- **One check per write.** A lone write outside a transaction checks at its own commit; a write that
  joins an open transaction, and the transaction itself, check once at that commit. Nothing is
  checked per staged call, and nothing is checked for a read.
- **A refusal, not an exception.** A dead credential writes nothing and every staged operation's row
  in the commit answers `Revoked`; the bound save and the single writes answer the same outcome
  through their own shapes. The job keeps its storage, its journal and its channel, and the worker
  decides what to do.
- **A job may start a job.** A child inherits the same authority; its start is refused `Revoked` once
  that authority is dead, and nothing else about starting changes.

## The owner's answers

The item put five questions to the owner and they were answered on 2026-11-20. They are what the
design below is written against, and the merge records them in the decision:

1. **Any key it has permission for, no arbitrary restrictions.** A job gets the same `IPageWrites` a
   page does, over the whole key space the shell routes; only the reserved keys are refused. The
   bound save is the starting page whatever else a worker writes.
2. **Once per write, and once per commit when transactions land.** A lone write with nothing open is
   a transaction of one and is checked at its own commit; a write that joins an open transaction, and
   the transaction itself, are checked once at that commit, not per staged call.
3. **Refuse and let the worker decide, and not an exception.** A dead authority answers a value and
   stores nothing; the worker chooses whether to stop, retry or publish what it has. The code is not
   broken because a sign-in ended, so nothing throws.
4. **A handle, not the token.** A job started from a run's view holds the run's id and asks
   `ViewTokenStore` whether it is still live; the bearer secret never reaches the job, and the fixed-time
   comparison the request already made is unchanged.
5. **A job may start a job.** There is no depth or key restriction; a child inherits the same
   dead-able credential and its start is checked the same way.

## What exists today

- **A job carries a name.** [`PageJobsScope`](../../../src/uk.osric.asp.Web/Pages/PageJobsScope.cs) holds
  `PageUser? user` and passes it to [`PageJobs.StartAsync`](../../../src/uk.osric.asp.Web/Work/PageJobs.cs),
  which stores it as [`PageJob.Owner`](../../../src/uk.osric.asp.Web/Work/PageJob.cs); every job call is
  filtered by `job.Owner.Name`, so another account's id reads as unknown.
  [`PageJobRunner.Context`](../../../src/uk.osric.asp.Web/Work/PageJobRunner.cs) builds the worker's
  capabilities without `IPageWrites` and with `PageJobsScope` bound to `job.Owner`.
- **A write is one path, and the transaction is a frame.**
  [`PageWritesScope`](../../../src/uk.osric.asp.Web/Pages/PageWritesScope.cs) refuses an anonymous render
  with `UserFacingException`; a member called with no transaction open opens a
  [`PageWriteTransaction`](../../../src/uk.osric.asp.Web/Pages/PageWriteTransaction.cs), stages, commits
  and answers, and one called while the render has a transaction open joins it and answers `Staged`
  (`pages-writes-are-transactions`). `PageWriteTransaction.CommitAsync` is where every write, lone or
  batched, reaches the store, applies its operations in order and passes `CancellationToken.None`, so
  a write that has begun is not abandoned.
- **The two checks already exist.** `OnValidatePrincipal`
  ([`AuthenticationExtensions`](../../../src/uk.osric.asp.Web/Authentication/AuthenticationExtensions.cs))
  compares the account's live security stamp, and the cookie is a row: `AuthTicketStore.Find(key)`
  answers it and `AuthTicket.ExpiresAt` is swept hourly. `ViewTokenStore.ForRun(runId)` answers a
  live run's token, `Forget` is what makes it dead, and
  [`ViewAuthenticationHandler`](../../../src/uk.osric.asp.Web/Authentication/ViewAuthenticationHandler.cs)
  compares the account's stamp with the token's.
- **The endpoint holds the principal and the request.** [`PageEndpoint`](../../../src/uk.osric.asp.Web/Pages/PageEndpoint.cs)
  maps the principal to `PageUser` once; [`PageChain`](../../../src/uk.osric.asp.Web/Pages/PageChain.cs)
  builds a `PageJobsScope` for every layer that is not a layout.
- **Nothing else counts as activity.** `WorkerContext.Publish` re-arms a job's silence clock; a
  returned `ReceiveAsync` does; a write does not, because there is no write.
- **The answers are the row's views.** `PageSaveResult`, `PageEditResult`, `PageMoveResult` and
  `PageDeleteResult` already carry an outcome mapped from the row
  ([`PageWriteResult`](../../../src/uk.osric.asp.Sdk/PageWriteResult.cs),
  [`PageWriteOutcome`](../../../src/uk.osric.asp.Sdk/PageWriteOutcome.cs)); `PageDeleteResult` is already
  the row's view, and `PageEditOutcome`, `PageMoveOutcome` and `PageDeleteOutcome` already carry
  `Busy` and `Staged` beside the write's own outcomes.
- **The writer is documented as absent.** `IWorkerContext`'s remarks, `docs/pages.md` §Jobs, the
  seeded `/help` and `PageAuthorPrompt` all say a worker is offered no writer.

## The design

### 1. The authority is captured once, from the request

`PageJobAuthority` is a Web type, never an `Sdk` one: owner, security stamp, and one handle.

- **A sign-in** carries the ticket key from the request cookie (`ShellCookies.Authentication`) and the
  stamp from `EditorPolicy.SecurityStampClaim` on the principal.
- **A view** carries the run id from the `ViewToken.Claim` claim, and the stamp from
  `ViewTokenStore.ForRun(runId)`. The secret is what the request presented; the job receives only the
  id it was minted under.
- **No handle, no authority.** A principal with no cookie and no run claim is not a credential the
  host can keep alive, so it produces no authority and a job start is `Anonymous` as it is today. A
  test that fabricates a principal therefore gains a cookie, as a real render has one.

`PageJobAuthorityFactory` (singleton, holding `ViewTokenStore` and the verifier) turns
`(ClaimsPrincipal, HttpRequest)` into an authority or null; `PageEnvironment` carries it,
`PageEndpoint` calls it beside `MapUser`, and `PageChain` hands it to every layer's `PageJobsScope`.
One authority per render, not one per layer.

### 2. One host-side call answers live or dead

`PageJobAuthorityVerifier` (singleton: `IServiceScopeFactory`, `AuthTicketStore`, `ViewTokenStore`,
`TimeProvider`) answers the question the two authentication paths answer:

1. the account exists and its current `SecurityStamp` equals the captured one; and
2. the handle still resolves — `AuthTicketStore.Find(key)` names the same account and has not expired,
   or `ViewTokenStore.ForRun(runId)` still names that account.

The user read is scoped, so the verifier opens a scope per check and closes it, the way
`AuthTicketStore` already does. The ticket's expiry is compared with the clock rather than left to
the hourly sweep: a job's authority ends when the browser's sign-in does, not up to an hour later.

The permission seam is one check, and it is separate from the job's clock:

- **`IPageWriteCredential`** — an interface in `Pages` with one member,
  `ValueTask<bool> IsLiveAsync(CancellationToken)` — is the question a commit asks. `PageJobAuthority`
  implements it over the verifier, so the captured value is the thing that answers, and it writes the
  refusal line at Warning when the answer is no (the runner's log scope puts the job's id on it).
- **The activity signal is a delegate**, `Action? touched`, which the runner binds to
  `JobLifetime.ReArm`. It is not a permission: a render has a deadline, not a silence clock, so there
  is nothing for a page to touch, and a type would only be a name for a call.

A render passes neither, so a page's write costs no extra read — the request's own authentication has
just done it. The verifier and the outcome vocabulary are shared by both callers already; only the
cadence differs. If a page ever needs the check — a long streaming render, a request stopped while it
unwinds — it passes its own `IPageWriteCredential` instead of null, the commit asks it, and a dead
cookie answers `Revoked` for the rest of that render. That is a policy choice the seam leaves open,
not a redesign, and it is not worth its two reads per commit today.

### 3. The job keeps the authority, and the name is derived from it

`PageJob` holds the `PageJobAuthority` for its life; `Owner` becomes `Authority.Owner`, and the
`PageJobs` filters keep comparing the name because that is the label a viewer sees. `PageJobRequest`
carries the authority through the queue. Nothing is added to `PageJobInfo`: an authority is the
host's, and a viewer has no business reading a ticket key back out of a journal.

`PageJobs.StartAsync` takes the authority instead of `PageUser?`; an absent authority is `Anonymous`
as before. A start from a job's scope touches the clock — `touched` — because starting work is not
silence, and checks the credential first, for the reason in section 8; a render's start does neither.

### 4. The runner offers the writer

`PageJobEnvironment` gains the `IServiceScopeFactory` the host already has, and `RunAsync` opens one
`AsyncServiceScope` per job, disposing it when the job ends. `PageEditor` is scoped and holds the
store's `StorageDbContext`; one scope per job gives the writer the same store a render's writer has,
and a per-call scope could not outlive a transaction that spans calls.

`Context` then builds the capability set with:

```csharp
PageTransactionGate gate = new();
IPageWrites writes = new PageWritesScope(
    job.Page, editor, job.Owner, writable: true, environment.Reserved, environment.Seeds,
    gate, job.Authority, job.Lifetime.ReArm);
writes.AttachDeadline(job.Lifetime.Token);
capabilities.Add<IPageWrites>(writes);
```

The gate is the job's own, because a job commits as many transactions as it has work for and they
must not join a render's; the path is the starting page, so the bound save repairs the page that
started the job. The writer's deadline is the job's token, so staging stops when the job is cut and a
commit that already began still finishes. The renderer, the layout, the composition and the response
stay absent by shape (`pages-a-worker-gets-no-response`).

A worker makes one write at a time, exactly as a render does. The gate is a frame and not a lock: a
worker that writes from two tasks at once would have one task's transaction joined by the other. That
is the page's rule, unchanged, and `/help` says it for a worker as it does for a page.

### 5. The check happens where every write converges

`PageWritesScope` and `PageWriteTransaction` gain an optional `IPageWriteCredential` and an optional
`Action? touched`, passed from the scope into every transaction its `Open()` makes. Every staging
entry on the transaction — `SaveOwnAsync`, `SaveAsync`, `MoveAsync`, `DeleteAsync`, `RestoreAsync`,
`AdoptAsync`, `InstallAsync` — and `CommitAsync` call `touched`. The outermost commit — and only it —
asks the credential's `IsLiveAsync` before `pages.CommitAsync`, because it is the one that stores; a
nested commit absorbs into its parent as it does today.

- **Once.** One check per transaction's commit, before the store commit; a staged call is never
  checked. A write made on the capability while a transaction is open joins it and answers `Staged`,
  so its answer is the commit's, exactly as it is for a page.
- **Every staged operation is refused, none is skipped.** A dead authority marks each staged
  operation's row `Revoked` — the rows in `_rowOfOperation`, which is the same set a rollback marks
  `NotAttempted` — and returns without touching the store. A row that answered `Missing` or
  `Unchanged` at staging keeps that answer, because it was never going to write.
- **Nothing was attempted.** `Revoked` is not `NotAttempted`: every operation was refused by the
  credential, not by an earlier refusal in the report.
- **Not abandoned.** The check and the store commit use `CancellationToken.None`, as the store
  commit already does. The job's clocks bound staging — the wait for a compile slot and the compile
  itself — not a commit that has begun (`pages-a-page-may-save-itself`).
- **The compiler's fault is not rethrown.** A refused commit stored nothing, so the worker's answer is
  the refusal; the fault belongs to a save that never happened. The credential's refusal line is the
  log.
- **Nothing else changes.** The reserved keys, the addressable-key check, the deadline, the anonymous
  refusal, the ordering of operations and the nesting rules are the ones the page already has.
  `RollbackAsync` and a transaction that is disposed need no check: neither writes.

### 6. The refusal is a value on every answer

A dead credential answers **`Revoked`** and stores nothing. The word names the three ways a
credential dies — the ticket row deleted, the stamp rotated, the run's token forgotten — and it is the
same value everywhere:

| Answer | Shape |
| --- | --- |
| A commit's report | each staged operation's `PageWriteOutcome` is `Revoked`, not `NotAttempted` |
| A write inside an open transaction | `Staged` when it is called, then `Revoked` in the commit's report |
| A named save | `PageEditOutcome.Revoked`, no hash, no diagnostics |
| A move | `PageMoveOutcome.Revoked`, no hash, no keys below |
| A delete | `PageDeleteOutcome.Revoked`, so `Deleted` is false and `WasLive` says what the read saw |
| The bound save | `PageSaveResult.Outcome` is `PageEditOutcome.Revoked`, `Hash` is empty, diagnostics are empty |
| A child start | `PageJobStart.Refusal` is `PageJobRefusal.Revoked` |

The five enums the row already feeds gain the one member: `PageWriteOutcome`, `PageEditOutcome`,
`PageMoveOutcome`, `PageDeleteOutcome` and `PageJobRefusal`. No record changes shape — `PageSaveResult`
already carries `Outcome`, `PageDeleteResult` is already the row's view, and a revoked bound save
answers the empty hash the capability already uses when there is none, documented as "nothing was
stored". A page never meets the outcome at run time, because a render holds no credential to ask;
only a worker reads it.

### 7. A write is activity

The `touched` delegate re-arms `Pages:Jobs:SilenceSeconds` at every stage and at the commit, the way
`Publish` and a returned `ReceiveAsync` do. A compile longer than the silence limit still cuts the job, because the
clock is not paused by a write the way `ReceiveAsync` pauses it; a commit that has begun is not cut,
for the reason above. The ceiling is untouched: a worker that writes forever still ends at
`MaxDurationSeconds`.

### 8. A job may start a job

Every `PageJobsScope` gets the authority, and a job's also gets the `touched` delegate. `StartAsync`
asks the credential whenever there is one before it queues a child, and answers
`PageJobRefusal.Revoked` when it is dead. A render's start asks too, rather than branching on who is
calling: it is one read the request made a moment ago, and one path is cheaper than a flag. The child
holds the authority it was started with, so it dies with the same sign-in. There is no depth limit and
no key restriction (the owner's answer).

`SendAsync`, `GetAsync`, `ListAsync`, `ReadEventsAsync` and `CancelAsync` are not checked: they are
not writes, they are already filtered by the owner's name, and a job whose credential died can still
read and cancel its own work while the worker decides what to do next.

### 9. What stays unguarded, and why

Storage, fetch, publishing, receiving and the job handle are not identity-bearing. The starting page's
storage is the page's data, not the visitor's authority; fetch is bound to the worker key, not the
caller; and a journal is the job's own. The authority dies; the job does not have to.
`pages-a-job-is-not-durable` stands.

### 10. The manual, the prompt and the docs

- The seeded `/help` gains the rule under **Starting a job**: a job borrows the sign-in that started it;
  a sign-out, a new sign-in in the same browser, `users passwd`, `users remove` or the end of a run's
  view kills it; a write then answers `Revoked` and stores nothing; the worker decides what to do; a
  write is activity; a job may start a job and the child inherits the credential. The paragraph that
  says a worker is offered no writer is replaced, and the writing section says `Revoked` is a worker's
  answer and never a page's (`product-manual-is-a-page`).
- `PageAuthorPrompt` gains the same rule under **Jobs** and its `Version` moves from
  `page-author-v53` to `page-author-v54`. It says the assistant may use the writer from a worker
  where a page may, the authority is the run's view or the owner's sign-in, a write inside an open
  transaction answers `Staged` and can be `Revoked` at the commit, and `Revoked` means stop or
  publish — not retry blindly.
- `docs/pages.md` records the writer in both places it says what a worker is offered — the
  `IPageWrites` bullet and §Jobs — with the borrowed authority, the one-write-at-a-time rule and
  `Revoked`; the capability bullet for `IPageJobs` keeps its words about filtering by visitor.
- `docs/shell.md` §A run's view gains the consequence: a job a page starts under a run holds the run's
  id, so the run ending is what ends the job's writes.
- `docs/assistant.md` needs no change: the port table that named the page-store write authority as a
  blocker was retired before this landed, so there is nothing there to lose.
- `PageWritesScope`'s own remark still calls a second write a page bug, which the transaction made
  untrue; the change fixes the sentence while it is in the file.

## The contract changes

| File | Change |
| --- | --- |
| [`PageWriteOutcome.cs`](../../../src/uk.osric.asp.Sdk/PageWriteOutcome.cs) | `Revoked` |
| [`PageEditResult.cs`](../../../src/uk.osric.asp.Sdk/PageEditResult.cs) | `PageEditOutcome.Revoked` |
| [`PageMoveResult.cs`](../../../src/uk.osric.asp.Sdk/PageMoveResult.cs) | `PageMoveOutcome.Revoked` |
| [`PageDeleteResult.cs`](../../../src/uk.osric.asp.Sdk/PageDeleteResult.cs) | `PageDeleteOutcome.Revoked` |
| [`PageJobRefusal.cs`](../../../src/uk.osric.asp.Sdk/PageJobRefusal.cs) | `Revoked` |
| [`PageSaveResult.cs`](../../../src/uk.osric.asp.Sdk/PageSaveResult.cs) | doc comment: `Hash` is empty when nothing was stored |
| [`IWorkerContext.cs`](../../../src/uk.osric.asp.Sdk/IWorkerContext.cs) | remarks: the writer is offered, bound to the starting page and the starting credential |
| [`IPageWrites.cs`](../../../src/uk.osric.asp.Sdk/IPageWrites.cs), [`IPageTransaction.cs`](../../../src/uk.osric.asp.Sdk/IPageTransaction.cs) | the `Revoked` answer, and that a render never sees it |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | the new members |

## The checks

- **`PageJobAuthorityTests`** (new): a live cookie authority is live; deleting the ticket row, rotating
  the stamp, or passing the ticket's expiry each makes it dead; a live view authority is live and
  forgetting the run's token makes it dead; a view whose account's stamp moved is dead. The cookie
  branch uses `DatabaseTemplate`, as `StoredTicketDataFormatTests` does.
- **`PageJobWritesTests`** (new): a worker saves a page it does not own while its authority is live; a
  sign-out between two writes answers `Revoked`, stores nothing, and the job publishes and finishes on
  its own terms; a transaction of three writes reports three `Revoked` rows after one check (a counting
  credential proves the cadence); a refused commit does not throw; a worker whose credential is dead
  is refused `Revoked` for a child start.
- **`PageWritesScopeTests`**: a render's write holds no credential and the existing answers do not
  change; a lone write made while a transaction is open answers `Staged`, the commit asks the credential
  once for every staged operation, every staged row answers `Revoked` when it says no — and clears the
  hash it would have left — and each staging call plus the commit touches the clock.
- **`PageJobRenderTests`**, **`PageHarness`**: a render's `PageJobsScope` and `PageWritesScope` gain the
  authority and neither of the job's two collaborators; the principal that starts a job carries a
  cookie row.
- **`Web.IntegrationTests/PageJobTests`**: the real host offers a worker `IPageWrites`, and the page
  it saves is served on the next request — one test proving the wiring, not the revocation.
- **`PageAuthorPromptTests`**: the prompt's jobs section carries the rule and the version moved.

## What the merge recorded

The merge wrote `pages-a-jobs-credential-is-borrowed` — a job's authority is the credential the starting
render presented, checked at each commit, not the visitor's name — and edited:

- `pages-a-worker-gets-no-response`: the renderer, layout and response stay out by shape; the writer
  comes in, bound to the credential.
- `pages-any-page-may-write-the-store`: the authority is the presented credential — a render's
  sign-in, or the one a job borrowed from the render that started it.
- `pages-jobs-run-a-worker-for-the-page`: "owned by the visitor who started it" becomes "owned by the
  credential that started it", and a job's calls stay filtered by the owner's name.
- `auth-sign-in-is-long-lived-and-revocable`: a job's borrowed authority ends with the ticket it
  borrowed, so signing out one browser ends only that browser's jobs; a stamp rotation ends every job
  as it ends every sign-in.
- `auth-a-view-token-names-its-run`: a job started under a view holds the run's id, so the run's end
  is what ends the job's writes.

The name this plan proposed was eight words where the record allows two to six, so it was narrowed at the
merge to `pages-a-jobs-credential-is-borrowed`, which states the same rule and fits. Nothing on trunk
cited the longer form, so nothing moved with it.

## Not in scope

- **A durable job, a jobs table or a stored authority.** `pages-a-job-is-not-durable` stands: the
  authority lives in memory with the job, and a restart ends both.
- **A configured worker account.** Rejected in [`conversation-is-not-a-page`](../../backlog/conversation-is-not-a-page.md):
  the authority is the starter's credential or nothing.
- **A per-key restriction.** The owner answered for the whole key space; the reserved keys are the
  only refusal, as they are for a page.
- **Checking a staged call, or a read.** One check per commit; `Resolve` and `SeedSourceAsync` are
  reads and spend none.
- **Session, layout, the renderers, composition and the response.** Absent by shape, not by distrust.
- **Fetch credentials.** Bound to the worker key, not the caller, so revocation does not apply.
- **A "you are revoked" event.** The refusal is the answer, and a worker that wants a viewer to know
  publishes it itself.

## What would make it unnecessary

If the write a job needs belonged to an account-scoped store
([`no-account-store`](../../backlog/no-account-store.md)) rather than to the page store, the job would write
rows and not source, and no page-store authority would be involved.

**Origin:** the owner, 2026-11-18 — "the worker/job gets to write with all the authority of the
caller, and I'd really like to terminate that authority if the caller logs out or otherwise
invalidates their session, so probably going to need to hand the job a token". The withheld writer is
[`conversation-is-not-a-page`](../../backlog/conversation-is-not-a-page.md) §4; the transaction it needed has
landed (`pages-writes-are-transactions`).
