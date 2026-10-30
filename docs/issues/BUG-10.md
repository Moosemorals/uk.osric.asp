<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-10 — `make format-check` fails on the current tree

| | |
| --- | --- |
| Severity | Low |
| Area | Build, hygiene |
| Location | `test/uk.osric.asp.UnitTests/TemplateSourceGeneratorTests.cs` |

## Problem

`dotnet format uk.osric.asp.slnx --verify-no-changes` reports:

```
test/uk.osric.asp.UnitTests/TemplateSourceGeneratorTests.cs(65,2): error FINALNEWLINE: Fix final newline. Insert '\n'.
```

The file does not end with a newline, which `.editorconfig` (`insert_final_newline = true`) and the
project style require. `make format-check` therefore fails on a clean checkout, so the check cannot be
used as a gate (or will be ignored). It also means the last commit was not verified with the
repository's own target.

## Fix outline

1. Add the final newline (`make format` will do it).
2. Run `make format-check` and `make license-check` before committing; make them part of CI (OPS-03).
3. Consider wiring `format-check` into the build stamp so `make build` fails on drift.
