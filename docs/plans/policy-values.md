<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The password rule is the one policy an operator may turn

| | |
| --- | --- |
| Status | Proposed 2026-11-19 — waits for the owner. Nothing below is built |
| Size | Small — one options record, one pure function given its bounds, one row removed from a table, and the tests, settings and decision that go with them |
| Area | Web (authentication, users), docs |
| Spike | None. Every claim below was read off `trunk` on 2026-11-19: `PasswordPolicy`, `UserName`, `SecurityHeaders`, `PageApiPolicy`, `PageEndpoint`, `PageRenderer`, `SqliteErrors`, the four background services, `ShellCatalogue` and `AuthenticationOptions` |
| Backlog | [`policy-is-a-constant`](../backlog/policy-is-a-constant.md) is this step, and it closes at the merge |
| Decisions | At the merge: `auth-passwords` is edited in place to say the floor is 12 and the ceiling 1024 and that configuration may tighten either; `code-policy-values-are-configuration` loses its citation of the item, which has closed |

## The short version

[Configuration](../architecture.md#configuration) is the only source of a policy value, and
[`policy-is-a-constant`](../backlog/policy-is-a-constant.md) lists the eight values that stayed in
code so the exception is not silent. The item asks two questions of each — can configuration only
tighten it, and is it a constraint or a preference — and this plan answers them: **two** qualify and
become keys, **six** stay constants.

The two are the password's bounds. A minimum of 12 and a maximum of 1024 are both *preferences*
defended in one direction only: an operator with a stricter rule wants a longer minimum, and an
operator who wants a cheaper hash wants a shorter maximum. Neither can be made weaker by a setting
if each key's accepted range starts at the shipped value, so this is the tightening
`code-policy-values-are-configuration` permits, not an exception to it.

Everything else stays: a user name's 64 characters is a column length, the CSP is already the
tightest policy that works, the API allow-list is default-deny data a page may not reach, the 1 MiB
form body bounds a buffered read, and the retry counts and compile attempts are algorithms rather
than preferences. The item's table keeps a sentence for each.

## What exists today

| Value | Where | What it actually is |
| --- | --- | --- |
| Password length 12–1024 | `Users/PasswordPolicy` | two `const`s, used by `UserCommands` when a password is set — and nowhere on the sign-in path, which verifies whatever it is given |
| User name length 64 | `Users/UserName` | a `const` that is also five `HasMaxLength` calls in `StorageDbContext` and four EF models |
| Header set and the CSP | `Http/SecurityHeaders` | a fixed list plus `default-src 'none'` with `script-src` widened to `'self'` per response by `AllowScripts` |
| The page API allow-list | `Core/Compilation/PageApiPolicy` | data: `Sdk` allowed, everything else denied |
| The 1 MiB form body | `Pages/PageEndpoint` | `MaxFormBytes` on a body the pipeline buffers before the page runs |
| Compile attempts, SQLite retries | `Pages/PageRenderer`, `Storage/SqliteErrors` | 3 and 3, each a bound on a race rather than on a caller |
| Cleanup and sample periods | the four background services | 1 h and 5 min and 1 min sweeps and a 10 s sample; the balance sampler's period is already `Assistant:BalanceSampleSeconds` |
| The reserved keys | `Shell/ShellCatalogue` | the shell's own route table; `Program` already refuses a leftover `Pages:ReservedPaths` key |

## The two questions, answered

| Value | Can configuration only tighten it? | Constraint or preference? | Answer |
| --- | --- | --- | --- |
| Password **maximum** | Yes — a shorter maximum is stricter, and it bounds the hasher's work | preference | **Expose** as `Authentication:Password:MaximumLength`, range 12–1024 |
| Password **minimum** | Yes — a longer minimum is stricter | preference | **Expose** as `Authentication:Password:MinimumLength`, range 12–1024, and never above the maximum. The item doubted this one because a shorter password is not a legitimate choice; the range is the thing that stops one, and a site with a stricter rule wants a longer floor |
| User name length | No — a shorter column breaks every existing name | constraint | Stays |
| The headers and the CSP | No — there is nothing left to tighten: the policy is already `default-src 'none'`, and every setting could only widen it | rule | Stays |
| The API allow-list | No — an entry removed is a code change to `Core`, not an operator knob, and a page that could edit it grants itself APIs | data | Stays |
| The 1 MiB form body | Yes — a smaller body is stricter | a bound on a buffered read | Stays; see open question 2 |
| Compile attempts, SQLite retries | No — a different number is a different algorithm | algorithm | Stays |
| The sweep and sample periods | Not really — a shorter sweep only makes an expired row linger for less time, and the sampler's period is already a key | bound, not a guarantee | Stays; see open question 3 |
| The reserved keys | No — a shorter list lets a page shadow `/login` | the route table | Stays |

## The change

1. **`PasswordOptions`.** A record beside `AuthenticationOptions`, with the two factories every
   options record has — one from `IConfiguration` that reports and throws, one from a
   `ConfigurationReader` that adds to the caller's report. `MinimumLength` is recommended 12 and
   accepted 12–1024; `MaximumLength` is recommended 1024 and accepted 12–1024; a section-level
   `Fail` when the minimum exceeds the maximum names both keys, because that is the one pair whose
   mistake is a valid number each.

2. **One report.** `AuthenticationOptions` gains `Password`, read through
   `reader.For("Password")`, so a bad password bound is reported in the same start as a bad ticket
   lifetime.

3. **The rule takes its bounds.** `PasswordPolicy` stops holding the numbers:
   `Validate(PasswordOptions policy, string password)` stays a pure function, and its two messages
   name the configured lengths rather than a constant.

4. **The caller supplies them.** `UserCommands.RunAsync` resolves the `AuthenticationOptions` the
   host already registers as a singleton and passes `options.Password`.

5. **`appsettings.json`** gains `Authentication:Password` with `MinimumLength` 12 and
   `MaximumLength` 1024.

6. **Tests.** `PasswordPolicyTests` builds the policy it tests rather than reading a constant, and a
   new `PasswordOptionsTests` proves the shipped values, an override, and the three refusals: a
   minimum below 12, a maximum above 1024, and a minimum above the maximum. `ShippedConfigurationTests`
   already reads `AuthenticationOptions`, so it proves the new keys are in the file by construction.

7. **The record.** The item's table loses the password row, gains a sentence naming the two keys, and
   keeps the other six — reworded only where the reading above sharpened the reason. The item moves
   to [closed.md](../backlog/closed.md) at the merge.

8. **The docs.** `architecture.md#configuration`'s "what stays in code" bullet stops pointing at the
   item and states the rule alone, and `code-policy-values-are-configuration` loses the same
   citation, because the item it points at has closed. `auth-passwords` is edited in place.

Nothing a page author can do changes, so `/help` is untouched.

## Open questions

1. **The minimum.** The item's own sketch said only the maximum was plausible. This plan reads the
   rule's wording — "configured only in the direction that keeps or tightens the shipped guarantee"
   — as covering both, and proposes exposing both. The narrower answer is to expose the maximum only
   and keep the floor as `auth-passwords` fixes it.
2. **The form body.** A tighten-only key would be legitimate under the rule, but a form smaller than
   the page that reads it is a page that silently fails at some size, which sounds like the operator's
   footgun rather than their policy. The plan keeps 1 MiB a constant.
3. **The periods.** `Assistant:BalanceSampleSeconds` already proves a period can be a key. The three
   sweeps are proposed to stay because nothing observable changes when they do; if the owner reads a
   sweep interval as policy whatever it does, they become three keys and three settings.
4. **The section's name.** `Authentication:Password` puts the rule under the thing that uses it; a
   separate `Users:Password` would keep account rules that are not sign-in — the user name length —
   together with it. The plan follows the only caller and the existing section.

## Not in scope

- **A weaker rule.** No key may lower the minimum or raise the maximum.
- **Composition rules.** `auth-passwords` refuses them, and a setting is not the way to add one.
- **The user name length.** It is a column before it is a policy, and moving it needs a migration.
- **Anything on the sign-in path.** A password is verified as given; only setting one is checked.
