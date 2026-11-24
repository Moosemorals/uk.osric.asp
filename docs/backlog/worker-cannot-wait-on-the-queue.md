<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A worker cannot wait on the durable queue

| | |
| --- | --- |
| Difficulty | — |
| Utility | — |
| Status | unshaped |
| Blocked by | — |
| Area | — |
| Location | — |
| Spike | — |

## Idea

A worker cannot block on the durable queue: [`IMessageStore`](../../src/uk.osric.asp.Sdk/IMessageStore.cs)
is peek-and-take only, because a render answers a visitor and must not wait, and
`IWorkerContext.ReceiveAsync` waits on the job's in-memory channel rather than on `StoreMessages`, so
a worker waiting for a topic polls `TakeAsync` and uses a timed `ReceiveAsync` to keep its silence
clock armed. Give the job a wait on the store — `WaitAsync(topic, waitSeconds)`, bounded by
`Pages:Jobs:MaxWaitSeconds` and pausing the silence clock the way `ReceiveAsync` does — implemented as
a bounded poll in the store or by waking it through the job's channel; a page render still never
waits. Revisit the next time background workers come up, and edit `pages-a-message-is-taken-once`,
which records that there is no wait.
