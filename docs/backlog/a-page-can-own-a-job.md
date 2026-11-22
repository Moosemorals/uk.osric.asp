<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A job needs a signed-in visitor, so an inbound request cannot start one

| | |
| --- | --- |
| Difficulty | protective-dragon |
| Utility | high |
| Status | blocked |
| Blocked by | `a-page-sees-its-request`, `the-site-is-one-type` |
| Plan | — |
| Area | Sdk, Web (work, pages), docs |
| Location | `src/uk.osric.asp.Sdk/IPageJobs.cs`, `src/uk.osric.asp.Web/Pages/PageJobsScope.cs`, `src/uk.osric.asp.Web/Work/PageJobs.cs`, `PageJobRunner.cs`, `PageJob.cs`, `WorkerContext.cs`, `src/uk.osric.asp.Web/Pages/Seeds/help`, `docs/pages.md`, `docs/decisions.md` |
| Spike | — |

## Idea

Work that outlives a request exists, and only a person can start it. `PageJobs.StartAsync` and `Send`
refuse a render with no signed-in visitor (`PageJobRefusal.Anonymous`), because every call is filtered
to the visitor who asked and the writer a job holds acts as that account
(`pages-a-page-may-save-itself`, `pages-a-jobs-credential-is-borrowed`). That is right for a job a
person started and wrong for work the site itself received: an inbound `POST` has no visitor, so the
page that answers it must do the work inside the render, against a five-second deadline that a fan-out
cannot meet and a body it cannot reliably finish reading.

The general gap is not "federation needs a worker". It is **work a page can start on behalf of
nobody**: an inbox, a slow feed, a report, anything the site receives rather than a person asks for.

The owner chose a job owned by the page: no visitor store and no `IPageWrites`, `Page` data only, its
own queue capacity and its own fetch rate, ended by silence or the job's ceiling. That is the same
`IWorker` and the same journal as today, with a second kind of owner.

The hard part is the bound the signed-in rule was standing in for. An anonymous request becomes a work
source, so something else has to stop a public page from being an amplifier: its own queue, its own
rate, the request body's cap, and `Pages:Jobs`' clocks and message limits all still apply, and the
plan decides whether a job's starting page must be the key that received the request. None of those
needs a new configured number if the existing ones carry the second owner.

## Sketch

1. **An owner that is a key.** `PageJob` learns an owner that is either a visitor or the starting page.
   A page-owned job has no Visitor subject, so `IWorkerContext.Stores.Visitor` is absent rather than
   empty, and its writer is not offered — `IPageWrites` is a visitor's authority
   (`pages-any-page-may-write-the-store`).
2. **A second queue.** Page-owned work gets its own capacity, so an inbox cannot fill the queue a
   person's job waits in. The existing `BoundedWorkQueue` is the mechanism; the setting is a second
   value beside `Pages:Jobs:QueueCapacity`.
3. **The rate is the bound.** A page-owned job's fetch allowance comes from
   [`fetch-is-a-rate`](fetch-is-a-rate.md) and is published through
   [`the-site-is-one-type`](the-site-is-one-type.md), so a fan-out is sized rather than hoped for.
4. **The body is the message.** `StartAsync` already takes a first message; the body
   [`a-page-sees-its-request`](a-page-sees-its-request.md) makes visible is what the inbox page passes,
   bounded by `Pages:Jobs:MaxMessageBytes` and refused over the cap rather than cut.
5. **Nothing else changes.** The journal, the silence clock, the ceiling, cancellation and a restart
   losing every job are the worker's existing rules (`pages-a-job-is-not-durable`), and a page-owned
   job is no more durable.
6. **The manual and the record.** `/help` gains the page-owned start and what it may reach; the merge
   records the name the plan intends and amends `pages-jobs-run-a-worker-for-the-page`.

## Not in scope

- **An inbox, an actor or a timeline.** This is the work primitive; the page that receives an activity
  is page code.
- **A job that survives a restart.** A worker writes to the starting page's data, which is the record.
- **Anonymous writes.** A page-owned job reads and writes `Page` data; it does not act as an account.

## Open questions

None of shape. The owner chose the page-owned job on 2026-11-22; the plan decides the queue's name,
its shipped capacity and whether a job may start from a layer other than the page that received the
request.

**Origin:** the owner's ask 5 of 2026-11-22, whose inbox could not hand an inbound `POST` to a worker
because every start needs a signed-in visitor.
