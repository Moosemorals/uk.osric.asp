<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-02 — There is no health endpoint for the container

| | |
| --- | --- |
| Severity | Low |
| Area | Deployment, operations |
| Location | `src/uk.osric.asp.Web/Program.cs`, `deploy/uk.osric.asp.container` |

## Problem

Nothing exposes liveness or readiness. The quadlet has `Restart=always` and a 300 s start timeout,
but podman/systemd cannot tell whether the app is serving; it can only see that the process exists.
The dependency on SQLite is invisible to the operator, and a bad migration that leaves the app
unable to read storage still looks "up".

## Fix outline

1. Add `app.MapGet("/healthz", ...)` returning 200 with a tiny body, and optionally a `/readyz` that
   pings the database (`database.Database.CanConnectAsync()`).
2. Exclude these routes from the page catch-all, and decide whether they are public (they reveal
   little) or loopback-only.
3. Add `HealthCmd=`/`HealthInterval=` to the quadlet (podman supports healthchecks on containers), or
   rely on the systemd `Restart` and a simple external monitor.
4. Add an integration test for the endpoint.
