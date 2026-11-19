<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page leaves a message for another page

| | |
| --- | --- |
| Status | Proposed 2026-11-17 — waits for the owner. Nothing below is built |
| Size | Large — one `Sdk` capability with six types, offered to two kinds of caller; two tables and a migration; a store, a render-bound scope and a worker binding; an account-keyed recipient check; named topics; configuration; the activity slide and the cascades; the page-delete cleanup; the layout flash; the page session's signed-in lifetime moved onto the sign-in; the manual, the privacy page and the docs |
| Area | Sdk, Web (pages, work, storage), docs |
| Spike | None. Every count, path and behaviour below was taken off `trunk` on 2026-11-17 |
| Backlog | [`message-does-not-survive-a-redirect`](../backlog/message-does-not-survive-a-redirect.md) is this step. It was sized `S` when the sketch was one store write; the shape below is larger |
| Decisions | New names proposed: **pages-a-page-has-an-inbox**, **pages-a-topic-is-a-named-inbox**, **pages-an-inbox-belongs-to-its-visitor**, **pages-a-message-is-taken-once**. Edits **pages-the-session**, **pages-a-job-is-not-durable** and **pages-layouts-are-markup**. Relies on `pages-capabilities-are-one-seam`, `pages-page-io-is-async`, `pages-jobs-run-a-worker-for-the-page`, `pages-a-worker-gets-no-response`, `pages-a-page-has-a-state`, `code-policy-values-are-configuration` and `code-validation-at-the-boundary` |

## The short version

Every live stored source that can run code has a **nominal inbox** addressed by its key: a page with
code, or a worker. Any page render or job may post to it. A **named topic** is an inbox with no page
behind it. The receiver takes one message at a time, oldest first, and the take removes it.

- **One capability**, `IPageInbox` from `ctx.Capabilities`, with four calls: `PostAsync(pagePath,
  message)` and `TakeAsync()` for a page or worker's own inbox, and `PostAsync(PageTopic, message)`
  and `TakeAsync(PageTopic)` for a named topic. All answer immediately with a status; none waits for
  a message.
- **The inbox belongs to one account or session, never the site.** A signed-in message is keyed by
  the account, so the laptop and the desktop signed in as that account share one queue. It lives
  for `Pages:Inbox:SignedIn:RetentionDays` (30) from the last successful post or take, and signing
  out on one device does not delete it. An anonymous message is keyed by the page session and ends
  with it, 30 minutes after its last use. A worker acts for its job's owner. This is not a channel
  between visitors.
- **The reader owns what it reads.** The host filters every take by the current account or session,
  and the message does not report an owner: it is always the reader's own.
- **The page session follows the sign-in.** A signed-in visitor's session now lives as long as the
  sign-in (`Authentication:TicketLifetimeDays`, 30 by default) rather than eight hours. The anonymous
  session stays 30 minutes. This is a separate clock from the account inbox, which slides on inbox
  activity rather than on any request.
- **A topic is a named inbox.** `PostAsync(PageTopic.Flash, message)` and
  `TakeAsync(PageTopic.Flash)` reach a queue with no page behind it, per account or session, and any
  page or layout can take from it. `PageTopic.Flash` is the first one: a page posts "saved" and
  whichever layout runs next takes it, whatever page landed.
- **Two small tables in the site database**, so a message survives a restart: one row per message
  with the account or session it belongs to and the page, worker or topic it is for, and one row per
  signed-in account that carries the sliding 30-day window.
- **A take is one atomic statement**, `DELETE … WHERE Id = (SELECT … ORDER BY Id LIMIT 1)
  RETURNING …`, with the account clock's update in the same transaction, so there is no
  check-then-take race and no `HasMessages` call to get wrong.
- **A sender cannot recall a message**, and a sender never learns whether it was read. A receiver
  that takes a message and then fails has lost it; delivery is at most once.
- **Every expected outcome is a value**: `Queued`, `InboxFull`, `VisitorFull`, `MessageTooLarge`,
  `NotAddressable`, `OwnedBySite`, `UnknownPage`, `NotASink`, `Anonymous`, `SessionEnded`, `NoRoom`,
  `Empty`, `ReadOnly`.

