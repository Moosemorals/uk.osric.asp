<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Documentation

The site's design record and its work. [Architecture](architecture.md) is the map; each subsystem
page below describes one part of it, [decisions](decisions.md) is the named record of what was
settled and why, and [the backlog](backlog/README.md) and [plans](plans/README.md) are what is still
to do. Everything here is checked: `make decisions`, `make backlog` and `make check-pages` fail a
record that has drifted from itself.

## Contents

- [Architecture](architecture.md) — the functional core and the imperative shell, where code goes,
  configuration, visibility, logging and the rest of the system.
- [Pages](pages.md) — the page contract, the compiler, the store, the roles, jobs and the editor.
- [The shell](shell.md) — everything the site serves that is not a stored page: the document shape,
  layouts, composition, error responses and authentication.
- [Storage](storage.md) — the SQLite database: source storage, page sessions, the assistant's memory
  and import/export.
- [Metrics](metrics.md) — the instruments, the panels on `/admin/status` and the Prometheus scrape.
- [The assistant](assistant.md) — the `/assistant` subsystem, its tools, runs, memory and budgets.
- [Logging](logging.md) — what is logged, at which level and to what shape.
- [Testing](testing.md) — the suites, what each proves and the rules a test follows.
- [Glossary](glossary.md) — the site's words, each with what it is and is not.
- [Decisions](decisions.md) — every decision, named and cited from the code and these pages.
- [Backlog](backlog/README.md) — the rules an item follows and the items themselves.
- [Plans](plans/README.md) — the plans proposed or in progress.
- [Spikes](spikes/README.md) — the questions answered with evidence before anything was built.
- [Security reviews](security/README.md) — the reviews that were written rather than assumed.
- [Archived decisions](archive/README.md) — the frozen record from before decisions had names.
- [Deployment](../deploy/README.md) — the host, the quadlets, Caddy and the CI account.
- [Contributing](../CONTRIBUTING.md) — the branch, the worktree and the gate.

## The order

The backlog table is both the inventory and the order: a row's place is its priority, and the line
under it in italics is the reason it sits there. [The backlog index](backlog/README.md) says what each
column means, how the order is decided and how a step stops for the owner; `make backlog` holds this
table and the items to it.

