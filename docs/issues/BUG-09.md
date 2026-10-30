<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-09 — Page log entries are categorised as `MainEndpoint`, not the page

| | |
| --- | --- |
| Severity | Low |
| Area | Web, observability |
| Location | `src/uk.osric.asp.Web/SimpleContext.cs:10-17`, `src/uk.osric.asp.Web/MainEndpoint.cs:12` |

## Problem

`MainEndpoint.Handle` takes `ILogger<MainEndpoint>` and passes it to `SimpleContext`, which logs:

```csharp
logger.LogInformation("Path: {Path} - {Message}", path, message);
```

Every page's log lines are therefore filed under `Microsoft.Extensions.Logging`'s category for
`MainEndpoint`, and the message template embeds the path as a prefix. Filtering by page, or setting a
level per page, is impossible, and the category is misleading when reading logs.

`IContext.Log` also offers no level; everything is Information.

## Fix outline

1. Inject `ILogger<SimpleContext>` (or a logger created from `ILoggerFactory` with
   `CreateLogger($"Page.{path}")`) so entries are categorised by the page.
2. Stop prefixing the path into the template; use the category or a structured scope
   (`logger.BeginScope(new { Path = path })`).
3. Consider adding a level to `IContext.Log` (`Log(LogLevel, string)` or `LogInfo`/`LogError`) —
   this is an API decision that belongs with ARCH-02.
4. Keep `SimpleContext` the only place that touches `ILogger`, so `Core` stays free of it.
