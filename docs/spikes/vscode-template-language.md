<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — Teaching VS Code the template language

| | |
| --- | --- |
| Question | What does it take for VS Code to read a page template — HTML outside the blocks, C# inside them, and errors against the author's own line — without npm, a bundler or a third-party dependency? |
| Answered | 2026-11-13 |
| Version measured | VS Code 1.146.0 with its built-in `html` and `csharp` grammars (extension version 10.0.0); `vscode-textmate` 9.3.2 and `vscode-oniguruma` 2.0.1 used only as a test harness |
| Verdict | Highlighting is a small **declarative** extension — one manifest and three data files, no `main`, no JavaScript, no npm — but only if C# is embedded as a *flat fragment*; including `source.cs` wholesale breaks on the language's central idiom. Real error positions need no language server at all: the compiler already answers in template space, and a problem matcher makes that output clickable |
| Recorded | The declarative half landed: `tools/vscode-asp` and `make check-pages`. The live half is [IDEA-029](../jobs/IDEA-029.md) |

## The answer

Three layers, and only the first is needed to review a page:

| Layer | What it buys | What it costs | Node? |
| --- | --- | --- | --- |
| TextMate grammar | HTML and C# highlighting in the editor | one manifest and three JSON files, ~6 KB of grammar | no |
| `check-pages` task | every compiler error as a clickable Problems entry | ~20 lines of C# and a problem matcher | no |
| Language server | squiggles while typing, completion, hover | a C# server plus a ~200-line plain-JS client, and a plan | a spawned process, no npm |

Everything below is what was actually run, not what the grammars look like from outside.

## The language, as the tokenizer defines it

The authority is [`TemplateTokenizer`](../../src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs):
text, `<% %>` code, `<%! %>` declarations, `<%= %>` escaped writes and `<%== %>` raw writes, where
`%>` closes any of them. Two consequences a grammar has to copy exactly:

- `<%` is only an opener when it starts a block; a bare `%>` in HTML is text, and `<%=`/`<%==`/`<%!`
  are the opener only when the extra character is adjacent.
- `%>` ends the block wherever it appears, including inside a C# string or a `//` comment. A page
  that writes `<% var s = "%>"; %>` has already ended its block at the first `%>`; the compiler sees
  the same thing the tokenizer saw.

## TextMate cannot embed `source.cs` wholesale

The shape that suggests itself — a grammar for `text.html.asp` that includes `text.html.basic` and
`source.cs` inside the blocks, with an `embeddedLanguages` mapping — does not survive the first
`if`:

```text
<% if (x) { %>tail
```

tokenizes as

```text
"<%"   punctuation.section.embedded.begin.asp
"if"   meta.embedded.block.asp > keyword.control.conditional.if.cs
"{"    meta.embedded.block.asp > punctuation.curlybrace.open.cs
" %>"  meta.embedded.block.asp > ...        <- never the end delimiter
"tail" meta.embedded.block.asp > variable.other.readwrite.cs
```

The reason is in the engine rather than the grammar: a begin/end rule only ends when *its own* end
pattern matches, and a nested open rule hides the enclosing rule's end. `source.cs` opens a `block`
on `{` and closes it on `}`; a code block that opens a brace and closes it in a later block — the
idiom every page uses — leaves that rule open for ever, so `%>` is never seen and the rest of the
file is coloured as C#. Patching `block` alone does not help: `if-statement` itself is a begin/end
rule whose `end` is `(?<=})|(?=;)`, so with no `}` before `%>` it is the `if` that never closes.

`embeddedLanguages` is also not what makes code coloured. The embedded grammar does that; the
mapping only tells VS Code which language is at a position, so features that ask
`getLanguageIdAtPosition` see `csharp` inside a block. It is worth keeping, but it is not the
highlighting.

## The fix: a flat C# fragment

The grammar that works gives every C# rule a `match` and no rule a `begin`/`end`. `%>` is then
reachable from every position, and the outer block always wins.

```json
"asp-code": {
  "begin": "(<%)",
  "beginCaptures": { "1": { "name": "punctuation.section.embedded.begin.asp" } },
  "end": "(%>)",
  "endCaptures": { "1": { "name": "punctuation.section.embedded.end.asp" } },
  "contentName": "meta.embedded.block.asp",
  "patterns": [{ "include": "#csharp" }]
}
```

Most of C# is still the real thing: scope-qualified includes pull individual leaf rules out of the
built-in grammar, and the spike verified that VS Code's engine resolves them
(`source.cs#numeric-literal`, `source.cs#string-literal`, `source.cs#type-builtin`,
`source.cs#expression-operators`, the punctuation rules and so on). What has to be written by hand
is small:

- **comments**, because `source.cs#comment` is a begin/end rule and `<% // note %>` is a valid block
  whose `%>` is inside the comment; the line rule must stop at the delimiter, `//.*?(?=%>|$)`.