| Slug | Title | Difficulty | Utility | Status | Blocked by |
| --- | --- | --- | --- | --- | --- |
| [`tool-surface-is-one-shell`](backlog/tool-surface-is-one-shell.md) | The assistant's tool surface is one shell, one question and one memory | protective-dragon 🔥🐉 | high | [planned](plans/assistant-shell-tool.md) | — |
| | *The shell already carries eleven of the twenty tools; making it POSIX, composable and the preferred surface leaves three tools and buys back the ceiling. `check` and the page's search have landed, so this is the next step.* | | | | |
| [`update-the-prompt`](backlog/update-the-prompt.md) | The assistant's prompt is a reference manual with the instructions at the bottom | determined-mountain-goat 💪🐐 | high | blocked | `tool-surface-is-one-shell` |
| | *51,800 characters sent as the head of every request, of which 3,000 restate tool descriptions that travel beside it and the operating rules start past 34,000. It is rewritten once the shell has landed, so it is built against three tools and a POSIX command set rather than optimising a surface about to disappear.* | | | | |
| [`outbound-headers-are-an-allow-list`](backlog/outbound-headers-are-an-allow-list.md) | Outbound headers are an allow-list, so a page cannot sign a request | caffeinated-squirrel ☕🐿️ | high | [planned](plans/outbound-headers.md) | — |
| | *The federation node's delivery cannot be signed because the host refuses `Signature`, `Digest`, `Date` and `Host`; the same change stops a redirect replaying the page's headers to another host.* | | | | |
| [`the-page-api-signs`](backlog/the-page-api-signs.md) | The page API admits no asymmetric key, so a page cannot verify a signature | eager-beaver ⚙️🦫 | high | open | — |
| | *The cheapest step and independent of the rest: four entries admit RSA, so an inbound request can be verified before anything else lands. Ed25519 waits for the framework.* | | | | |
| [`a-page-sees-its-request`](backlog/a-page-sees-its-request.md) | A page cannot see its request body, its raw target or a repeated header | determined-mountain-goat 💪🐐 | high | open | — |
| | *The body's bytes, the raw target and a repeated header are the three facts a signature is rebuilt from; the header names are already visible.* | | | | |
| [`the-site-is-one-type`](backlog/the-site-is-one-type.md) | The site's own facts are a capability each, and two of them are not reachable at all | protective-dragon 🔥🐉 | high | [planned](plans/site-info.md) | — |
| | *One home for the site's own facts — origin, editor, build, the image's documents and corpus, and the live bounds — so the surface stops growing a door per fact.* | | | | |
| [`a-page-can-own-a-job`](backlog/a-page-can-own-a-job.md) | A job needs a signed-in visitor, so an inbound request cannot start one | protective-dragon 🔥🐉 | high | blocked | `a-page-sees-its-request`, `the-site-is-one-type` |
| | *Work a page can start on behalf of nobody: a page-owned job with `Page` data only, its own queue and its own rate, which is what an inbox needs and what no visitor-scoped start can do.* | | | | |
| [`manual-is-a-reference-dump`](backlog/manual-is-a-reference-dump.md) | The author manual is a reference dump whose contents list has drifted | determined-mountain-goat 💪🐐 | high | open | — |
| | *The author's source of truth, ordered by API rather than by what an author wants to do, with an index that has already drifted; it is rewritten once the contract work in front of it has landed.* | | | | |
| [`storage-is-not-one-model`](backlog/storage-is-not-one-model.md) | Storage is not one model: a page reaches a patchwork of stores | protective-dragon 🔥🐉 | high | [planned](plans/storage-model.md) | — |
| | *The model every storage gap resolves into; it supersedes the two storage plans waiting for a yes, so it is read before them.* | | | | |
| [`a-page-cannot-keep-a-secret`](backlog/a-page-cannot-keep-a-secret.md) | A page cannot keep a secret | determined-mountain-goat 💪🐐 | high | blocked | `storage-is-not-one-model` |
| | *The secrets slice of the storage model, added at the owner's request before the release closes: a fifth shape on the Page subject, with no cross-page read, nothing catalogued, and plaintext at rest until [`secrets-are-readable-at-rest`](backlog/secrets-are-readable-at-rest.md) lands.* | | | | |
| [`no-account-store`](backlog/no-account-store.md) | A page has no persistent per-account store | protective-dragon 🔥🐉 | high | blocked | `storage-is-not-one-model` |
| | *The records slice of the storage model, which absorbs the item and its plan; it lands with [`storage-is-not-one-model`](backlog/storage-is-not-one-model.md) rather than beside it.* | | | | |
| [`conversation-is-not-a-page`](backlog/conversation-is-not-a-page.md) | The assistant's conversation is a stream and a set of host actions, not a page | protective-dragon 🔥🐉 | medium | blocked | `no-account-store` |
| | *The assistant's conversation as a page needs the account store beneath it, and that is [`no-account-store`](backlog/no-account-store.md).* | | | | |
| [`editor-is-a-textarea`](backlog/editor-is-a-textarea.md) | The editor is a plain textarea | determined-mountain-goat 💪🐐 | high | open | — |
| | *High use and high cost; the gutter, the diagnostics and the row state already cover the worst of it.* | | | | |
| [`ship-the-tested-image`](backlog/ship-the-tested-image.md) | A release does not ship the image the tests passed | persistent-salmon ⬆️🐟 | high | open | — |
| | *The browser suite drives a source build while the deploy ships an image built later; this makes the promoted image the one the browser drove, and the host gate already builds one twice without keeping it.* | | | | |
| [`message-does-not-survive-a-redirect`](backlog/message-does-not-survive-a-redirect.md) | A page cannot leave a message for a page it sends a visitor to | protective-dragon 🔥🐉 | high | blocked | `storage-is-not-one-model` |
| | *The messages slice of the storage model, which absorbs the item and its plan; it lands with [`storage-is-not-one-model`](backlog/storage-is-not-one-model.md) rather than beside it.* | | | | |
| [`secrets-are-readable-at-rest`](backlog/secrets-are-readable-at-rest.md) | A secret a page keeps is plaintext in the database and in every backup | determined-mountain-goat 💪🐐 | medium | blocked | `a-page-cannot-keep-a-secret` |
| | *A second pass over the storage model's secrets shape: the shape lands with [`storage-is-not-one-model`](backlog/storage-is-not-one-model.md), and the cipher, key and rotation land here.* | | | | |
| [`sec-page-runs-in-process`](backlog/sec-page-runs-in-process.md) | Page code has no process boundary | cornered-porcupine 📍🦔 | high | open | — |
| | *High utility and a redesign of how a page runs; the unit is now a long-lived worker per author over a socket rather than a process per render, and the author it keys on is not in the store yet.* | | | | |
| [`revision-hides-its-diagnostics`](backlog/revision-hides-its-diagnostics.md) | A page cannot read a revision's diagnostics | eager-beaver ⚙️🦫 | medium | open | — |
| | *The store keeps the last compile's answer and the catalog drops it, so a reloaded editor, a history view and a page explaining a broken library can say a revision failed but not what it said.* | | | | |
| [`ops-no-outage-notification`](backlog/ops-no-outage-notification.md) | No outbound notifications when the site breaks | eager-beaver ⚙️🦫 | medium | open | — |
| | *Useful, but one owner and a unit that restarts already notices most outages.* | | | | |
| [`import-cannot-reset-a-site`](backlog/import-cannot-reset-a-site.md) | Import only adds and overwrites, so an archive cannot reset a site | caffeinated-squirrel ☕🐿️ | medium | open | — |
| | *A destructive reset has to be designed carefully; the additive import is the safe default until it is.* | | | | |
| [`view-cannot-submit-a-form`](backlog/view-cannot-submit-a-form.md) | A run can look at the site but cannot submit one of its forms | determined-mountain-goat 💪🐐 | medium | open | — |
| | *Testing a page's own form is the one read the view cannot make; it needs a write-scoped credential before it can be a tool.* | | | | |
| [`terminal-cannot-reach-the-shell`](backlog/terminal-cannot-reach-the-shell.md) | A person cannot type into the shell the assistant uses | protective-dragon 🔥🐉 | medium | open | — |
| | *The kernel already takes an input and an output; a terminal is the other end, and the editor's `/vim.js` is the start of the `vi` it needs.* | | | | |
| [`declaration-does-not-name-its-close`](backlog/declaration-does-not-name-its-close.md) | A declaration block does not name its close | protective-dragon 🔥🐉 | medium | open | — |
| | *A language change in two releases that rewrites every declaration in the store; neither release has landed, so it waits on its own plan.* | | | | |
| [`old-response-spelling`](backlog/old-response-spelling.md) | The old response spelling is still on `IContext` | contented-capybara 😌🦫 | low | open | — |
| | *Waits for a release: the alias has to have been out long enough that nothing in the store reports `PAGE009` before it is deleted.* | | | | |
| [`archive-is-not-a-form`](backlog/archive-is-not-a-form.md) | `/admin/import` and `/admin/export` are shell code because an archive is not a form | caffeinated-squirrel ☕🐿️ | low | open | — |
| | *The two admin routes already work; making them pages buys consistency, not capability.* | | | | |
| [`no-browser-render`](backlog/no-browser-render.md) | A page cannot render a page in a browser | persistent-salmon ⬆️🐟 | low | open | — |
| | *A spike, a sidecar and a capability for a question the server's own answer often settles.* | | | | |
| [`nothing-checks-page-assets`](backlog/nothing-checks-page-assets.md) | Nothing checks a page's CSS, HTML or SVG | persistent-salmon ⬆️🐟 | low | open | — |
| | *Pays off once pages write their own CSS and SVG, and each checker is a parser we write, so it waits for that reason rather than a hunch.* | | | | |
| [`formatter-cannot-align`](backlog/formatter-cannot-align.md) | The formatter cannot align a run of declarations, and deletes one aligned by hand | caffeinated-squirrel ☕🐿️ | low | parked | — |
| | *The owner parked it on 2026-11-20: Roslyn deletes the padding and clang-format corrupts raw strings, so it waits for one of them to move rather than for work here.* | | | | |
