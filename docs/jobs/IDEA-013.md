<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-013 — There is no glossary, so the site's own words are guesswork

| | |
| --- | --- |
| Size | Medium |
| Area | Documentation, vocabulary |
| Location | `docs/` |

## Idea

The solution has built up a vocabulary of its own and nothing defines it. The words are not
interchangeable, but the prose uses them as if they were, and the code is the only authority.

The assistant vertical makes the cost visible. Five words are in circulation for four things —
conversation, message, **run**, **step** and **tool call** — and "turn" floats across all of them
without ever appearing in code: it means a whole exchange in "a multi-turn conversation" and a single
provider round trip in "the transcript sent to the provider each turn". The smaller of those two is
already called a *step* by `Assistant:MaxSteps`, `StepCount`, the `step` field on every event and the
metric `assistant.run.steps`. So the docs say one word for two units, one of which the code has
already named better. Reading that cold, there is no way to tell which is which — which is exactly
the question that produced [the naming plan](../plans/archive/naming-consistency.md#terms-this-plan-depends-on).

The pages vertical is the same, and larger: **page**, **key**, **path**, **head**, **revision**,
**template**, **image**, **runtime id**, **slot**, **budget**, **staleness**, **tombstone**, **scrub**
and **seed** are all distinct, all used in prose, and all one careless sentence away from each other. A
page is not a revision; a revision is not a template; the image is not the page; a key is not a path;
a seed is not a page even though restoring a seed writes one.

And the same will happen to the observability words — meter, instrument, metric, series, panel,
dashboard — as soon as there is more than one person reading them.

There is a second, cheaper cost visible already: `docs/decisions.md` is 120 rows of the same
vocabulary, and most of its value is lost to a reader who does not already know what a "revision" or
a "run" is on first use.

## Sketch

1. One file, `docs/glossary.md`, alphabetical, one entry per term. Each entry gets three lines:
   - a one-sentence definition;
   - **what it is not** — the neighbouring term it is most often confused with, and the difference
     that matters;
   - the type, table, config key or metric prefix that owns it, so the code stays authoritative and
     the glossary only explains.
2. Start with the three vocabularies that have already caused confusion, in this order:
   - **assistant** — conversation, run, step, message, tool call, tool approval, journal, transcript;
   - **pages** — key, path, head, revision, template, image, runtime id, slot, budget, staleness,
     tombstone, scrub, seed;
   - **observability** — meter, instrument, metric, series, panel, dashboard, scrape.
3. Retire "turn" from prose: **step** for the provider round trip, **exchange** for a user message plus
   everything it sets off. Grep `docs/` for the word and fix the handful of real uses; the rest are the
   ordinary English verb.
4. Do not let the glossary grow rules. If an entry needs to say what *must* happen, that is a `DEC-`
   row and the glossary links to it. A glossary that restates behaviour becomes a second, stale source
   of truth for it.
5. Link it once from [architecture.md](../architecture.md) and from each plan's terms section, and
   delete those sections in favour of the link as they are superseded.

The cheapest guard against rot is that the last line of every entry is a pointer into code. The code
cannot drift from itself, so the glossary can only ever be incomplete — it can never be confidently
wrong.

## Relationship to other work

[The naming plan](../plans/archive/naming-consistency.md) needs the assistant half of this now, because its
central decision — that `Run` keeps its name — is an argument about what `Run` is not. That plan
carries a short terms table and links here for the rest. Doing the glossary first would have made that
plan shorter, and will make the next one shorter still.
