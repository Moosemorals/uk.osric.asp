<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Testing

Work test-first. Write the failing test, make it pass, then refactor — the refactor step is where the
core stays pure and the shell stays thin.

## The three suites

| Project | Tool | Scope | Speed |
| --- | --- | --- | --- |
| `test/uk.osric.asp.UnitTests` | NUnit | Individual rules in the functional core | Milliseconds |
| `test/uk.osric.asp.Specs` | Reqnroll (Gherkin) + NUnit | Business behaviour, in the language of the domain | Milliseconds |
| `test/uk.osric.asp.WebTests` | NUnit | The template compiler/runtime and the web shell | Milliseconds to seconds |

- **Unit tests** cover one rule at a time and exercise `Core` directly.
- **Specifications** describe *what* the site does in plain language. Step definitions call the core,
  so the scenarios double as executable documentation. Start a feature here.
- **Web tests** cover what a unit test cannot reach: compiling and running generated handlers, and
  the endpoints and storage that talk to them.

## Running them

```bash
make test        # unit + specification + web suites; no browser needed
make test-unit   # unit tests only
make test-spec   # specifications only
make test-web    # web and runtime tests only
```

## Conventions

- Tests live in the namespace matching the project and use `using` directives inside the namespace, like
  production code.
- Name tests for the behaviour, not the method.
- One behaviour per test, with arrange/act/assert separated by blank lines.
- Prefer `[TestCase]` over copy-pasted tests for input tables.
- Test assemblies get the repository root embedded as `RepoRoot` assembly metadata
  (`Directory.Build.targets`), so infrastructure code never relies on the working directory.
