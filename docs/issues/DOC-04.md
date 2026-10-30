<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# DOC-04 — `docs/testing.md` omits the web test project

| | |
| --- | --- |
| Severity | Low |
| Area | Documentation, testing |
| Location | `docs/testing.md:11-26` |

## Problem

The "two suites" table lists only `uk.osric.asp.UnitTests` and `uk.osric.asp.Specs`. There are now
three test projects: `uk.osric.asp.WebTests` exists, has 43 tests, and is included in `make test`
(`Makefile` `test: test-unit test-spec test-web`). The documented commands list only
`make test-unit` and `make test-spec`, omitting `make test-web`, and `make test` is described as
"unit + specification suites". Someone following the docs would not know the web tests exist or how
to run them alone.

`Directory.Build.targets` also recognises `.WebTests` as a test project, which the docs do not
mention.

## Fix outline

1. Add a row for `test/uk.osric.asp.WebTests` (NUnit, endpoint and storage behaviour, milliseconds/
   seconds) and change the heading to "the test projects".
2. Document `make test-web`.
3. State what belongs where: core rules in unit tests, site behaviour in specs, endpoint/storage
   behaviour in web tests, and the shell pipeline in integration tests once ARCH-08 lands.
4. Mention that anything a unit test can prove does not belong in a browser test (already stated)
   and that the browser suite was removed.