- **identifiers**, so a call is `entity.name.function.cs` and a member is
  `variable.other.object.property.cs`; and
- **keywords**, the one real duplication: the C# grammar scatters them through its expression and
  statement rules, so the fragment carries its own list (about 15 rules) with C#-compatible names.

The result is 15 repository entries and 38 match rules in 5.9 KB minified — the prototype in the
session workspace is the readable form. Every rule reuses the standard `.cs` scope names, so every
colour theme already has colours for it.

Measured against the eight seeded pages, in the harness: every `<%` opener highlighted, no C# scope
outside a block, and 1–45 ms to tokenize a whole file (462 lines is the largest). The failing cases
are the ones that are already page bugs: an unterminated string or an unclosed block comment is a
begin/end rule in C# too, and it will keep the rest of the block coloured until it is closed.

## Attribute values need an injection

HTML's string rule swallows `<%` inside an attribute, so `<a href="/p?x=<%= v %>">` is plain HTML
with no C#. A second grammar fixes it:

```json
{
  "scopeName": "asp.injection.html",
  "injectionSelector": "L:string.quoted.double.html - meta.embedded.block.asp, L:string.quoted.single.html - meta.embedded.block.asp",
  "patterns": [
    { "include": "text.html.asp#asp-raw-write" },
    { "include": "text.html.asp#asp-write" },
    { "include": "text.html.asp#asp-declaration" },
    { "include": "text.html.asp#asp-code" }
  ]
}
```

`injectTo: ["text.html.asp"]` keeps it out of every other HTML file in the editor, and the
scope-qualified includes mean the block rules are written once. This is 18 lines, and the spike
confirmed both quote styles.

## Extensionless seeds

The seeded pages are keyed by file name — [`PageAssetSeeder`](../../src/uk.osric.asp.Web/Pages/PageAssetSeeder.cs)
uses `Path.GetFileName` — so `about`, `_layout` and the rest cannot grow an `.asp` extension
without changing page keys and routes. Association has to be by path.

VS Code's own rules (from `languagesAssociations.ts`) are: a workspace setting beats any
contributed association; an exact filename beats a glob; among globs the **longest pattern wins**;
a pattern containing `/` is matched against the full path, otherwise against the file name; and a
glob beats an extension. That last point is why `filenamePatterns` in the extension is the wrong
tool — it would claim `site.css` and `rocket.svg` too.

So the extension contributes the language with no associations, and the workspace carries:

```json
"files.associations": {
  "**/Pages/Seeds/*": "asp",
  "**/Pages/Seeds/*.css": "css",
  "**/Pages/Seeds/*.js": "javascript",
  "**/Pages/Seeds/*.svg": "xml"
}
```

The four longer patterns keep the assets out of the template language. Two housekeeping effects:
[`.gitignore`](../../.gitignore) currently ignores all of `.vscode/` but three files, so
`.vscode/settings.json` needs an exception, and [`REUSE.toml`](../../REUSE.toml) needs an entry for
it and for the extension's JSON (JSON cannot carry the SPDX tags).

The extension itself is installed once with `code --install-extension <folder>`; it is not a
marketplace extension and is not recommended from `.vscode/extensions.json` (that list only takes
marketplace ids). Four declarative files are enough: the built-in `html` extension is the proof that
an extension with no `main` loads.

## Diagnostics already know their place

This is the part that needs no new machinery. [`TemplateSourceGenerator`](../../src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs)
emits `#line` directives that map every page-owned span back to the template, and
[`PageCompiler.MapDiagnostics`](../../src/uk.osric.asp.Runtime/PageCompiler.cs) already turns that
into `PageDiagnostic` values positioned with a `TemplatePosition`. The spike ran the real compiler
on real seeds:

| What | Measured |
| --- | --- |
| template → generated source | 0.02–0.05 ms |
| cold compile (loads the reference set) | 1.5 s, once per process |
| warm compile, page with code | 30–70 ms |
| syntax-only parse | 0.2–0.7 ms — and **zero** diagnostics for the broken template, because the generated source is syntactically valid and the errors are semantic |

A typo in a seed answers, through the compiler, with the author's own coordinates:

```text
about-broken:8:51: error CS1061: 'IBuildInfo' does not contain a definition for 'Comit' ...
```

Template errors (`TEMPLATE001` unterminated block, `TEMPLATE002` empty write) and policy errors
(`PAGE002`) are positioned too. The smallest consumer — [`tools/check-pages`](../../tools/check-pages),
about twenty lines — generates, compiles and prints `file:line:col: severity message`, and a task
with a small `problemMatcher` turns that into Problems entries and red squiggles in the editor, with
no language server and no JavaScript:

