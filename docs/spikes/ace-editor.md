<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — Ace as the editor (IDEA-001)

| | |
| --- | --- |
| Question | Can Ace replace the textarea in `EditorEndpoint` without npm, and does it give vim keybindings, C# inside HTML, and a useful no-JavaScript fallback? |
| Answered | 2026-11-03 |
| Version measured | `ace-builds` 1.45.2, tag [`v1.45.2`](https://github.com/ajaxorg/ace-builds/tree/v1.45.2) |
| Verdict | Yes as a progressively-enhanced editor over the existing textarea; no as a literal drop-in — it needs a custom mode, a mount script and a CSP decision, and it does not format C# |
| Recorded | Nothing decided; adopting it needs a `DEC-` entry and a reconciliation with IDEA-006 |

## The answer

Ace is the only mature editor that satisfies the constraint that matters most here: **no npm and no
build step**. `ace-builds` ships a browser bundle checked in at `src-min-noconflict/`, its GitHub
tags match its npm versions, and the files it needs can be extracted from a tag tarball with `curl`
and `tar`. CodeMirror 6, which [IDEA-001](../jobs/IDEA-001.md) names as the smallest real option, is
pure ESM packages and a bundler: out, while this project has no npm. Monaco needs a build too, and
brings no vim keymap.

What Ace does *not* do, and the reason "drop-in" is the wrong word:

1. **There is no cshtml or asp mode.** `mode-html` highlights tags and the `<style>`/`<script>` inside
   them; `mode-csharp` highlights C#. Composing the two over `<% %>` is roughly forty lines of our
   own, modelled on `mode/jsp`, which does exactly this for Java scriptlets.
2. **It highlights C#, it does not format it.** There is no C# formatter in the browser and
   `ext-beautify` is js-beautify: it does not know `<% %>` and will mangle it. Real formatting is a
   Roslyn job on the server, which is a different piece of work.
3. **Its no-JavaScript story is the one we already have.** Ace is a `div` that only exists once
   scripts run, so the fallback is the existing `<textarea>`, kept as the form field and hidden only
   after the editor has mounted. That is not a compromise: it is the shape the current tests already
   pin, and it means a vendor file that 404s degrades to a working page rather than a blank editor.

The rest of this document is the evidence for those claims, the cost of adopting it, and what
adoption would collide with.

## What was measured

The vendored set is seven files, taken from the tag tarball and never from a package manager:

| Upstream path | Bytes | Why |
| --- | ---: | --- |
| `src-min-noconflict/ace.js` | 479,380 | the core, and its AMD loader |
| `src-min-noconflict/mode-html.js` | 63,609 | HTML mode; already bundles the XML, CSS and JavaScript highlight rules it embeds |
| `src-min-noconflict/mode-csharp.js` | 9,145 | C# mode; also registers `ace/mode/csharp_highlight_rules`, which the custom mode needs |
| `src-min-noconflict/keybinding-vim.js` | 119,286 | the vim keymap |
| `src-min-noconflict/ext-searchbox.js` | 14,164 | the find prompt that vim's `/` and `?` drive |
| `css/ace.css` | 31,691 | the editor's own stylesheet, needed when strict CSP is on |
| `css/theme/textmate.css` | 2,607 | a light theme that sits next to `site.css` without arguing with it |
| **total** | **719,882** | about 703 KiB, editor pages only |

That weight never reaches a public page: the script tags belong in `EditorEndpoint`, not in
`PageLayout.Header`, which every page renders. On the wire Caddy's `encode zstd gzip` already
compresses it; in `dotnet run` it does not, and neither serves it with a `Cache-Control` — so a
version in the path plus `immutable` via `StaticFileOptions.OnPrepareResponse` (or a move to
`MapStaticAssets`, which fingerprints and pre-compresses at build with no npm) is worth the twenty
minutes.

Provenance is verifiable without npm: the tag commit for `v1.45.2` is the `gitHead` npm recorded for
the same version, so the tarball is the shipped bytes. One trap for any update check —
`/releases/latest` on that repository has been stuck at `v1.5.0` since 2022 while tags and npm kept
going; use the tags API.

## Vim keybindings

`keybinding-vim.js` is not an approximation: its source header credits CodeMirror and points at
`codemirror.net/5/LICENSE`, because it is a fork of CodeMirror 5's `vim.js` — the implementation that
CodeMirror 5 users know and CodeMirror 6's `@replit/codemirror-vim` descends from. It brings motions,
operators, text objects, visual and visual-block, registers, macros, marks, a `:` ex-command subset
and folds. Its own documentation lists the gaps: number registers do not respect the delete-operator
special case, special marks are unimplemented — the usual long tail, not a toy.

The integration is one call, `editor.setKeyboardHandler("ace/keyboard/vim")`, plus our own
`editor.commands.addCommand` bindings for `:w` and `Ctrl-S` that submit the existing form. Nothing
about the save path changes: one antiforgery-checked `POST`, one `303`, one flash message — IDEA-001
step 5 holds.

## C# inside HTML

Ace has no asp mode, and the nearest neighbours (`mode/jsp`, `mode/razor`) highlight the wrong
language with the wrong delimiters. The composition is the JSP one, and this is the whole idea:

```js
ace.define("ace/mode/asp_highlight_rules",
  ["require", "exports", "module", "ace/lib/oop",
   "ace/mode/html_highlight_rules", "ace/mode/csharp_highlight_rules"],
  function (require, exports, module) {
    var oop = require("ace/lib/oop");
    var HtmlHighlightRules = require("ace/mode/html_highlight_rules").HtmlHighlightRules;
    var CSharpHighlightRules = require("ace/mode/csharp_highlight_rules").CSharpHighlightRules;

    var Rules = function () {
      HtmlHighlightRules.call(this);
      var open = [{ token: "meta.tag.template.asp", regex: "<%", push: "asp-start" }];
      var close = [{ token: "meta.tag.template.asp", regex: "%>", next: "pop" }];
      for (var key in this.$rules) this.$rules[key].unshift.apply(this.$rules[key], open);
      this.embedRules(CSharpHighlightRules, "asp-", close, ["start"]);
      this.normalizeRules();
    };
    oop.inherits(Rules, HtmlHighlightRules);
    exports.Rules = Rules;
  });
```

Two details that are easy to get wrong, both learned from reading `mode/jsp.js` closely rather than
copying it:

- JSP's `Mode` is a plain `TextMode` with `MatchingBraceOutdent` and **no `getNextLineIndent`
  override**, so scriptlet bodies get no C# block indentation. If auto-indent matters, inherit
  `HtmlMode` and delegate `getNextLineIndent` to `CSharpMode.prototype` when the state starts with
  `asp-`. That delegation is the difference between "highlights C#" and "edits like C#".
- `HtmlMode` defines `createWorker`, so the custom mode inherits it and Ace will look for
  `worker-html.js`. Either vendor the workers or call `session.setUseWorker(false)`; for pages of a
  few dozen lines, main-thread highlighting is not worth a worker.

The mode is **our** file, not a vendor file. Register it with
`ace.config.setModuleUrl("ace/mode/asp", "/editor/asp-mode.js")` so that re-extracting the upstream
tree can never collide with it.

## The no-JavaScript fallback

The editor is the only part of the site that writes, and the fallback is the page we already serve.
The rule is that the textarea is the form field and the editor is an enhancement bolted next to it:

```html
<div id="editor" hidden></div>
<form method="post" action="?">
  <input type="hidden" name="__RequestVerificationToken" …>
  <textarea name="content" id="content" rows="10" cols="50">…</textarea>
  <button type="submit">Save</button>
</form>
<script src="/vendor/ace/1.45.2/ace.js"></script>
<script type="module" src="/editor/mount.js"></script>
```

Mount order is load-bearing: `ace.js`, then `editor.setValue(textarea.value, -1)`, then unhide the
div, and only then hide the textarea; the form's `submit` handler copies `editor.getValue()` back
into the textarea. If any script fails, the textarea was never taken away.

`EditorEndpointTests` compares the form markup verbatim — for example
`<textarea name="content" rows="10" cols="50">…</textarea>` in
[EditorEndpointTests.cs](../../test/uk.osric.asp.Web.Tests/EditorEndpointTests.cs) — so keeping the
textarea exactly as it is has a second benefit: the existing unit tests keep proving the fallback.

The fallback is only *useful* if it is worth using, which is IDEA-001 step 2: a server-rendered line
gutter and each diagnostic anchored to its line. That layer is shared with the Ace path —
`session.setAnnotations` wants a zero-based line, and `PageDiagnostic.Position` is a one-based
`TemplatePosition`, so the mapping is `Line - 1` and the diagnostics payload is
`<script type="application/json">` (the default `System.Text.Json` encoder escapes `<`, so a
compiler message containing `</script>` cannot break out).

Caret across a save is the one IDEA-001 requirement Ace does not hand over, because a save is a full
`POST` → `303` → `GET`. `sessionStorage` keyed on the editor URL restores it with no server change and
no new session key.

## Strict CSP

Better than expected. Ace supports a strict policy: `ace.config.set("useStrictCSP", true)` before the
editor is created stops it injecting `<style>`, at the cost of linking the two stylesheets ourselves.
The upstream `demo/csp.html` is the canonical example and runs under `style-src 'self'`,
`script-src 'self' 'nonce-…'`, `worker-src 'self' blob:`.

So IDEA-001 step 4 can be stricter than it assumed:

```
script-src 'self'; style-src 'self'; worker-src 'none'; object-src 'none'; base-uri 'none'
```

with no nonce at all, because the only scripts are vendored files and one external module. `'none'`
for workers is only valid with `setUseWorker(false)`: Ace's `loadWorkerFromBlob` defaults to true and
would otherwise want `blob:`. The one claim here that rests on upstream's demo rather than on our own
test is that strict mode leaves the editor visually intact — check it in a browser, not in a unit
test, before the header goes in.

## Keeping it current without npm

Commit the extracted files; do not download at build time. `make deploy` runs `check`, and a build
that needs the network to produce the site is a build that can fail for reasons outside the
repository.

- `wwwroot/vendor/ace/1.45.2/` — the seven files above plus upstream `LICENSE`, `VERSION` and a
  `SHA256SUMS`.
- `make vendor-ace VERSION=x.y.z` — fetch
  `https://codeload.github.com/ajaxorg/ace-builds/tar.gz/refs/tags/v$(VERSION)`, check a pinned
  tarball hash, extract only the manifest, rewrite `SHA256SUMS`.
- `make vendor-ace-check` — re-hash the committed files against `SHA256SUMS`, so a hand-edited
  vendor file is a failure rather than a surprise. This is the gap [make audit](../../Makefile)
  leaves: `dotnet list package --vulnerable` cannot see JavaScript.
- `make vendor-ace-outdated` — advisory, outside `check`: list the newest tag and say whether it
  differs from `VERSION`. Use the tags API, not `releases/latest`.

The version is recorded in exactly two places — the `VERSION` file beside the assets and the URL
derived from it — so an upgrade is one command, a diff to read, and no lock file whose integrity
nobody checks.

## What it collides with

- **IDEA-006** makes `site.css` a page and says that if `wwwroot` is left empty,
  `app.UseStaticFiles()` goes with it. A vendored editor makes `wwwroot` permanently non-empty.
  Either the vendor tree lives under `wwwroot` and
  [Program.cs](../../src/uk.osric.asp.Web/Program.cs) keeps its static-file middleware, or it gets a
  static root of its own; decide before both jobs land in either order.
- **Licensing.** Ace is BSD-3-Clause, `Copyright (c) 2010, Ajax.org B.V.`, and the built files carry
  no SPDX header, so `LICENSES/BSD-3-Clause.txt` and a `REUSE.toml` annotation for the vendor tree
  are both required or `make license-check` fails. This is the first third-party runtime dependency
  in the tree, so it is a decision, not a commit.
- **Testing.** [testing.md](../testing.md) currently says "no browser needed". The unit-testable
  parts are the diagnostics payload and the fact that the editor page still contains the textarea and
  the expected script URLs. Vim keys, caret restoration and the submit sync are browser tests, and
  there is no browser harness — that is a decision too.

## Alternatives

| Option | Build step | Vim | `<% %>` + C# | No-JS | Verdict |
| --- | --- | --- | --- | --- | --- |
| **Ace 1.45.2** | none, prebuilt | built in (CM5 lineage) | ~40 lines of our own | textarea enhancement | **Recommended** |
| CodeMirror 5 | none, prebuilt | built in (same lineage) | `htmlembedded` + `scriptTypes` maps `<% %>` to `csharp` by configuration | textarea enhancement | Cheaper mode story, frozen upstream |
| CodeMirror 6 | bundler + npm | via `@replit/codemirror-vim` | custom | textarea enhancement | Out under no-npm |
| Monaco | bundler or AMD | not in the box | no C# without a language server | none | Out |
| textarea + gutter | none | none | highlighting impossible | it *is* the fallback | Not enough once vim is a requirement |

The honest counter-proposal is CodeMirror 5: it would save the custom mode entirely. The trade is an
upstream that stopped shipping, for a bundle that is still prebuilt and still MIT — defensible for an
owner-only tool, and worth revisiting if the mode turns out to be the part that hurts.

## Recommended shape

1. Server-rendered line gutter and diagnostics anchored to lines, textarea unchanged. Pure, testable,
   and useful with or without scripts — do this first whatever else happens.
2. Vendor Ace 1.45.2 with the `make vendor-ace` flow, the BSD licence text and the REUSE annotation.
3. `asp-mode.js` and `mount.js` as our own files outside the vendor tree: highlight rules,
   annotation mapping, submit sync, `:w`, caret restoration.
4. The `Content-Security-Policy` header with `useStrictCSP` and `setUseWorker(false)`, verified in a
   browser.
5. Decisions recorded: the dependency, the CSP, and the `wwwroot` question IDEA-006 raises.

## Sources

- <https://github.com/ajaxorg/ace-builds/tree/v1.45.2> — the tag the measurements come from.
- <https://github.com/ajaxorg/ace-builds/blob/master/demo/csp.html> — strict-CSP recipe.
- <https://github.com/ajaxorg/ace/blob/master/src/config.js> — `useStrictCSP`.
- <https://github.com/ajaxorg/ace/blob/master/src/mode/jsp_highlight_rules.js> — the scriptlet pattern.
- <https://github.com/ajaxorg/ace/blob/master/src/keyboard/vim.js> — the vim keymap and its gaps.
- <https://github.com/ajaxorg/ace/issues/5452> — "link the CSS yourself" confirmation.
