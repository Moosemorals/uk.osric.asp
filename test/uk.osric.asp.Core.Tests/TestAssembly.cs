// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using NUnit.Framework;

// Core is pure functions over immutable values, so every test is independent of every other and can
// run at the same time. A test that ever needs isolation opts out with [NonParallelizable].
[assembly: Parallelizable(ParallelScope.All)]
