<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The page API admits what adds no capability

| | |
| --- | --- |
| Status | Merged 2026-11-15. The branch was fast-forwarded into `trunk` after `make check` was green |
| Size | Small — six list entries, one grouping rule in `Check`, one sentence in the message, three descriptions that say what a page may name, and their tests |
| Spike | None. Every claim was measured on `trunk` on 2026-11-15 by compiling templates through `PageCompiler`, and by counting the reference set's public types over `TRUSTED_PLATFORM_ASSEMBLIES` |
| Decisions | `pages-admit-what-adds-no-capability`, recorded at the merge; it refines `pages-the-api-policy` rather than replacing it (`product-plan-before-building`, `product-steps-wait-for-the-owner`) |
| Backlog | None. The threat model it sits inside is [SEC-03](../../backlog/sec-page-runs-in-process.md), unchanged |

## The short version

`System.StringComparer` is refused to a stored page, and so are `System.Random`, `System.HashCode`,
`System.Text.Json.JsonNamingPolicy` and `Environment.NewLine`. That is not a security boundary doing
its job; it is a list that was only ever extended by features the site needed — `Task` for async,
`System.Text.Json` for a fetched body, the SHA-2 hashes for signing — and never by asking what an
author reaches for.

The direction stays: default-deny is what makes the capability seam mean anything, and a deny list
over 4,277 types fails open. What changes is the rule the list is read by, and the batch the rule
admits:

> **A type is admitted when its only effect is to compute over a value the page already holds or to
> shape a value the page already returns. It is refused when it adds a way to reach a resource — a
> file, the network, a process, the environment, an assembly, reflection, or process-wide state. A
> type that bundles a way to reach with a useful helper is admitted with the reaching members denied
> by name, which is the mechanism `Task` already uses.**

So `StringComparer` goes in, `Environment` stays out (`NewLine` arrives attached to `Exit`), and a
refusal names both the member the author wrote and the type that would have to be admitted.

## What was measured

Compiled through `PageCompiler` on `trunk`, with the usings a page has to write itself:

| Template | Result |
| --- | --- |
| `new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)` | `PAGE001: pages may not use System.StringComparer.OrdinalIgnoreCase.` **and** `PAGE001: pages may not use System.StringComparer.` |
| `new Random(1)` | `PAGE001: pages may not use System.Random.` |
| `System.HashCode.Combine(1, 2)` | `PAGE001: pages may not use System.HashCode.Combine.` **and** `… System.HashCode.` |
| `new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` | `PAGE001: … System.Text.Json.JsonNamingPolicy.` **and** `… JsonNamingPolicy.CamelCase.` |
| `Environment.NewLine` | `PAGE001: … System.Environment.NewLine.` **and** `… System.Environment.` |
| `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` | compiles |
| `d["a".ToUpperInvariant()] = 1` | compiles |

Three things follow from the table.

- **The list contradicts itself.** `System.Collections.Generic` is an admitted *namespace*, so a page
  may declare `Dictionary<string, int>` and implement `IEqualityComparer<string>` by hand, but may not
  use the comparer the base library ships for exactly that. `System.Globalization` is admitted too, so
  a page **can** set `CultureInfo.DefaultThreadCurrentCulture` — process-wide mutable state, accepted
  as residual risk in [SEC-03](../../backlog/sec-page-runs-in-process.md) — while a readonly comparer over `string` is
  refused. Membership is not being decided by capability; it is being decided by whether a feature
  needed it.
- **Every refusal is a redeploy.** The author is in the editor. The fix for `StringComparer` is a
  source edit in `Core`, a version bump, a build and a deploy, for a type that adds nothing.
