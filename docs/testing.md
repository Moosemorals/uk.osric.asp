# Testing

Work test-first. Write the failing test, make it pass, then refactor — the refactor step is where the
core stays pure and the shell stays thin.

## The three suites

| Project | Tool | Scope | Speed |
| --- | --- | --- | --- |
| `test/uk.osric.asp.UnitTests` | NUnit | Individual rules in the functional core | Milliseconds |
| `test/uk.osric.asp.Specs` | Reqnroll (Gherkin) + NUnit | Business behaviour, in the language of the domain | Milliseconds |
| `test/uk.osric.asp.EndToEnd` | Playwright + NUnit | Real journeys through a real browser and a real server | Seconds |

- **Unit tests** cover one rule at a time and exercise `Core` directly.
- **Specifications** describe *what* the site does in plain language. Step definitions call the core,
  so the scenarios double as executable documentation. Start a feature here.
- **End-to-end tests** are few and deliberate: they prove the shell is wired up (routing, binding,
  views), not that the business rules work. Anything a unit test can prove does not belong here.

## Running them

```bash
make test        # unit + specification suites; no browser needed
make test-unit   # unit tests only
make test-spec   # specifications only
make e2e         # Playwright suite
```

`make e2e` builds the solution, installs Chromium into `~/.cache/ms-playwright` if it is missing, runs
the web app on a free loopback port, and shuts it down afterwards. The first run downloads the browser
and takes a few minutes; later runs are quick.

On a minimal Linux machine Chromium may also need system libraries. If it reports missing shared
objects, install them through the distribution package manager (or run Playwright's installer with
`--with-deps`).

## Conventions

- Tests live in the namespace matching the project and use `using` directives inside the namespace, like
  production code.
- Name tests for the behaviour (`Greets_the_world_when_no_usable_name_is_given`), not the method.
- One behaviour per test, with arrange/act/assert separated by blank lines.
- Prefer `[TestCase]` over copy-pasted tests for input tables.
- Test assemblies get the repository root embedded as `RepoRoot` assembly metadata
  (`Directory.Build.targets`), so infrastructure code never relies on the working directory.
