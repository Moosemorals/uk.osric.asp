// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using NUnit.Framework;

// A fixture here owns a host and a throwaway database, and its tests share both — one fixture's tests
// add pages, sign in and change passwords in a single database — so only the fixtures run in parallel.
[assembly: Parallelizable(ParallelScope.Fixtures)]
