// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using NUnit.Framework;

// Each test compiles its own page into its own assembly, so Roslyn and the load context are safe to
// drive from several tests at once. A test that ever needs isolation opts out with [NonParallelizable].
[assembly: Parallelizable(ParallelScope.All)]
