// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using NUnit.Framework;

// The shell tests each build their own service, storage and context, so a fixture is independent of
// every other fixture and the fixtures run at the same time. Tests inside a fixture do not: the
// page-budget tests measure a wall-clock deadline against a page that burns CPU, and the storage
// fixtures migrate a database in setup, so each fixture owns its tests. PageMetricsTests opts out
// entirely because a MeterListener hears every meter in the process.
[assembly: Parallelizable(ParallelScope.Fixtures)]