```json
"problemMatcher": {
  "owner": "asp",
  "fileLocation": ["relative", "${workspaceFolder}"],
  "pattern": {
    "regexp": "^(.+?):(\\d+):(\\d+): (error|warning) (.*)$",
    "file": 1, "line": 2, "column": 3, "severity": 4, "message": 5
  }
}
```

`PageDiagnostic` keeps only a start `TemplatePosition`, not an extent, so a squiggle is one
character wide rather than the offending token. That is invisible where diagnostics are printed as
text today; giving them a length is a small `Core` change (`span.EndLinePosition` is already in the
mapped `FileLinePositionSpan` and is currently discarded) and is worth doing only if something
draws ranges.

## If you want a language server

A server is the only way to get diagnostics while typing and completion, and it can be a .NET
console app — LSP is JSON-RPC with `Content-Length` framing over stdio, and the compiler side is the
two public calls above. What the spike found missing or expensive:

- **Server boilerplate**: lifecycle, `textDocument/didOpen|didChange|didClose`, debounce,
  `publishDiagnostics`. Roughly 250–350 lines plus tests; the compile is 30–70 ms warm, so a
  debounce is required, but this is comfortably interactive.
- **Completion and hover need the other direction of the map.** `#line` maps generated source to the
  template; asking Roslyn for completions needs a *template* offset. [`GeneratedSource`](../../src/uk.osric.asp.Core/Compilation/GeneratedSource.cs)
  is only a string today, so either `Core` grows a segment map (~40 lines and tests) or the server
  inserts a sentinel at the caret, regenerates and finds it (~30 lines, and a hack). Roslyn's
  completion service also brings the Workspaces packages, which
  [`Directory.Packages.props`](../../Directory.Packages.props) already pins for EF Design but no
  product assembly references.
- **Semantic tokens are optional and map cleanly.** If the grammar were not wanted, classified
  spans could be mapped back with `GetMappedLineSpan` (generated → template) with no reverse map.
  It would need `Microsoft.CodeAnalysis.CSharp.Workspaces` for `Classifier`, or a hand-written walk
  of the syntax tree.
- **The client is the only JavaScript**, and it can be plain JS: spawn the server, frame JSON-RPC,
  forward document changes, put `publishDiagnostics` into a `DiagnosticCollection`. ~150–250 lines,
  no bundler and no npm. `vscode-languageclient` would remove the last hundred lines and add npm;
  with no npm, hand-rolling wins.

That is a plan-sized job, not a sitting, and it is independent of the two cheap layers.

## What it does not do

- **No formatting.** Same limit the [Ace spike](ace-editor.md) found: TextMate colours, it does not
  format. That is a Roslyn job on the server.
- **No understanding.** A plausible-but-wrong identifier still highlights; only the compiler knows.
- **The keyword list is a second copy of C#.** A new keyword or construct is a one-line change in
  the fragment, and nothing fails the build if it is forgotten — only the colour is missing.
- **A stray quote or comment drifts.** A `%>` inside an unclosed string is already a page bug; the
  grammar will colour the rest of the block as C# until it is closed, which is the same thing the
  tokenizer would have done.
- **Not the site's own editor.** IDEA-001's editor and its Ace mode are separate; this is for
  reading seeds in VS Code.

## What landed

1. [`tools/vscode-asp`](../../tools/vscode-asp) — the manifest, the two grammars and the language
   configuration, with the association in [`.vscode/settings.json`](../../.vscode/settings.json).
   No JavaScript. Install with `code --install-extension tools/vscode-asp`.
2. [`tools/check-pages`](../../tools/check-pages) — the compiler over the seeds, and the `check pages`
   task in [`.vscode/tasks.json`](../../.vscode/tasks.json) with the problem matcher. It is also in
   `make check`, so a broken seed fails the gate rather than the first visitor.
3. The live half — diagnostics while typing, completion, hover — is [IDEA-029](../jobs/IDEA-029.md):
   a C# server, a plain-JS client and, if completion is in scope, a reverse map in `Core`.

The tokenize harness that produced these numbers is test tooling, not a dependency: it is
`vscode-textmate` and `vscode-oniguruma` run on VS Code's own Electron, and the grammar is data that
needs no build.

## Sources

- <https://github.com/microsoft/vscode/blob/main/src/vs/editor/common/services/languagesAssociations.ts>
  — how `files.associations` resolves a path, and why a glob beats an extension.
- <https://github.com/microsoft/vscode/blob/main/src/vs/workbench/services/textMate/common/TMGrammarFactory.ts>
  — how `embeddedLanguages` and injection grammars are combined.
- <https://macromates.com/manual/en/language_grammars> — begin/end rules and injection selectors.
- `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs` — the language the grammar has to match.
- `src/uk.osric.asp.Runtime/PageCompiler.cs` — the mapping that makes error positions real.
