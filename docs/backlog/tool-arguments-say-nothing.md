<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The tools' arguments say nothing about themselves

| | |
| --- | --- |
| Difficulty | contented-capybara |
| Utility | high |
| Status | blocked |
| Blocked by | `check-without-storing` |
| Plan | [one-check](../plans/one-check.md) |
| Area | Core (Assistant), Web (assistant), docs |
| Location | `src/uk.osric.asp.Web/Assistant/Tools/`, `src/uk.osric.asp.Core/Assistant/ToolArguments.cs` |

## Idea

`ToolArgumentSchema` gained an optional `Description` for [the ask tool](../plans/archive/assistant-ask-owner.md),
so `ask_user`'s arguments explain themselves in the model's request body. Every other tool carries names only: the model reads `source`, `path`, `query`, `edits`, `content`, `kind` and
the rest with no sentence saying what goes in them or when they differ from a near neighbour.

[The survey](../spikes/ask-tool-shape.md) found the same two levers in every vendor's guidance, and put
them ahead of any schema choice: a description that says when to use a tool and when not, and a
description on each parameter. The first is already this site's habit; the second is not, for all but
one tool.

## Why it is not in `cannot-ask-the-owner`

The tool list is part of the request body, and its declaration order is part of the provider's cached
prefix (`assistant-a-tool-is-one-type`). Adding a sentence to every schema rewrites that prefix, so
the change is a choice about the tool definitions, not a by-product of adding one tool — and it wants a
before and after rather than an opinion.

## Sketch

1. **Measure first.** Record what a run's first request costs in cached and uncached input tokens today,
   and which tools the live suite picks wrongly — the ambiguous argument is the one worth the sentence.
2. **Write the ones that earn their place**, starting where a name is genuinely ambiguous: `edits`'
   per-edit fields, `source`'s and `query`'s scope, the two memory tools' arguments, the difference
   between `read_page` and `read_source`. A description that restates the name is cost with no gain.
3. **Measure again.** The new prefix's token count, and the same live suite, comparing tool choice rather
   than only pass or fail; a suite that passes either way says nothing about the change.
4. **Pin it.** If every argument ends up described, `ToolSpecsTests` asserts it for every registered tool
   rather than for the one waiting tool.
5. **Say a closed set once, in the schema.** `ToolArgumentSchema` gains an optional set of values beside
   the description, emitted as JSON Schema's `enum` only when it is set, so the other tools' request
   bodies are byte-identical.

## The one argument a description cannot fix

[`check-without-storing`](check-without-storing.md)'s `check` takes `language`, one of a closed set that grows as validators land
([`nothing-checks-page-assets`](nothing-checks-page-assets.md)). Prose can enumerate it, and prose goes stale: the tool's description would
have to be edited every time a language arrived, which is the drift `help` avoids by rendering the shell
registry. `enum` states the set where the model reads it as a constraint rather than a sentence, and
`enum` is what the provider guidance asks for in place of prose — unlike `maxItems` and `minLength`,
which DeepSeek's strict mode rejects and this site never opts into.

So the capability and the argument land together: one optional value on `ToolArgumentSchema`, one
declaration that uses it, and a new language later costs a row rather than a rewritten description.

## Not in scope

- **Renaming a tool or an argument.** The name is cited in the prompt, the tests and the transcript, and
  a rename is a different change with a different cost.
- **Describing a boolean one by one.** Where a value is a boolean or a two-value string, the parent
  argument's description carries its meaning; a sentence per flag is cost without a new fact.

## Where it lands

Slice 3 of [the check plan](../plans/one-check.md), which it opens: `check` and the argument
descriptions land together because both rewrite the tool definitions and so the provider's cached
prefix, and because the `enum` this job adds has no argument to describe until `check` exists. This
job closes at that merge; the sketch above is unchanged.