- **Each refusal costs two diagnostics, and neither names the entry to add.**
  [docs/pages.md](../../pages.md#page-api-policy) says the diagnostic "names the exact API to add"; it
  names `System.StringComparer.OrdinalIgnoreCase`, and the entry is `System.StringComparer`.

## Goals

1. A page may use the base types an author reaches for while working over data the page already
   holds, without the list having to grow one feature at a time.
2. The rule for adding the next one is written down, so the argument is about capability and not
   about taste.
3. A refusal is one diagnostic per refused type, naming the member the author wrote and the type that
   is not admitted.
4. The three descriptions of the policy — [docs/pages.md](../../pages.md#page-api-policy), the seeded
   `/help` manual and `PageAuthorPrompt` — agree with the list, in this change.
5. `make check` green, with the stored-image rule unchanged: a widened list moves the policy version.

## Non-goals

- **A deny list.** Rejected, and not reopened by this plan. The page compiler's reference set is the
  SDK plus the base class library: 166 `System.*`/`Microsoft.Win32.*` assemblies, **4,277 public types
  in 128 namespaces**, against 50 named types and two namespaces here. A deny list must be correct
  over the complement, it fails open where this fails closed, and one omission is not an annoyance but
  a break: `System.Runtime.CompilerServices.Unsafe` and `MemoryMarshal` are arbitrary memory access,
  `Marshal.GetDelegateForFunctionPointer` over the admitted `Func`/`Action` is arbitrary native calls,
  and `GetType`/`Type.GetType`/`Activator` reach the assemblies `PageLoadContext` exists to refuse. It
  also cannot state the rules the design relies on, because the dangerous type looks ordinary: the
  rule is "a page may not read another page's state", and the type is `File`.
- **Configuration-driven additions.** The friction is real, but a deployment-owned widening of a
  security policy needs its own decision, and this change does not need it.
- **Per-page trust tiers.** There is one author. The seam belongs to the day there is more than one.
- **`System.Environment`.** See D3.

## The design

### D1 — default deny stays, and membership is decided by capability

`s_allowedTypes` keeps its shape and its position in `Core`; what changes is the test a candidate has
to pass, stated in the remarks of [`PageApiPolicy`](../../../src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs)
and in [docs/pages.md](../../pages.md#page-api-policy). The test is the one quoted in the short version:
it computes over what the page already holds, or it shapes what the page already returns.

Two corollaries, both already true of the code and neither written down:

- The list is read at *type* granularity except where a type bundles a capability with a helper, and
  there `s_deniedApis` denies the members. `Task` is the only user of that mechanism today; the rule
  now says when to use it rather than leaving it as a one-off.
- A namespace is admitted only when every type in it passes the test. `System.Collections.Generic`
  and `System.Globalization` are read as such; `System.Text.Json.Nodes` joins them (D2).

### D2 — the batch this change admits

| Entry | Why it passes the test |
| --- | --- |
| `System.StringComparer` | A comparer over `string`, which is admitted, and `IEqualityComparer<T>` is admitted by namespace. It adds nothing a page cannot build by hand |
| `System.HashCode` | Combines hashes of values the page holds |
| `System.Random` | A number for page-visible variety. `/help` and the author prompt both say `RandomNumberGenerator` for anything security-shaped, and `RandomNumberGenerator` is already admitted |
| `System.Text.Json.JsonNamingPolicy` | Shapes the names a deserializer matches. `JsonSerializerOptions` is admitted and is useless against a snake_case API without it |
| `System.Text.Json.JsonDocumentOptions` | The settings argument to `JsonDocument.Parse`, which is admitted. The type that names the parameter should be nameable |
| `System.Text.Json.Nodes` (namespace) | `JsonNode`, `JsonObject`, `JsonArray`, `JsonValue`, `JsonNodeOptions`: building JSON as a value. The same capability as reading one through `JsonDocument`, which is admitted |

No entry here needs a member deny: none of them binds a way to reach a resource to a helper.

### D3 — considered and not admitted

- **`System.Environment`.** The type is the capability, not `NewLine`: `Exit`, `FailFast`,
  `GetEnvironmentVariable`, `GetEnvironmentVariables` and `CurrentDirectory` arrive with it. Admitting the type to deny six members trades a documented, one-line reason for a
  worse-reviewed surface, and a page that wants a line break writes `"\n"`.
- **`System.TimeZoneInfo`.** It computes only over a `DateTime`, but it reads the host's zone database
  — a resource the page contract does not model — and nothing needs it. `DateTimeOffset` is admitted.
- **`System.Collections.Immutable`, `System.Collections.ObjectModel`.** Copies of abstractions already
  admitted; no page has asked.
- **`System.Text.RegularExpressions`.** Unchanged: a CPU amplification path inside a deadline that
  cannot reclaim its thread ([SEC-03](../../backlog/sec-page-runs-in-process.md)). A page that needs matching gets a
  capability.

### D4 — one diagnostic per refused type

`CheckApi` adds a reference for the node's symbol and another for the node's type, so
`StringComparer.OrdinalIgnoreCase` is refused twice: once as the property and once as its container.

`PageApiPolicy.Check` groups the references by `namespace.type` and drops the member-less reference
when the same type is also named with a member. The rule lives in `Core` beside the list, not in the
walk, because the walk has no policy knowledge and should not acquire any:

- `StringComparer.OrdinalIgnoreCase` → one diagnostic, naming the member.
- `new Random(1)` → one diagnostic, naming the type (the constructor reference carries no member).
- `ctx.GetType()` → still two diagnostics, `System.Object.GetType` and `System.Type`: they are two
  different refused types, so the rule has nothing to fold. Only the same type refused twice is one
  line.
- `Task.Run` and `Task.Wait` are two members of one type and stay two diagnostics.

### D5 — the message names the type, not only the member

One sentence today, one sentence plus a clause:

| Case | Message |
| --- | --- |
| A member of an admitted type, denied by `s_deniedApis` | unchanged — `PAGE001: pages may not use System.Threading.Tasks.Task.Wait; it starts or blocks work the render cannot stop.` |
| A member of a refused type | `PAGE001: pages may not use System.IO.File.ReadAllText. The type System.IO.File is not part of the page API.` |
| A refused type, no member named | `PAGE001: pages may not use System.IO.File.` |

The clause is factual rather than an instruction, because the message is shown in the editor to
whoever saved the page, and the list is a source file rather than a setting.

### D6 — the policy version moves

`PolicyVersion` in [`PageCompiler`](../../../src/uk.osric.asp.Runtime/PageCompiler.cs) goes from 4 to 5,
as its comment asks. A widened list does not make an existing image wrong, but the rule is that a
change to the policy rechecks every stored page on its next request, and keeping to it is cheaper
than reasoning about which changes are safe.

### D7 — the three descriptions move with it

- [docs/pages.md](../../pages.md#page-api-policy): the bullet that enumerates the list gains the batch,
  the `Environment` reason, and the membership rule as the paragraph that introduces the list.
- The seeded `/help` manual: a short subsection where the diagnostics are explained, saying what
  `PAGE001` means, that the list is `PageApiPolicy`, and the three substitutes an author reaches for
  — `StringComparison` on `String.Equals`, `ToUpperInvariant` for a case-insensitive key, and
  `RandomNumberGenerator` for anything security-shaped. It is the author manual, and a page author
  who hits the wall has nothing else to read.
- `PageAuthorPrompt`: the API policy paragraph gains `StringComparer`, `Random`, `HashCode`, the JSON
  naming policy and `System.Text.Json.Nodes`, so the assistant stops writing pages that fail to
  compile for a reason it cannot see.

## Phases

### Phase 1 — the list (Core)

The six entries, the rule in the remarks, and the `PageApiPolicyTests` cases for each. Green on
`make test-core`.

### Phase 2 — the refusal (Core + Runtime)

The grouping rule in `Check`, the message clause, the `PolicyVersion` bump, and the tests in D4 and
D5. Two existing tests assert the old text and change with it: `RejectionNamesTheApiAndThePageLine`
in `Core.Tests` and the `System.IO` case in `Runtime.Tests` — the latter passes unchanged because it
matches a substring.

### Phase 3 — the descriptions

`docs/pages.md`, the `/help` seed and `PageAuthorPrompt`. `make check-pages` compiles the seed; no
behaviour is added, so no new test.

## Tests

`Core.Tests/PageApiPolicyTests`:

- One `TestCase` per entry in D2, allowed, beside the existing refusals.
- `System.Environment`, `System.TimeZoneInfo` and `System.Text.RegularExpressions.Regex` refused — D3
  as a test, so a later widening has to argue with it.
- `ANamingMemberIsRefusedOnce`: the member and the type reference for one type yield one diagnostic.
- `TwoMembersOfOneRefusedTypeAreReportedSeparately`: the fold is per type, and two members of one
  refused type stay two refusals.

`Runtime.Tests/PageApiPolicyTests`:

- `APageMayUseTheOrdinaryBaseTypes` and `AJsonBodyCanBeBuiltAndItsNamesShaped`: templates that
  construct a case-insensitive dictionary, combine hashes, take a `Random`, parse a `JsonNode` and set
  `JsonNamingPolicy.CamelCase` compile. These are the behaviour that fails without the change.
- `ADeniedTypeIsReportedOnce`: `System.IO.File` reaches the author as one `PAGE001`, not two.
- `EnvironmentIsStillRejected`, beside the existing refusals.

## What proves it

`make check` — format, licence, audit, decisions, `check-pages`, the four suites and `publish`.

## Risks

- **`Random` reads as permission to mint a token.** It is not: a page cannot authenticate anyone —
  the page contract has no path to the shell's cookie or its user store — and both descriptions point
  at `RandomNumberGenerator`. Worth watching only if a capability ever trusts a page-minted value.
- **More JSON surface is more parser surface.** `JsonNode` parses with the same depth cap as
  `JsonDocument`, and a page's tree is memory the render budget already bounds by the container.
- **The rule is a judgement, not a test.** D3 is where the judgement shows; it is written down so the
  next candidate is argued against it rather than against the last author's mood.

## Status

Merged into `trunk` on 2026-11-15 after `make check` came back green (format, licence, audit,
decisions, `check-pages`, 827 Core, 174 Runtime, 1805 Web, 232 integration, publish). The entry is
`pages-admit-what-adds-no-capability` in [the record](../../decisions.md).

Two things the build added that the phases did not name:

- `PageCompilerTests.RuntimeIdStartsWithTheFormatAndPolicyVersions` pins the policy number, so D6's
  bump is a one-line test change as well as a one-line constant.
- `PageAuthorPrompt.Version` moves, with the three assertions that pin it, because the policy
  paragraph is part of what a stored run records. It was `v29` to `v30` on this branch;
  `pages-a-comment-is-not-code` landed `v30` the same day for the template comment, so the merge
  folded the two and moved it to `v31`.
