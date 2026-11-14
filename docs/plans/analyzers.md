<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Analysis at full strength

| | |
| --- | --- |
| Status | Done — DEC-290. The full set is on, the build is clean, and every exception carries its reason |
| Size | Medium — a build policy, an exception list, about ninety fixes in `src` and the tests, and a documentation file per product assembly |
| Spike | None. The survey below is the measurement: the rules are already in the SDK, and turning them on is one property |
| Decisions | DEC-290: the full `CA` rule set is on, and every exception is named in `.editorconfig` with its reason |
| Backlog | — |

## The short version

The build runs the SDK's default analyzer set with warnings as errors, so the tree is clean against
it. It is not clean against the rules the SDK leaves off. `AnalysisMode=All` turns on every `CA`
rule, including the ones that are noisy in general, and the survey below is what that produces.

The job is not "make the warnings go away". It is to decide, rule by rule, whether this codebase
follows the rule; fix the code where it does not; and write the exception down where it does not
want to. A muted warning with no reason is the thing the current setup already avoids; the full set
has to keep that promise at a larger scale.

## What the sweep found

The baseline is already healthy: `dotnet format` changes 0 of 719 files, REUSE covers 839 of 839,
no top-level package is vulnerable or deprecated, and the current build has no warnings.

With `AnalysisMode=All` the same tree produces 5,627 warnings: 2,268 in `src` and 8,986 in `test`
(a file linked into two test projects reports twice). By rule and by area:

| Rule | `src` | `test` | Verdict |
| --- | ---: | ---: | --- |
| CA2007 ConfigureAwait | 1,836 | 7,374 | Exception: there is no synchronization context to capture |
| CA2234 Uri instead of string | — | 656 | Exception in tests: a request path is a string |
| CA1515 Make public types internal | 16 | 458 | Fix in `src` (nested types in internal types); NUnit fixtures must stay public |
| CA2000 Dispose before losing scope | — | 342 | Exception in tests: `HttpClient` owns request content |
| CA1848 LoggerMessage delegates | 202 | 2 | Exception: source-generated loggers duplicate every call site |
| CA1307 Specify StringComparison | 78 | 38 | Exception: every hit is a `char` overload, which is already ordinal; CA1310 is clean |
| CA1062 Validate arguments | 2 | 20 | Fix in `src`; test helpers do not guard |
| CA1054/55/56 Uri as string | 8 | 12 | Exception: the page contract speaks strings |
| CA1031 Catch general exception | 20 | — | Reviewed site by site; each is a boundary that logs and carries on |
| CA2100 Review SQL query | 14 | 4 | Fixed statements only, never user input: reviewed and marked |
| CA1308 Normalize to uppercase | 10 | 12 | Exception: lowercase is the normal form for paths, hashes and keys |
| CA1849 Call async methods | 14 | 18 | Fix where the call is genuinely blocking; the rest are tests |
| CA1032 Exception constructors | 14 | — | Exception: these exceptions exist only with their message |
| Other (`CA1001`, `CA1068`, `CA1305`, `CA1716`, `CA1720`, `CA1819`, `CA1822`, `CA1861`, `CA2025`, `CA5394`, `CA1812`, `CA1869`, `CA2027`, `CA1034`, `CA1826`) | 38 | 46 | Fixed, or a reviewed mark where the rule does not apply |

Outside the `CA` rules the same sweep found:

- `RestoreEndpoint.HasErrors` is never called, and `ChartChrome` takes a `plot` parameter it never reads.
- Three suppressions are no longer suppressing anything (`IDE0370`), and several casts are redundant.
- Twelve tracked files do not end with a newline, against `.editorconfig`'s `insert_final_newline`.
- No product assembly emits an XML documentation file, so "XML-document the public surface of
  `Core`" is a convention rather than a build failure. Nine public members are undocumented.
- `Microsoft.NET.Test.Sdk` 18.11.0 is a patch behind, and `Microsoft.Playwright.NUnit` 1.59.0 is
  behind the browser it drives.

## Decisions

### D1 — The full rule set is the default, and the exceptions are named

`Directory.Build.props` sets `AnalysisMode=All` with `AnalysisLevel=latest`. `TreatWarningsAsErrors`
is already on, so a new violation fails the build in `make build`, not only in `make check`.

Every rule this codebase does not follow gets a `dotnet_diagnostic` entry in `.editorconfig` under
`[*.cs]`, beside a comment that says why. The exception is a decision someone can review; nothing
is muted anonymously, and a rule that is not listed stays on.