The worker's two existing facilities do not change. `IWorkerContext.ReceiveAsync` is still a message
to *this run*, in memory and waitable; `Publish`/`IPageJobs.ReadEventsAsync` is still a replayable
journal of events. The inbox is a third thing: a durable message to a *source*, which any page or
worker can leave and a page or worker takes later. [The inbox is not the journal](#6-inbox-job-channel-journal).

## What exists today

The worker does **not** already have what the sketch assumed. It has two facilities, and neither is a
persistent, consuming, poll-only mailbox:

| | The journal | The job channel |
| --- | --- | --- |
| Reached by | `IWorkerContext.Publish`, `IPageJobs.ReadEventsAsync` | `IWorkerContext.ReceiveAsync`, `IPageJobs.SendAsync` |
| What it holds | Events `(sequence, type, json)` | Messages |
| Consumed? | No. A reader passes the last sequence and replays; a dropped sequence answers `Reset` | Yes. A read removes the message |
| Waits? | A read may wait for the next event | A read may wait for the next message |
| Bounded by | `Pages:Jobs:MaxEvents`, `MaxEventBytes`, `RetentionMinutes` | `Pages:Jobs:MaxPendingMessages`; a read pauses the silence clock |
| Persisted? | No — a job does not survive a restart | No |
| Addressed to | One job | One job |
| Statuses | `PageJobEvents` with `Events`, `Next`, `Reset`, `State`, `Failure` | `null` for *timeout or ended*; `Aborted` tells them apart. `PageJobSendResult` answers a refusal |
| Offered to | A running job | A running job |

The page session is the durable per-visitor store: `PageSessions` and `PageSessionValues`, minted on
the first write, `HttpOnly` cookie, 30 minutes anonymous and 8 hours signed in, sliding, cleared on
sign-out, swept every five minutes. Its reads are open across pages (`TryGetValueAsync(pagePath,
key)`), and its writes are structurally bound to the rendering page. That is why a page can *read*
another page's key but cannot **leave** it a message: the receiver cannot delete what it reads, so a
value written under the sender's path repeats forever unless overwritten.

`pages-any-page-may-write-the-store` does not close this. It is about page **source**: any page may
save, delete and restore any key the shell does not route. It does not let a page write another
page's session keys.

## Goals

1. A page that answers a `POST` with a `303` can leave the confirmation for the page it sends the
   visitor to, with no path convention in the URL and no key the receiver has to know.
2. A worker can leave a message for a page, and take one left for the worker, through the same
   capability a page uses. The job channel and the journal are unchanged.
3. A message survives a restart, is delivered to the account or session that posted it, and to no
   other account, session or user. A signed-in message is visible on every device of the account and
   lives 30 days from the last inbox activity; an anonymous one ends with its session.
4. A full inbox, an empty inbox, a message over its cap, a reserved key, an unknown key, a library, a
   content-only page, a `HEAD` and a discarded anonymous session are all values, not exceptions.
5. The caps and the signed-in window are configuration, the account and session sweeps cascade, and
   a page's tombstone takes its inbox with it so a page re-created at the same key inherits nothing.
6. The manual, the privacy page, the storage doc and the decisions say what it does, in the same
   change.
7. A signed-in visitor's page session lives as long as the sign-in it belongs to: one lifetime, read
   from the authentication section, not an eight-hour number beside a thirty-day one.
8. A page can leave a flash on a named topic, and whichever layout runs next takes it once, whatever
   page landed.
9. A topic is a queue with no page behind it: it needs no stored recipient, no existence check and
   no registry, and a malformed name is refused by the value type before any store is touched.

## Non-goals

- **Broadcast or fan-out.** One message to one key. A page that wants to tell every page something
  writes a page or a library.
- **Peek, count, wait or recall.** `TakeAsync` is the only read, and it removes. A sender cannot
  change its mind, delete, prioritise, delay or address a message to a future time.
- **Replacing the job channel.** `IPageJobs.SendAsync` and `IWorkerContext.ReceiveAsync` still talk
  to one run, in memory. The inbox addresses a source, not a run.
- **A site-wide inbox.** A message is never shared between visitors; the identity is the scope.
- **Fan-out to every device.** A message is a queue: the first device to take it gets it, and a
  second device of the same account gets `Empty`. A chat log every device sees is a different
  feature, not a second reader of this one.
- **Delivery across an identity change.** A message posted while anonymous is not seen after signing
  in, and an account's message is not seen by an anonymous render.
- **A broadcast topic.** A topic is a queue: one taker gets the message. Telling every layout, every
  page or every visitor needs a different feature.
- **A topic registry.** Any page may name any topic; a misspelled custom topic is a separate queue.
  `PageTopic.Flash` is the one shared name, and the manual says to put a custom name in a library
  constant rather than spell it twice.
- **An owner on the message.** The reader is always the owner, because the host filters the take;
  reporting it would invite a page to check what the host already guaranteed.
- **A push to the browser.** A page polls during its render, inside the render budget. A page that
  needs a live stream uses a job and its journal.
- **A record of what a receiver did.** A taken message is gone. A page that must keep it writes it to
  its own storage or session.
- **Messages to the shell.** The shell's routes answer no page code. The few shell flows that need a
  flash already use `PageSessionRequest` directly.

## Design

### 1. The address

A recipient is either a **live stored source that can run code** or a **named topic**:

| Kind | A sink? | Why |
| --- | --- | --- |
| `PageKind.Page` | Yes | It renders and can call `TakeAsync` |
| `PageKind.Worker` | Yes | It runs as a job and can call `TakeAsync` |
| `PageKind.Text` | No | No C# at all, so nothing can take (`NotASink`) |
| `PageKind.Library` | No | Declarations only, never served (`NotASink`) |
| A reserved key | No | The shell answers it and no page code runs (`OwnedBySite`) |
| Nothing live at the key | No | `UnknownPage` |
| A topic | Yes | A named queue with no page behind it; no check is possible or needed |

A page or worker recipient is checked before the write, as a job start checks its worker: normalise
the path, refuse a path that cannot name a page, refuse a reserved key, read the live head and refuse
a missing or wrong kind. A page that is a layout is still a `Page`, so it can be addressed, and a
layout is offered the inbox, which is what makes the flash work.

A topic is not stored and has no head: `PostAsync(PageTopic.Flash, ...)` skips the read, the
reserved-key check and the kind, because the name is the whole address. `PageTopic` refuses a
malformed name when the page constructs it, before any store is touched. A topic nobody takes is
swept with the account or session, like any other message.

A page deleted after the check but before the insert leaves an undeliverable row until the account
or session that owns it ends; it is harmless and noted under [risks](#risks).

### 2. The identity

A message belongs to the account that posted it, or to the anonymous session that posted it, and only
that account or session can take it:

| Poster | Key | Lifetime |
| --- | --- | --- |
| A signed-in page render | `Owner` = the normalised account name | `Pages:Inbox:SignedIn:RetentionDays` (30) from the last successful post or take in that account |
| An anonymous page render | `SessionId` = the page session id | The page session: 30 minutes idle, cleared on sign-in/sign-out by the existing sweep and cascade |
| A job | `Owner` = the job's owner | The same account window |

This is the owner's answer of 2026-11-17: *"messages should certainly be per account, not per login"*
so the laptop and the desktop share a queue, with *"30 days for users, 30 minutes for anonymous, and
both countdowns reset on activity"*. Two tables carry it:

- `PageInboxOwners` is one row per signed-in account that has used the inbox: the normalised name, a
  foreign key to `Users.UserName` with `ON DELETE CASCADE`, and `ExpiresAt`. A successful post or
  take sets `ExpiresAt = now + RetentionDays` in the same transaction as the message; an empty take
  does not. The row is the account's clock, and every message is its child, so the clock ending
  deletes the messages by cascade. A removed account takes its owner row and its messages with it.
- `PageInboxMessages` carries `Owner` or `SessionId`, exactly one. A signed-in message is a child of
  `PageInboxOwners`; an anonymous message is a child of `PageSessions`. The two cascades are the
  lifetime.

The activity is **inbox activity**, not every signed-in request: a page view does not touch the
owner row; a post or a successful take does. That is deliberate. A per-request `UPDATE` would be one
write on every request for a mailbox nobody opened, and the counterpoint is right — SQLite is fast
and *n* is small — but it is a write we do not need. A chat between two devices polls by taking, and
each successful take is the activity.

Two consequences the decisions must state and the privacy page must disclose:

- A signed-in message is shared by every device of the account and survives signing out on one of
  them. The 30-day no-inbox-activity window is what ends it. Logging out on the laptop must not
  delete the message the desktop is about to take.
- An anonymous message ends with the session, so a visitor who leaves the site for half an hour
  loses it. That is the same boundary as everything else anonymous.

A page render that is anonymous cannot post to a worker, because a worker's inbox is the account's
and an anonymous render has no account: `Anonymous`. A signed-in page, a view-token render (which is
the owner's account) and a job can post to any page or worker, for their account. A job posts and
takes for its owner.

The message records the sender, and nothing about its owner:

```csharp
public sealed record PageInboxMessage(
    string From,          // the page layer's key, or the worker's key
    string Body,
    DateTimeOffset SentAt);
```

`From` is stamped by the host from the render, not accepted from the page, so a page cannot claim to
be another. For a worker it is the worker's key (`job.Worker.Key`), not the starting page: the code
that posted is the worker. `SentAt` is UTC. `Owner` is the key on the row, not in the value a page
sees: the take already guarantees the reader is the owner.

### 3. The row and the take

Two small tables, added by one EF Core migration.

`PageInboxOwners` is the signed-in account's clock:

| Column | Type | Notes |
| --- | --- | --- |
| `Owner` | `TEXT PRIMARY KEY` | The normalised account name, `BINARY`, max `UserName.MaximumLength`; foreign key to `Users.UserName`, `ON DELETE CASCADE` |
| `ExpiresAt` | `TEXT NOT NULL` | Fixed-width UTC ISO-8601; indexed, because the sweep reads it |

`PageInboxMessages` is one row per message:

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `INTEGER PRIMARY KEY` | The FIFO order. Auto-increment, so a new row is always after an older one |
| `Owner` | `TEXT NULL` | Foreign key to `PageInboxOwners.Owner`, `ON DELETE CASCADE`; `NULL` for an anonymous message |
| `SessionId` | `TEXT NULL` | Foreign key to `PageSessions.SessionId`, `ON DELETE CASCADE`; `NULL` for an account message |
| `PagePath` | `TEXT NULL` | The recipient page or worker's canonical key; `NULL` for a topic |
| `Topic` | `TEXT NULL` | The topic's name, max 64; `NULL` for a page or worker |
| `FromPath` | `TEXT NOT NULL` | The sender's canonical key |
| `Body` | `TEXT NOT NULL` | The string the page posted |
| `SentAt` | `TEXT NOT NULL` | Fixed-width UTC ISO-8601, as the session store writes its timestamps |

Two `CHECK`s say what a row is: exactly one of `Owner` and `SessionId` is set; and exactly one of
`PagePath` and `Topic` is set. Four indexes serve the two addresses and the caps —
`(Owner, PagePath, Id)`, `(Owner, Topic, Id)`, `(SessionId, PagePath, Id)` and
`(SessionId, Topic, Id)`. The lifetime is the two cascades: a removed `PageInboxOwners` row takes
the account's messages, and a removed `PageSessions` row takes the anonymous ones. They rely on the
same foreign-key enforcement `PageSessionValues` already relies on, so a connection that turns it
off (the archive test does) is the only place they would not run.

A signed-in message is keyed by the account, not by the mailbox's page or topic: the laptop and the
desktop share one queue, and the row has no login key to tell them apart. The take filters by the
owner, which is the current account; there is no owner argument a page can name.

The take is one atomic statement, not a read followed by a delete:

```sql
DELETE FROM "PageInboxMessages"
WHERE "Id" = (
    SELECT "Id" FROM "PageInboxMessages"
    WHERE "Owner" = @owner AND "PagePath" = @pagePath
    ORDER BY "Id"
    LIMIT 1)
RETURNING "FromPath", "Body", "SentAt";
```

The anonymous form is the same with `"SessionId" = @sessionId AND "Owner" IS NULL`. The topic form
replaces `"PagePath" = @pagePath` with `"Topic" = @topic`. A manual `DbCommand` and
`ExecuteReaderAsync` is already how `SqliteConversationStore.StartRunAsync` reads a `RETURNING`
clause, so this introduces no new mechanism. Two takes of one message answer one `Taken` and one
`Empty`.

A **successful signed-in take** then resets the account's clock in the same transaction:

```sql
UPDATE "PageInboxOwners" SET "ExpiresAt" = @expiresAt WHERE "Owner" = @owner;
```

An **empty take does not**: there was no inbox activity. A **successful anonymous take** touches the
page session through `PageSessionRequest.TouchAsync`, the same slide any session read gets; an empty
one does not.

The post is the owner clock and the insert in one transaction:

```sql
INSERT INTO "PageInboxOwners" ("Owner", "ExpiresAt") VALUES (@owner, @expiresAt)
ON CONFLICT ("Owner") DO UPDATE SET "ExpiresAt" = excluded."ExpiresAt";
```

```sql
INSERT INTO "PageInboxMessages" ("Owner", "SessionId", "PagePath", "Topic", "FromPath", "Body", "SentAt")
SELECT @owner, NULL, @toPath, NULL, @fromPath, @body, @sentAt
WHERE (SELECT COUNT(*) FROM "PageInboxMessages"
       WHERE "Owner" = @owner AND "PagePath" = @toPath) < @maxMessagesPerInbox
  AND (SELECT COALESCE(SUM(length(CAST("Body" AS BLOB))), 0)
       FROM "PageInboxMessages" WHERE "Owner" = @owner)
      + length(CAST(@body AS BLOB)) <= @maxVisitorBytes;
```

If the insert writes nothing, the transaction rolls back, so a refused post does not reset the
clock. The topic form binds `@topic` and uses `"Topic" = @topic` in the count; the anonymous form
uses `"SessionId"` and adds `EXISTS (SELECT 1 FROM "PageSessions" WHERE "SessionId" = @sessionId)`,
with `NULL` for `Owner`. The follow-up read that names the refused rule — the session store's
`RefusalAsync` pattern — answers `InboxFull`, `VisitorFull` or `SessionEnded`.

Two statements per successful post or take is the whole cost of the sliding window: SQLite handles
it in one transaction, and *n* is small. Nothing is written on a request that never uses the inbox.

### 4. The capability

```csharp
public interface IPageInbox {

    /// Posts one message to the inbox of the page or worker at <paramref name="pagePath"/>.
    Task<PageInboxPostResult> PostAsync(string pagePath, string message);

    /// Takes the oldest message from this source's own inbox.
    Task<PageInboxTakeResult> TakeAsync();

    /// Posts one message to a named topic.
    Task<PageInboxPostResult> PostAsync(PageTopic topic, string message);

    /// Takes the oldest message from a named topic.
    Task<PageInboxTakeResult> TakeAsync(PageTopic topic);
}
```

```csharp
public sealed record PageTopic {

    /// <summary>The one shared name: a page posts a confirmation and a layout takes it.</summary>
    public static PageTopic Flash { get; } = new("flash");

    /// <summary>Names another queue, such as <c>search</c>.</summary>
    public static PageTopic Named(string name) => new(name);

    public string Name { get; }

    // 1 to 64 lowercase ASCII letters, digits and hyphens, starting with a letter. A malformed name
    // throws ArgumentException here: it is a page bug, not an outcome.
    private PageTopic(string name) => Name = name;
}
```

```csharp
public enum PageInboxRefusal {
    None,            // posted, or taken
    Empty,           // take: the inbox holds nothing
    ReadOnly,        // a HEAD render; posting and taking are writes
    NotAddressable,  // the path cannot name a page
    OwnedBySite,     // a reserved key the shell answers
    UnknownPage,     // no live stored source at the key
    NotASink,        // a library or a content-only page
    Anonymous,       // an anonymous render tried to post to a worker
    SessionEnded,    // the anonymous session was discarded before the write
    MessageTooLarge, // the body is over Pages:Inbox:MaxMessageBytes
    InboxFull,       // the recipient's inbox holds Pages:Inbox:MaxMessagesPerInbox
    VisitorFull,     // the account or session's messages total Pages:Inbox:<kind>:MaxVisitorBytes
    NoRoom,          // the site could not mint a session for an anonymous sender
}

public readonly record struct PageInboxPostResult(PageInboxRefusal Refusal) {
    public bool Queued => Refusal is PageInboxRefusal.None;
}

public readonly record struct PageInboxTakeResult(PageInboxRefusal Refusal, PageInboxMessage? Message) {
    public bool Taken => Refusal is PageInboxRefusal.None;
    public bool Empty => Refusal is PageInboxRefusal.Empty;
}
```

The page-target refusals — `NotAddressable`, `OwnedBySite`, `UnknownPage`, `NotASink` and the
anonymous-to-worker `Anonymous` — never come from a topic call; the identity, cap and read-only
refusals apply to both. There is no login refusal: a signed-in render, a view token and a job all
act for an account, and an account inbox outlives any one sign-in. One enum, as `PageJobRefusal` is one enum, because a page branches on the
value and the log line names the rule. `TakeAsync` has no "do I have messages" sibling: the take *is* the test, so there is
no time-of-check/time-of-use gap to get wrong. A page that wants everything loops until `Empty`, and
the render budget bounds the loop.

Both calls return `Task` because the store is async (`pages-page-io-is-async`), not because they
wait. Neither call waits for a message to arrive: the answer is immediate, which is the "synchronous"
shape the sketch asked for. A blocking call on the request thread is not added.

The refusals that are values are the expected ones. A call on an abandoned render, or a first
anonymous post after the response was committed (when its cookie can no longer be sent) throws as
`IPageSession` throws: those are page bugs, not outcomes.

### 5. Two callers

**A page layer.** `PageChain` offers the capability to every layer, including a layout, bound to the
layer's key and the render's user: the account when there is one, the page session when there is
not. An embedded page posts as itself and takes from its own key. A layout takes `PageTopic.Flash`
on its render, which is the flash: a page posts to that topic and whichever layout runs next takes
it ([the flash](#10-the-flash-and-topics)). A page can also address a layout's own key, but the
topic is what makes the flash independent of which layout the landing page uses. A `HEAD` gets
`ReadOnly` for every call, because taking is a delete and posting is an insert. A view-token render
is the owner's account, so it uses the account inbox like any signed-in render.

**A job.** `PageJobRunner` adds the capability to the job's `PageCapabilities`, bound to
`job.Worker.Key` and `job.Owner.Name`, with the job's deadline. The worker reaches it with
`ctx.Capabilities.GetRequired<IPageInbox>()` like every other capability. The job acts for its owner,
so it posts and takes the owner's account inbox. A successful post or take re-arms the job's silence
clock, as `ReceiveAsync` and `Publish` do. An empty take does not: a worker that only polls is not
working, and a worker that wants to wait owns `ReceiveAsync(waitSeconds)` as its sleep primitive.

### 6. Inbox, job channel, journal

Three things now look like messaging; the manual and the decisions must keep them apart:

| Want to… | Use |
| --- | --- |
| Tell the page that lands after this request something | `PostAsync(pagePath, …)`, then a redirect |
| Tell whichever layout runs next something | `PostAsync(PageTopic.Flash, …)` |
| Leave work for a worker, or a message for a page, from anywhere, which survives a restart | `PostAsync(pagePath, …)` or `PostAsync(topic, …)` |
| Ask one running job something and wait for its answer | `IPageJobs.SendAsync` / `IWorkerContext.ReceiveAsync` |
| Watch a running job's progress, replaying or live | `Publish` / `IPageJobs.ReadEventsAsync` |

The inbox is **durable**; the journal and the job channel are not. That is not a contradiction of
`pages-a-job-is-not-durable`: that decision rejects re-running a job after a crash, and the inbox
does not re-run anything. A message taken by a run that then crashes is gone; the inbox never
re-delivers it. The decision is edited to say so.

### 7. Caps and the lifetime

`Pages:Inbox` holds every bound:

| Key | Shipped | Range | What it bounds |
| --- | --- | --- | --- |
| `MaxMessageBytes` | 16 384 | 1 024 – 1 048 576 | One body |
| `MaxMessagesPerInbox` | 8 | 1 – 256 | One page inbox or one topic, for one account or session |
| `Anonymous:MaxVisitorBytes` | 16 384 | 1 024 – 16 777 216 | Every message one anonymous session holds |
| `SignedIn:MaxVisitorBytes` | 65 536 | 1 024 – 16 777 216 | Every message one account holds |
| `SignedIn:RetentionDays` | 30 | 1 – 365 | An account's inbox, from its last successful post or take |
| `CommandTimeoutSeconds` | 5 | 1 – 120 | One store command |

Every value is required and every refusal names the key, the range and the shipped value, as
`Pages:Session` does. `PageInboxOptions.FromConfiguration` follows the same shape, with a test for
the shipped value, an override and a refusal.

The signed-in lifetime is the `PageInboxOwners` row's `ExpiresAt`; the sweep deletes rows past it and
the cascade takes the messages. The existing five-minute `PageSessionCleanupService` gains one call,
`IPageInboxStore.RemoveExpiredAsync`, and logs one line with the count when it removed anything. No
new timer is added. The anonymous lifetime is the page session's own
`Pages:Session:Anonymous:IdleMinutes` (30) and its five-minute sweep; there is no separate anonymous
retention key, because the session already has one.

The caps are per account or per session, not per page or topic, so one page's messages can use the
room another would have had — the same rule as the session, and the manual says so. A new account
starts with an empty inbox and its own byte total.

### 8. Lifecycle

- **Delete.** `DeletePageAsync` already deletes `PageState` in the tombstone's transaction because a
  page saved at the same path must not inherit it. It deletes `PageInboxMessages` with the same
  conditions, for the same reason: a re-created key inherits no mail. `ScrubPageAsync` does the same
  when it finally removes a tombstone. Only `PagePath` rows are deleted; a topic is not a page.
- **Move.** The proposed [page move](page-move.md) renames a key. The inbox is addressed by path, so
  a move drops the old path's messages; the move's own plan must add the delete (or decide to carry
  them) when it lands. Nothing in this plan depends on the move.
- **Sign-out.** `PageSessionCookie.ClearAsync` removes the page session, so anonymous messages go
  with it. The account's inbox is untouched: signing out on the laptop must not delete the message
  the desktop is about to take. The account's 30-day window ends it instead.
- **Sign-in.** The page session rotates, so anonymous messages go; the account inbox is already the
  account's and does not change.
- **Activity.** A successful post or take sets `ExpiresAt` forward. An empty take, a page view or a
  refused post does not.
- **Expiry and a removed account.** The five-minute sweep deletes an expired `PageInboxOwners` row
  and its messages. A removed `Users` row cascades the owner row and its messages, so a re-created
  account with the same name inherits nothing.

### 9. The page session follows the sign-in

The owner's 2026-11-17 answer is that "session" means the 30-day login timer for a signed-in visitor.
The inbox is not the only place that says "session": `IPageSession` still says eight hours, and
`Pages:Session:SignedIn:IdleMinutes` carries the number. Leaving it there would give the site two
meanings of the word, and a page that writes a half-written form to its session would lose it after
eight hours while the login that owns it ran for thirty days.

The signed-in idle lifetime becomes the sign-in's lifetime:

- `PageSessionOptions.SignedInIdle` is set from `AuthenticationOptions.TicketLifetime`. `Program`
  reads `Authentication` once and passes the value in; the convenience
  `FromConfiguration(IConfiguration)` reads the authentication section itself for the tests.
- `Pages:Session:SignedIn:IdleMinutes` is removed from `appsettings.json` and from the options
  record. A deployment that keeps the key is refused at startup with the "no longer read; remove the
  key" message that `Pages:ReservedPaths` already uses, because a key that no longer decides
  anything is worse than a missing one.
- `Pages:Session:Anonymous:IdleMinutes` stays 30 and is still the anonymous session's only lifetime.
- The sweep, the eviction order and `MaxLiveSessions` do not change. A signed-in row now lives up to
  30 days instead of eight hours; there is one owner (`product-one-owner`), and anonymous rows are
  still evicted first.
- Reading slides the timer, as before; a `HEAD` and a layout do not. The login cookie slides on any
  request, while the session row slides only when the session is used, so a row nothing uses for 30
  days is swept even if the visitor is still signed in. That is the ceiling, not a promise that every
  signed-in request keeps a row alive.
- A page session is honoured only while the request is signed in as its account: the resolver
  already deletes a row whose account does not match, so ending the sign-in (a password change, a
  removed account) ends access to the row at the next request even though the row itself waits for
  the sweep.

The page session's signed-in idle is the sign-in's lifetime. The inbox is not keyed by it: a
signed-in message belongs to the account and has its own 30-day window, slid by inbox activity, so
two devices of one account share it and signing out on one leaves the other's mail alone. Both are
30 days in the shipped configuration, but they are two clocks, and the plan keeps them apart on
purpose.

### 10. The flash and topics

The flash is why a topic exists. A page handles a `POST`, redirects, and the next page should say
"saved" whatever page it is. The page does not know which layout the landing page will use, so it
posts the confirmation to a topic and the layout that runs next takes it:

```csharp
// the page that handled the POST
await inbox.PostAsync(PageTopic.Flash, json);
ctx.Redirect(nextPath);

// /_layout, on every HTML render
PageInboxTakeResult flash = await ctx.Capabilities
    .GetRequired<IPageInbox>().TakeAsync(PageTopic.Flash);
if (flash.Taken) { /* write it */ }
```

- `flash` is one queue per account or session, as every inbox is: another account's flash is not in
  it. A signed-in flash is account-wide, so whichever device of the account renders a layout taking
  `flash` first consumes it; that is the queue rule, not a bug.
- A topic is a **queue, not a broadcast**. The first taker gets the message; a second layout or page
  that takes `flash` in the same chain gets `Empty`. The site has one base layout, so it is the
  taker.
- A custom topic is `PageTopic.Named("search")`. A misspelled name is a separate queue, so the manual
  says to put a custom name in a library constant. `PageTopic.Flash` is the one name the SDK owns.
- A topic has no stored recipient: a post skips the page-head read and can never be `UnknownPage` or
  `NotASink`. A malformed name throws when the page constructs the `PageTopic`, before a store call.
- A `HEAD` is `ReadOnly`, so a HEAD of a page does not consume the flash. A page that does not use a
  layout leaves it; a page that uses any layout taking `flash` consumes it, on whichever device of
  the account gets there first.
- A page can post to a topic from a `GET` as well as a `POST`; the same render's layout runs after
  the content, so the flash appears at once.
- The take is a `DELETE` even when the topic is empty. A layout that takes `flash` on every HTML
  render pays one small write per render, the same order as the session's own touch; it is the price
  of no `HasMessages` call and no check-then-take race.

## Configuration

`appsettings.json` gains:

```json
"Inbox": {
  "MaxMessageBytes": 16384,
  "MaxMessagesPerInbox": 8,
  "Anonymous": { "MaxVisitorBytes": 16384 },
  "SignedIn": { "MaxVisitorBytes": 65536, "RetentionDays": 30 },
  "CommandTimeoutSeconds": 5
}
```

`Pages:Session:SignedIn:IdleMinutes` goes from the same file, because the signed-in page session's
idle is now the `Authentication:TicketLifetimeDays` value. The inbox has its own
`SignedIn:RetentionDays` (30); the anonymous inbox has no separate key because the page session's
30-minute idle is its lifetime.

No topic-name key is added: the 1–64 lowercase letter, digit and hyphen rule is an invariant in
`PageTopic`, because a topic is a label rather than a deployment policy. The caps above apply to a
topic exactly as they apply to a page inbox.

`Program.Main` reads it with every other section and reports all problems in one list, as it does for
`Pages:Session` and `Pages:Jobs`. `PageEnvironment` carries the options and the store; a host that
offers neither offers no capability, which is the shape a page is already prepared for.

## Steps

Each step is test-first. Nothing is built until the owner approves the plan, and the merge waits for
the owner at every step (`product-steps-wait-for-the-owner`).

1. **The SDK.** `IPageInbox`, `PageTopic`, `PageInboxMessage`, `PageInboxPostResult`,
   `PageInboxTakeResult`, `PageInboxRefusal`; the two overloads for a topic; the unshipped public API
   entries; the doc comments that say the identity, the one-at-a-time take, the queue-not-broadcast
   rule and the worker difference. A compile test is the API policy's: the new types are reachable
   from a page.
2. **The tables.** `PageInboxOwners` and `PageInboxMessages`, the `StorageDbContext` model with the
   two checks, the five indexes and the three cascades (`Users`, `PageInboxOwners`, `PageSessions`),
   the migration, and the migration test (up, down, up).
3. **The store.** `IPageInboxStore` and `SqlitePageInboxStore`: the owner upsert and the insert in
   one transaction, the two atomic takes and the clock update, the page and topic addresses, the
   `RemoveExpiredAsync` sweep and the refusal read. Tests for the caps, the order, the concurrent
   take, the account filter, the topic address, the activity slide and the cascades.
4. **The configuration.** `PageInboxOptions`, `PageInboxCaps`, `appsettings.json`, `Program.Main`,
   and the three configuration tests.
5. **The page caller.** `PageInboxScope`, the recipient check against `IPageReadStore` and
   `ReservedPaths` for a page address, the topic path with no check, the account from the render's
   user or the page session when there is none, the session mint and slide for an anonymous post,
   the `HEAD` rule, the layout offer and the wiring in `PageChain`. The scope tests.
6. **The worker caller.** The capability in `PageJobRunner.Context`, bound to the job's owner, the
   page and topic addresses, the re-arm on a successful post or take, and the worker tests: a page
   leaves a message for a worker; a worker leaves one for a page; a worker posts and takes a topic;
   an empty take does not re-arm; `ReceiveAsync` is unchanged.
7. **The seeded layout.** `/_layout` takes `PageTopic.Flash` and renders it in the banner, so the
   flash works out of the box; the seeded `/help` shows the same lines.
8. **The lifecycle.** The delete/scrub cleanup in `SqlitePageStore`, the `RemoveExpiredAsync` call
   in `PageSessionCleanupService`, and the tests for the cascades: an expired owner row deletes its
   messages, a removed `Users` row deletes both, and a session row removed deletes its own.
9. **The session's lifetime.** `PageSessionOptions.SignedInIdle` from `AuthenticationOptions`, the
   removal of `Pages:Session:SignedIn:IdleMinutes` and its startup refusal, the updated session
   tests, and the `IPageSession` remarks.
10. **The words.** `/help`, `/privacy`, `docs/pages.md`, `docs/storage.md`, `docs/glossary.md`, and
   the decisions at merge. `make check` is the gate.

## Tests

The behaviours this change introduces, each as one test:

- A page posts to another page; the other page takes it without knowing the sender's path.
- The oldest message is taken first; a second take answers `Empty`.
- Two concurrent takes of one message answer one `Taken` and one `Empty`.
- A `HEAD` post and a `HEAD` take answer `ReadOnly`; a layout is offered the capability and takes a
  flash topic.
- A reserved key is `OwnedBySite`; an unknown key is `UnknownPage`; a library and a content-only
  page are `NotASink`.
- An anonymous render posting to a worker is `Anonymous`.
- A message over `MaxMessageBytes` is `MessageTooLarge`; a full inbox is `InboxFull`; an account over
  its total is `VisitorFull`; a discarded anonymous session is `SessionEnded`.
- A signed-in message is keyed by the account: a page posts on one login and a second login of the
  same account takes it; another account does not see it; signing out on the first does not delete
  it; an anonymous session does not see it.
- A successful post or take moves the account's `ExpiresAt` forward; an empty take and a page view
  do not. An expired owner row deletes its messages, and a removed `Users` row cascades both.
- A page A posts during a `POST`, redirects to B, and B takes and renders it on the next request.
- A page posts to `PageTopic.Flash`, redirects to B, and B's layout takes and renders the flash; a
  page that uses no such layout leaves it. A second take of `flash` answers `Empty`.
- A topic is a separate queue from a page inbox: a message posted to `PageTopic.Flash` is not taken
  by the page's `TakeAsync()`, and a custom `PageTopic.Named("search")` is not taken by `flash`.
- A topic is per account or session: another account does not see `flash`, a second login of the
  same account does, and an anonymous render posts to and takes from the session's own `flash`.
- A worker posts to and takes from a topic for its owner; a malformed `PageTopic` throws when the
  page constructs it, before any store call.
- A worker takes a page's message; a worker posts one to a page; a successful take re-arms the
  silence clock and an empty one does not; a worker acts for its owner rather than a login.
- Deleting a page removes its inbox; a page saved again at the key takes `Empty`.
- A restart over the same database still delivers a signed-in message to the same account and an
  anonymous one to the same session; a different account or session does not see either.
- The migration applies up, down and up; the store refuses at each cap; an expired owner row, a
  removed `Users` row and a removed `PageSessions` row each delete the messages they own.
- The signed-in page session's idle is the authentication ticket's lifetime; changing that one value
  moves the sweep threshold; the removed `SignedIn.IdleMinutes` key is refused at startup; the
  anonymous idle is still its own 30 minutes.

The integration test for the restart opens a second host over the same database file, which is what
the existing integration host already supports. Anything a unit test can prove does not go to the
browser suite.

## Documentation

- **`/help`** gains "Leaving a message for another page" beside "Per-visitor state" and "Starting a
  job": the four calls, `PageTopic`, the statuses, the account rule (a message belongs to the account,
  every device shares it, it lives 30 days from the last inbox activity, and signing out does not
  delete it; an anonymous one ends with the session), the caps, the take-once rule, the
  queue-not-broadcast rule, the worker difference, the `flash` topic, and a complete `POST` → `303` →
  layout take example. It says to encode the body if it is echoed: the host treats it as text, not
  HTML.
- **`/privacy`** gains what a message is, who can read it, that a signed-in one is shared by the
  account's devices and kept for up to 30 days after the last inbox activity (signing out does not
  delete it), and that an anonymous one ends with the session. Its existing session line changes
  from "30 minutes ... or 8 hours if you are signed in" to 30 minutes anonymously and the sign-in's
  lifetime when signed in, because of [§9](#9-the-page-session-follows-the-sign-in).
- **The seeded `_layout`** takes `PageTopic.Flash` after the banner and renders it, so the flash
  works without a page author writing the layout side.
- **`docs/pages.md`** lists `IPageInbox` and `PageTopic` with the other capabilities, says how they
  differ from `IPageJobs`, and says a layout takes `flash`.
- **`docs/storage.md`** gains the two tables, the owner row, the five indexes, the three cascades,
  the sweep and the delete rule.
- **`docs/glossary.md`** defines **inbox** and **topic**, and distinguishes them from a **job
  message** and the **journal**.
- **`IPageSession`**'s remarks and **`docs/pages.md`** say the signed-in session lives as long as the
  sign-in; **`docs/storage.md`** says the sweep's signed-in threshold is the login's.
- **`docs/decisions.md`** at merge: the four new entries, the edit to `pages-a-job-is-not-durable`,
  the edit to `pages-the-session` and the edit to `pages-layouts-are-markup`.
- **`message-does-not-survive-a-redirect`** gains the *Planned* note and the plan link; its row changes from `S` to `L`.

## Risks

- **A message is per account, so every device shares one queue.** The first device to take it gets
  it, and a second gets `Empty`; a flash posted for the laptop may be consumed by the desktop if it
  renders first. A fan-out or a chat log is [not in this step](#considered-and-rejected).
- **The account window is inbox activity, not page views.** A user who browses for 31 days without
  opening the inbox loses an unread message; a per-request touch would keep it at one write per
  request. The plan prices the one `UPDATE` per successful post or take instead.
- **A signed-in page session now holds a half-written form and any page scratch values for up to 30
  days.** The key and byte caps still bound it, sign-out deletes it, and a row is honoured only while
  signed in as its account; the trade is a longer-lived row for a visitor who rarely returns. The
  alternative is two lifetimes that drift, which is the state the owner asked to end.
- **A worker's inbox is shared by its concurrent runs.** Two jobs of one worker for one account take
  from the same queue and compete. That is a work queue, not a conversation; a page that wants one
  run uses `IPageJobs.SendAsync`.
- **At most once.** A page that takes and then throws loses the message; a worker that takes and then
  crashes loses it. There is no transaction around a render, and adding one would make the inbox a
  record rather than delivery.
- **A page deleted between the check and the insert** leaves an undeliverable row until the account
  or session that owns it ends, or the delete cleanup. It is not a security hole — no one else can take
  it — and the page-delete transaction covers the ordinary path.
- **A layout takes on render.** A layout now performs one bounded write when it takes its own inbox
  or `flash`, which `pages-layouts-are-markup` must allow. A `HEAD` is refused, a layout can only
  take from its own inbox or a topic, and it still cannot write the page store, start a job or write
  a session value. A layout that posts is allowed for the same reason, but a post happens once per
  page it wraps; the manual says take, not post.
- **A topic is a queue, not a broadcast.** The first taker gets the message; a second layout or page
  in the same chain gets `Empty`. Telling every layout, every page or every visitor is a different
  feature.
- **A custom topic typo is a separate queue.** `PageTopic.Flash` is the one shared name; a custom
  name spelled two ways is two queues. The manual says to put a custom name in a library constant.
- **The flash is best-effort.** It is delivered only by a page that uses a layout taking `flash`; a
  `HEAD`, a redirect or a non-HTML response leaves it waiting for the next page that does. A layout
  that takes `flash` on every HTML render pays one empty `DELETE` per render, priced like the
  session's touch. The manual says so, and a page that must say something to a specific page
  addresses that page.
- **The body may hold visitor input.** The manual's example encodes it, but the host does not parse
  or sanitise it; a page that renders it raw is the page's bug, exactly as with `IPageStorage`.
- **The sliding window is two statements per successful post or take.** The owner upsert and the
  message write are one transaction; a page view or an empty take adds nothing. That is the *n* is
  small trade the owner named, and it is the only place the plan spends a write on the clock.

## Considered and rejected

- **Reserved keys in `PageSessionValues`.** It would reuse the table and the cookie, but the
  receiver's `KeysAsync` and `AllKeysAsync` would show keys it never wrote, the sender would write
  outside its own path, and the session's key count and byte caps would mix two kinds of data. The
  third point is the one that cannot be fixed by a prefix.
- **A site-wide inbox per page.** One inbox shared by every visitor: the first visitor to land takes
  another visitor's message. Page code cannot guard against that, because the correlation is gone.
- **A login-keyed mailbox with a cascade.** It is what the previous draft proposed: the auth ticket
  is the key and sign-out deletes the messages. It fails the owner's case — the laptop and the
  desktop are different logins of one account, so they would not share a queue, and signing out on
  the laptop would delete the mail the desktop was about to take.
- **A fixed 30-day retention from `SentAt`.** It is simple, but it does not reset on activity: a
  message read every day for 31 days would still be deleted on day 31. The owner asked for a sliding
  window, so the account's row carries `ExpiresAt` and a successful post or take moves it.
- **Touching the account row on every signed-in request.** It would make a page view count as inbox
  activity, at one write per request for a mailbox nobody opened. A post or a successful take is the
  activity; that is the whole of the churn.
- **The page session as the message key.** It is close for an anonymous visitor, and a signed-in
  session now follows the sign-in, but a worker has no session at all. The account is the one
  identity a page render, a view token and a job all have.
- **Posting the flash to the layout's key.** It works for the one base layout, but a page has to know
  which layout the landing page uses, and a second layout needs a second post. The topic lets any
  layout take it and the page names no layout ([why](#10-the-flash-and-topics)).
- **A topic as a page-path prefix.** A prefix such as `topic:flash` hides a second address space
  inside a path, and a page path could collide with it. A `Topic` column and a `PageTopic` type are
  honest about the two addresses.
- **A broadcast topic.** A topic is a queue: the first taker gets the message. Telling every layout,
  page or visitor is a different feature, not a flag on this one.
- **A layout with no inbox.** Keeping `pages-layouts-are-markup` absolute would make the flash
  impossible without a shell route. The layout taking from its own inbox or a topic is one bounded
  write, explicitly written in the layout, and a `HEAD` does not do it.
- **An owner in the message.** The host filters the take by the current account, so the message's
  owner is always the reader. Reporting it would invite a page to check what the host already
  guaranteed.
- **A nullable message instead of a status.** `null` cannot tell full from too large from unknown,
  and a page ends up parsing a message to find out why. A refusal value is the codebase's answer to
  "expected failure", as it is for a job start (`pages-jobs-run-a-worker-for-the-page`).
- **A wait on a page's take.** A page render is bounded and answers a visitor; a wait would hold the
  request. The worker already has `ReceiveAsync` for waiting, and a page polls.
- **Recall, delete, priority or delay.** The owner's rule is fire-and-forget: a sender cannot change
  its mind. Each of these would add a second addressing mode and a second status family.
- **Reusing the job journal.** It is replayable, sequence-cursored and job-scoped; a page has no job
  and a message must be consumed once. The name "journal" in the sketch was the wrong facility; the
  API's feel is kept, the storage is not.

## What this does not close

- The shell's own flash messages stay on `PageSessionRequest`; the shell is not offered the
  capability.
- A page that wants a *stream* of messages still needs a job and its journal.
- A message to a page that does not exist is refused, so a page cannot pre-seed a future key.
- An identity change between the post and the take loses the message; no migration across accounts
  or sessions is attempted.
- A message is per account: every device of the account shares one queue, the first taker gets the
  message and a second gets `Empty`. There is no fan-out and no chat log.
- Signing out does not delete a signed-in message; the 30-day no-inbox-activity window does. There
  is no "delete all my mail" action.
- A flash is delivered only by a page that uses a layout taking `flash`; a page that uses no such
  layout leaves it.
- A topic is a queue, not a broadcast; a second taker gets `Empty`, and there is no topic registry.
- A custom topic name is the page's convention; `PageTopic.Flash` is the only shared name, and a
  misspelling is a separate queue until the account's window or the session ends.
- The anonymous page session stays 30 minutes; only the signed-in one follows the sign-in.
- A signed-in session row can still be swept before the auth cookie ends if no page uses the session
  for the whole window. The login keeps working, and a page that never wrote has nothing to lose.
