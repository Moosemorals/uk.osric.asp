<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-04 — EF Core logs every SQL command at Information in production

| | |
| --- | --- |
| Severity | Low |
| Area | Configuration, observability |
| Location | `src/uk.osric.asp.Web/appsettings.json:5-11` |

## Problem

`appsettings.json` sets `Logging:LogLevel:Default = Information` and only raises
`Microsoft.AspNetCore` to `Warning`. EF Core's `Microsoft.EntityFrameworkCore.Database.Command`
category is therefore at Information, and every page request logs the full SQL text (including
`INSERT`/`UPDATE` statements) to stdout, which the quadlet sends to the journal.

Observed while running the app: every `SELECT`, `INSERT` and the entire migration sequence appear as
`info: Microsoft.EntityFrameworkCore.Database.Command[20101]`. This buries the application's own log
lines, grows journald usage and can leak content into logs (the parameters are redacted, but the
statement shape is not).

## Fix outline

1. Raise the EF categories to `Warning` in the default configuration, e.g.
   `"Microsoft.EntityFrameworkCore": "Warning"`.
2. If SQL visibility is wanted while debugging, put it in `appsettings.Development.json` at
   Information and keep production quiet.
3. Consider an explicit `Logging:LogLevel` for `uk.osric.asp` at Information/Debug to keep the app's
   own messages.
4. Verify with `make run` under `ASPNETCORE_ENVIRONMENT=Production`.