### D2 — Test code has its own exceptions, scoped to `test/**`

The test tree is where most of the noise is, and it is noise: NUnit fixtures must be public, request
content is owned by `HttpClient`, a request path is a string, and test randomness is not a secret.
Suppressing these globally would blunt them in `src`, so they are scoped to the test tree and named
one by one. The `src`-side rules stay on.

### D3 — The noisy-but-real rules are fixed, not muted

`CA1305` (culture-dependent text in generated source), `CA1849`, `CA1822`, `CA1062`, `CA1068`,
`CA1001`, `CA2025`, `CA1861`, `CA1515` (nested types) and `CA1812` are fixed in `src`. A few sites
get a `#pragma` with a reason because the rule cannot see the invariant: the SQL helpers
(`CA2100`), the retry jitter (`CA5394`), the emitted byte arrays (`CA1819`), and the
log-and-continue boundaries (`CA1031`) — the last of which is the repo's stated rule for a worker
loop, applied deliberately rather than by accident.

### D4 — Five IDE rules are promoted because they find defects

`IDE0051` (unused private member), `IDE0059` (unnecessary assignment), `IDE0060` (unused parameter),
`IDE0004` (redundant cast) and `IDE0370` (unnecessary suppression) move from suggestion to warning.
They found real dead code in this sweep. The rest of the style category stays where it is: making
`IDE0022` an error would rewrite every method in the tree for no behavioural gain.

### D5 — A product assembly publishes its documentation

`GenerateDocumentationFile` is on for the product assemblies and off for test and tool projects, and
the nine undocumented public members get documentation. This turns the architecture document's "the
public surface of `Core` is documented" into something the build holds.

## What this touches

| Area | Change |
| --- | --- |
| [`Directory.Build.props`](../../Directory.Build.props) | `AnalysisMode`, `AnalysisLevel`; documentation file for product assemblies |
| [`.editorconfig`](../../.editorconfig) | The exception list with reasons, the test-tree section, the five promoted IDE rules |
| `src/**` | Around sixty fixes: disposables, static members, guards, parameter order, culture, nested visibility, exception and array shapes |
| `test/**` | The warnings the scoped exceptions do not cover |
| [`Directory.Packages.props`](../../Directory.Packages.props) | `Microsoft.NET.Test.Sdk` to 18.12.0, `Microsoft.Playwright.NUnit` to 1.64.1 |
| [`docs/testing.md`](../testing.md) | What the gate now proves about analysis |

## What landed

- `AnalysisMode=All` and `AnalysisLevel=latest` in `Directory.Build.props`; `TreatWarningsAsErrors`
  already there, so the full set is enforced by `make build` and not only by the gate.
- Twenty-three rules named in `.editorconfig`: ten for the whole product, twelve scoped to
  `test/**`, one scoped to `ToolArguments.cs`. Five `IDE` rules promoted to warning.
- Ninety files changed. The largest groups were the nested types that were `public` inside internal
  types (`CA1515`), the culture-dependent text in the generated `#line` directives (`CA1305`), the
  sync calls in async methods (`CA1849`), and the constants and guards the full set asked for.
- Two behaviours made explicit rather than changed: `RunStream` is now `IDisposable` and the
  registry disposes it when it expires, and the assistant's renderer dependency left `ToolServices`
  when the renderer's stateless members became static.
- `Microsoft.NET.Test.Sdk` 18.12.0 and `Microsoft.Playwright.NUnit` 1.64.1, with the Firefox build
  the suite drives reinstalled to match.

## Verification

1. `dotnet build uk.osric.asp.slnx --no-incremental` with the new settings: zero warnings.
2. `make check` — format, licences, audit, seeded pages, every suite, Release publish.
3. `make browsers` then `make test-browser`, because the Playwright package moved.
4. `make image-check` is not needed: no runtime behaviour changes, and the gate already publishes.

## Risks

- **Build time.** The full rule set adds roughly twenty seconds to a clean compile at this size. The
  stamped build is unchanged; only a source change pays it.
- **Rule churn.** Enabling `All` also enables rules a future SDK may add. The exception list is the
  place to argue with them, and the default stays strict.
- **Test churn.** The scoped exceptions mean a genuinely unguarded test helper will not be reported.
  That is the same trade the repo already makes for page code (DEC-02's spirit): tests are evidence,
  not product surface.
