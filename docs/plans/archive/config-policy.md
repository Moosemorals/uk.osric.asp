<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Policy values come from configuration

| | |
| --- | --- |
| Status | Landed: the rule, the reader, every option migrated, the shipped file, and the tests |
| Size | Large — a configuration reader, every options record migrated, the shipped file, the startup path and the test harnesses |
| Area | Web (configuration, pages, assistant, metrics, authentication, admin), tests, docs, deploy |
| Backlog | [IDEA-031](../../jobs/IDEA-031.md) — the policy-shaped constants that deliberately stay |
| Decisions | Proposed in prose: the number is taken on `trunk` when this work merges |

## The short story

The repository already read most operator-facing limits from configuration, but every options record
also carried a fallback in code. That is two sources of truth: a missing key silently became a
build-time default, so an operator could not tell whether the running value came from configuration
or from the build.

This change makes configuration the only source.

- **Every value read from `IConfiguration` is required.** There is no fallback in code. A missing,
  blank, malformed or out-of-range value stops the process at startup.
- **The operator gets one report, not a stack trace.** `ConfigurationReader` records a problem
  rather than throwing, every section is built through one reader, and a single `Complete` reports
  every bad key in every section at once, with the accepted range and the value this repository
  ships. `Program.Main` writes that to standard error and exits non-zero.
- **`appsettings.json` is the shipped source of truth.** A production override may be partial,
  because configuration providers merge key by key.
- **The rule is written down** in the agent instructions and the architecture, and each subsystem
  document says what its own keys are for.
- **The clear-cut tail is migrated:** sign-in throttling, the authentication ticket lifetime, the
  assistant's tool and shell limits, and the metrics cache and query deadline.
- **Security policy that configuration could only weaken** stays an invariant and is recorded as
  [IDEA-031](../../jobs/IDEA-031.md).
- **Enforcement is documentation and review**, deliberately: no analyzer and no reflection guard. The
  tests prove the contract and the shipped file, not that every future literal is caught.

The decision this plan proposes is one sentence: *a policy value comes from configuration, never from
a literal or a code fallback, and a configuration the site cannot run with stops it at startup.*

## Scope

In scope: every value the Web shell reads from `IConfiguration`, whether through a
`FromConfiguration` factory, a direct indexer or an options binder.

Out of scope, and explicitly invariants: cryptographic and wire-format constants, schema constraints,
algorithm work caps, internal retry counts and sweep periods, the safety floors and ceilings that
range a configured value, framework options derived from configuration, `Logging`, `AllowedHosts` and
hosting settings. A security policy is configured only in the direction that keeps or tightens the
shipped guarantee; one that configuration could only weaken stays an invariant.

Optional values are explicit rather than absent: `Assistant:ApiKey` is required only while the
assistant is enabled and the provider requires one, `Assistant:WritablePathPrefix` and `Pages:Layout`
may be blank because blank is a meaningful setting, and `Pages:Fetch:Credentials` may be absent when
no page names a credential.

## What changed

### The reader

`Web/Configuration/ConfigurationException.cs` is the expected failure. `Web/Configuration/ConfigurationReader.cs`
records a problem per key rather than throwing at the first, and `Complete` throws one
`ConfigurationException` listing them all. Readers share the problem list — a nested section, a
section another borrows from, and `Program`'s own reader included — so the whole configuration is one
report. Every options record has two factories: one from `IConfiguration`, which reports its own
section and throws, and one from a `ConfigurationReader`, which joins the caller's report.

The message is the product, one line per bad key across every section:

```
The site cannot start because its configuration is incomplete or invalid:
  Pages:Render:Slots is 0, which is outside the accepted range of 1 to 64. Set it to a whole number from 1 to 64; 2 is recommended.
  Pages:Read:MaxRowsPerList is "not a number", which is not a whole number. Set it to a whole number from 1 to 10000; 500 is recommended.
  Metrics:TimeZone is "Mars/Olympus", which this host does not know; use an IANA zone such as Europe/London.
Set these values in appsettings.json, appsettings.<Environment>.json or an environment variable, then start the site again.
```

