<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-02 — Health endpoint and its monitoring

| | |
| --- | --- |
| Severity | Low |
| Area | Deployment, operations |
| Location | `src/uk.osric.asp.Web/Program.cs`, `src/uk.osric.asp.Web/Health/`, `deploy/uk.osric.asp.container` |

## Problem

Nothing exposed liveness or readiness. The quadlet had `Restart=always` and a 300 s start timeout,
but podman/systemd could only see that the process exists, not that it is serving. The dependency on
SQLite was invisible to the operator, and a bad migration that left the app unable to read storage
still looked "up". A crash loop also restarted forever, which looks like "up" while never serving.

## Plan

### Endpoint (done)

Use ASP.NET Core's native health checks (`AddHealthChecks`/`MapHealthChecks`) rather than a
hand-rolled route, so the checks and the report plumbing stay in the framework.

- **Path:** `/heath` (deliberately not `/healthz`), mapped in `Program.cs` before the page catch-all.
- **Database check:** `StorageHealthCheck` resolves a fresh scope and calls
  `database.Database.CanConnectAsync()`; a database that cannot be reached is `Unhealthy`. This is
  the only check today; more checks (disk, page compiler) can be added later without touching the
  endpoint.
- **Report:** `HealthReportWriter` emits detailed JSON — overall `status`, `totalDurationMs`, and one
  entry per check with `name`, `status`, `description`, `durationMs`, `error` and `data`. The response
  is 200 when healthy and 503 otherwise, so an orchestrator or curl can read the difference.
- **Protection:** `/heath` is in `Pages:ReservedPaths` (both `ReservedPaths.Default` and
  `appsettings.json`), and the route is mapped ahead of the page catch-all, so a page stored under
  `/heath` can never shadow it.
- **Restart cap:** both quadlets now set `StartLimitBurst=3` and `StartLimitIntervalSec=60s`, so a
  crash loop stops as a failed unit instead of restarting forever.

Covered by `HealthEndpointTests` (integration: 200 + JSON shape, and a stored page cannot shadow the
route) and `StorageHealthCheckTests` (unit: healthy/unhealthy from `CanConnectAsync`).

### Remaining

- **Container-level `HealthCmd=`/`HealthInterval=`:** deferred. The app image
  (`mcr.microsoft.com/dotnet/aspnet:10.0-alpine`) ships no `curl`/`wget`, so a podman healthcheck has
  nothing to run inside the container; and a host-side probe is really the alerting story, which is
  OPS-05.
- **Alerting on the signal:** a timer that curls `/heath` and notifies on 503 — see OPS-05.
