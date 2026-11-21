// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

// A fixture runs beside every other fixture. A fixture whose tests each own their storage opts into
// running those tests beside each other as well (`ParallelScope.All`); one whose tests share a host,
// a store or a process-wide meter keeps them one at a time. The wall-clock fixtures are in the `Slow`
// category and are not run by `make test-web` at all (`code-slow-tests-are-opt-in`).
[assembly: Parallelizable(ParallelScope.Fixtures)]

// Most of the suite waits - on a database, a compile slot or a fake stream - rather than using a core,
// so the default one worker per processor leaves cores idle. The work that does burn CPU (Roslyn's
// first compile, the deadline fixtures) is the work marked slow or serial, and it still gets the
// process to itself when it runs.
[assembly: LevelOfParallelism(16)]