The recommendation is guidance for the operator, never a fallback: the value the site runs with is
the one in the file.

`Program.Main` becomes `Task<int>`, catches `ConfigurationException`, writes the message to standard
error and returns `1`. Error is the sink because the failure happens before the logger exists.

### Every options record

`RenderOptions`, `CompileOptions`, `PageStorageOptions`, `PageSessionOptions` (and `PageSessionCaps`),
`PageFetchOptions`, `PageReadOptions`, `ArchiveOptions`, `PageEditorOptions`, `PageLayoutConfiguration`,
`PageContentTypes`, `ReservedPaths`, `MetricsOptions`, `AssistantOptions`, `AuthenticationOptions` and
a new `AdminOptions` all read required values through the reader. Their properties are `required`, so a
half-populated policy cannot be built, and their `Default*` constants are gone; the maxima and floors
that keep a value from switching a guard off remain.

Three behaviours moved rather than merely changed shape:

- `ReservedPaths` refuses an empty list instead of falling back to the shipped one.
- `EditorPaths.Of` requires the editor key, because a request without services is a wiring mistake.
- `PageContentTypesConfiguration` catches the map's own `ArgumentException` and reports it as a
  configuration failure, so an unknown extension or a bad media type reads the same as a missing key.

`AssistantOptions` gained the five values the tools and the virtual shell used to carry as constants:
`MaxSearchMatches`, `DefaultListLimit`, `MaxListLimit`, `ShellMatchLimit` and `ShellDefaultLines`. The
model-facing prose stops naming a number and says the refusal names the accepted range; the prompt
version moves to `page-author-v27`.

### The shipped file

`appsettings.json` gained the `Pages:Archive` section it never had, the new `Authentication` section,
the metrics cache and deadline, and the assistant's listing limits. Two tests hold the file to the
code: every factory reads it, and its `Pages:ReservedPaths` is the list `ShellRoutes` documents.

### Tests

`ShippedConfiguration` can now be overridden and trimmed, and `TestOptions` builds every record
through its real factory on top of the shipped file, so the suite does not carry a second copy of the
values. New suites: `ConfigurationReaderTests`, `MetricsOptionsTests`, `AuthenticationOptionsTests`,
`ShippedConfigurationTests`, and `ConfigurationStartupTests` in the integration suite, which drives the
real host with bad values in several sections and asserts that one report names them all. The options
tests that asserted a fallback or a clamp now assert the refusal.

## Considered and rejected

| Option | Why not |
| --- | --- |
| Keep code defaults and log when one is used | Two sources of truth with a warning is still two sources; a deploy that half-works is worse than one that does not start |
| Report one section per start | Rejected on review: a hand-written file then costs one restart per section. One reader is threaded through every factory instead, and the report names every bad key at once |
| A reflection test over options and the shipped file | The chosen enforcement is review and documentation. `ShippedConfigurationTests` covers the part that actually bites — a factory and the file disagreeing — without pinning every future literal |
| A custom Roslyn analyzer | A new project and dependency for one owner, and it would have to whitelist the invariants the rule deliberately keeps |
| Move the password, header and API-policy constants too | Configuration could only weaken them. [IDEA-031](../../jobs/IDEA-031.md) asks whether a tightening-only key is worth having |
| Clamp an out-of-range value instead of refusing it | A clamp silently runs something other than what the operator wrote, which is the problem this change exists to remove |

## Verification

- `make build`
- `make test` — 760 core, 146 runtime, 1598 web and 222 integration tests
- `make format-check`
- `make license-check`

## What is unsure

- Whether every range is the one the owner would choose. They are a safety net rather than a
  straitjacket: wide enough that only a mistake meets them, and IDEA-031 is the place to argue about
  the ones that stayed in code.
- The integration test proves the report reaches standard error by capturing `Console.Error`, which
  is process-wide. The suite is not parallel, so it is safe today; a future parallel fixture would
  need a different seam.
