<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Logging

A log line answers one of two questions: *what happened to this request, run or job*, and *why did it
not happen*. Everything below serves those, and nothing is logged because it was easy to log.

## The sink

The host logs through `Microsoft.Extensions.Logging.Console` with the **JSON** formatter: one JSON
object per line, scopes included, UTC timestamps. Podman hands the container's stdout to systemd, so
one line is one journal entry and every field survives intact.

The console provider is part of the `Microsoft.AspNetCore.App` shared framework, so it is configured
rather than referenced: .NET 10 refuses an explicit `PackageReference` for a package the framework
already carries (`NU1510`), and `Directory.Build.props` treats that as an error.

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => {
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});
```

The formatter is chosen in code rather than in `appsettings.json` for two reasons. It is the shape of
the output, not a deployment setting, so it should not be something a host can quietly lose. And
`ClearProviders` is what keeps the framework's default text console from writing a second copy of
every line beside it.

**Why JSON and not the `systemd` formatter.** `AddSystemdConsole` gives journald a priority prefix, so
`journalctl -p warning` filters by level — but it flattens a scope to text on a continuation line, and
a flattened scope cannot be queried. Levels are already a field in every JSON line, so `jq` filters by
level just as well, and the ambient data that makes a line diagnosable stays structured.

`builder.Logging` levels still come from configuration: `Information` for the site, `Warning` for
`Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore`, and `Debug` for `uk.osric.asp` in
Development. See `appsettings.json` and `appsettings.Development.json`.

## Levels

The rule is about *who can fix it*, not about how surprising it was.

| Level | Means | Examples |
| --- | --- | --- |
| **Error** | Something is broken **in our code**. A defect, and the sort of thing that should send someone a page. | An unhandled exception; generated source Roslyn refuses to compile; a page image with no handler; a scrub that did not complete; a run that escaped its processor; an import that escaped its processor. |
| **Warning** | A **third party** is broken, or a limit we cannot fix from here was reached. We need to know; there is nothing to repair in this repository. | The model provider is unreachable, rate-limiting, timing out or answering with garbage; Prometheus is down; a storage sample failed; a render was abandoned at its budget; a compile timed out or waited too long for a slot; a stored password hash is unreadable. |
| **Information** | The happy background rumble: one line per thing that changes something. | The host starting; a page saved; a sign-in or a failed sign-in; a sign-out; an assistant run starting and finishing; an import committed; an export written; a page deleted or scrubbed. |
| **Debug** | Noise. Visible only to a developer on their own machine. | A cache hit or miss; a lazy recompile; a retried unique violation; a form or an archive that would not parse; a fetched response with an unknown charset; the generated source of a page that did not compile. |

`Trace` is not used anywhere, and nothing writes it. If a line is worth having in production it is
`Information` or above; if it is not, it is `Debug`.

Two consequences of the rule that are easy to get wrong:

- A **page that overruns its budget is a Warning, not an Error**. The page is the owner's code, the
  host cannot make it faster, and the site has already shed the request.
- A **failed sign-in is Information, not Warning**. Nobody is broken: somebody typed the wrong
  password. It is recorded because "who tried to get in" is a question worth answering later.

## Scopes

A scope carries the ambient facts every line in its span should not have to repeat. The JSON formatter
writes a scope's fields into the line, so a scope key is a field name you can query.

**Keys.** Every value that names something ends in `Id`, in PascalCase: `RequestId`, `RunId`,
`ConversationId`, `StageId`, `ToolCallId`. The signed-in account is `UserName` — the claim is a name,
not an id. A page is `Path`. A tool is `ToolName`, a model is `Model`.

**The request scope** is pushed before the exception handler, so the framework's own unhandled-
exception line carries it too, and it is filled in as the facts become known:

| Key | When | Where from |
| --- | --- | --- |
| `RequestId` | immediately | `HttpContext.TraceIdentifier` |
| `Method`, `Path` | immediately | the request |
| `UserName` | after `UseAuthentication` | the principal's `NameIdentifier` |
| one key per route value ending in `id` | after `UseAuthentication` | the route, so `/assistant/{id}` contributes `Id` |
| `Route` | after `UseAuthentication` | the endpoint's pattern, because `Id` alone does not say which id it is |

Route values are trimmed to the id-ish ones on purpose: a scope with a dozen keys in it is a scope
nobody reads. They are also title-cased, so the route's `{runId}` is the field `RunId` like every
other scope key.

**One category per page, and it is fixed.** A page logs under `uk.osric.asp.Pages` with its key on a
`Path` scope, never under a category per path: `ILoggerFactory` caches a logger for each distinct
category for the life of the process and never evicts one, and a not-found request chooses its own
URL, so a category per path would let an anonymous client grow that cache one unique path at a time
(DEC-24). `appsettings.json` turns on `IncludeScopes` for the console formatter so the path still
shows in the line.

**Work scopes.** Background work is not a request and has no `RequestId`; it pushes its own scope for
the unit of work, so a line from a worker is as attributable as a line from a request:

- `RunWorker`/`RunProcessor`: `RunId`, `ConversationId`, `UserName`, and `Model` once known.
- `ImportWorker`/`ImportProcessor`: `StageId`.
- `SimpleContext` (page code): `Page {Path}` around each message.

**What does not go in a scope.** A scope is not a dumping ground for the request: bodies, headers,
query strings, page source, prompts and reasoning are never in one. Nor is anything that identifies a
*process* rather than an event — `RequestId` is per exchange, not per host.

## Exceptions

- **Log the exception at the point that handles it**, meaning the catch that swallows it, substitutes
  a value or turns it into a response.
- **Do not log and rethrow.** A catch that stores something and rethrows, or a retry loop that
  rethrows when its attempts are spent, is not a handler: the exception carries on to the boundary
  that answers for it, and that boundary logs it once. `Runtime` and `Core` never log for the same
  reason: a compile fault and a page fault are logged by `Web` where they are answered.
- **Always pass the exception object.** `logger.LogWarning(exception, "…")`, not
  `logger.LogWarning("… {Message}", exception.Message)`: the stack, the inner exception and the type
  are the parts that make the line worth printing.
- **The account CLI is the one place that does not log.** `users add` and the rest answer on the
  console, which is the operator's journal: a refused command has already been said out loud, and a
  `Debug` line that the command's own minimum level filters out would be a line nobody can read.
- **Cancellation we asked for is not an event.** A `catch (OperationCanceledException) when
  (cancellationToken.IsCancellationRequested)` runs on every visitor who closes a tab; it logs
  nothing. A cancellation we did not ask for — a provider going silent, a compile timing out — is a
  timeout and is logged as one.
- **Never log what must not be stored.** Request bodies, page source, prompts, model reasoning,
  credentials, cookies, and provider error bodies stay out of the journal. `deploy/README.md` says the
  same thing about the assistant, and it applies everywhere.

## What each layer logs

| Layer | Logs |
| --- | --- |
| `Program` | the host starting, with the environment; nothing per request |
| `PageEndpoint`/`PageRenderer` | render outcomes worth knowing (abandoned, timed out); the rest is metrics |
| `PageImageBuilder`/`PageCompiler` | compile faults (Error); compile timeouts (Warning); the generated source of a failure (Debug) |
| `EditorEndpoint`/`PageEditor` | saves (Information); a scrub that did not complete (Error) |
| `Authentication` | sign-in, failed sign-in, sign-out (Information); a ticket that would not be deleted (Warning) |
| `Admin` | imports and exports (Information); a compiler fault during an import (Error); a rejected archive (Debug) |
| `Assistant` | run start and finish (Information); provider and tool failures (Warning); a run that escaped (Error) |
| `Storage`/`Metrics` | a failed sample or query (Warning); a retried write (Debug) |
| `Health` | nothing per probe: the status is the signal, and a Warning every ten seconds would bury the journal |
| `Core`/`Runtime`/`Sdk` | nothing, ever |

## Working with the journal

```bash
# what happened to one assistant run
ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -o cat \
  | jq -c 'select(.Scopes[]?.RunId == "d4f87b4e03dc4a9986fbce3248c10f3c")'

# everything at warning or above in the last hour
ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service --since -1h -o cat \
  | jq -c 'select(.LogLevel == "Warning" or .LogLevel == "Error")'

# the same, one line per entry with the field you care about
... | jq -r '[.Timestamp, .LogLevel, .Category, .Message] | @tsv'
```

`journalctl -o cat` is deliberate: the journal's own `-o json` wraps the line we wrote inside a
`MESSAGE` string, and `-o cat` hands back the line as it was written.

## Local development

`make run` is Development, where `uk.osric.asp` logs at `Debug` and the framework at `Information`.
The output is the same JSON; `jq .` turns it back into something readable while you watch. A
`Debug` line that would be noise in the journal is exactly what you want on the machine you are
debugging on.
