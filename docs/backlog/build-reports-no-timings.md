<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The build reports no timings

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

The build reports no timings: `dotnet format`, `make check`, each test suite, `make image` and the ssh
deploy steps are measured only by whoever watches the terminal. Record each duration during the build
and push it as a JSON blob to an endpoint that updates the site's Prometheus metrics, so the trends
are visible on `/status`.
