<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# uk.osric.asp template for VS Code

A language, two grammars, a language configuration and a small Language Server Protocol client. There is
nothing to install from npm and no build step: the client is one plain-JavaScript file and the server
is a .NET console app in `tools/asp-language-server`.

```bash
make vscode-extension   # link this folder into ~/.vscode/extensions, then reload the window
```

`code --install-extension` takes an extension id or a `.vsix`, not a folder, and there is no build
step here to make one.

The seeded pages have no extension, because a seed is served under its file name. The workspace
associates them by path in `.vscode/settings.json`; the four asset extensions are listed there too,
because a `files.associations` glob beats an extension association.

## The icon

`icon.png` is the site's `Pages/Seeds/rocket.svg` rendered at 128x128, because VS Code's manifest
takes a PNG and every extension that ships one does: the built-in `html` and `csharp` extensions and
every extension installed here use `.png`, and the manifest reference describes the field by pixel
size only. `make vscode-extension` re-renders it from the seed with `rsvg-convert` when that is
available, so the two cannot drift; the PNG is committed so a machine without it still gets an icon.

## Why the C# is a fragment

`source.cs` cannot be included wholesale inside a block. Its `block` rule opens on `{` and closes on
`}`, and a page opens a brace in one block and closes it in another — `<% if (x) { %> … <% } %>` — so
that rule never closes, `%>` is never reached, and the rest of the file is coloured as C#. The
grammar here gives every C# rule a `match` and no rule a `begin`/`end`, so `%>` always wins; the leaf
rules are still the built-in grammar's, reached through `source.cs#…` includes. Comments, keywords
and identifiers are ours, because `<% // note %>` must stop at the delimiter and the C# grammar's
keyword rules are spread across its statement and expression rules.

`<%# note %>` is the template's own comment, and it is a block of its own rather than C#: the rule
takes the whole construct as `comment.block.asp`. `asp-injection.html` includes it too, so a note
inside an HTML attribute is coloured as the comment it is rather than as markup.

`asp-injection.html` applies the same block rules inside HTML attribute strings, so
`href="<%= x %>"` highlights too. `injectTo` keeps it out of every other HTML file.

The fragment reuses VS Code's built-in `source.cs` grammar for strings, numbers, built-in types and
the expression operators. If that grammar is unavailable — the built-in `csharp` extension disabled —
the delimiters, keywords, identifiers and punctuation still highlight; only the reused leaves lose
their colour.

## Squiggles while typing

The extension is no longer only data: `extension.js` is a Language Server Protocol client for
`tools/asp-language-server`, and it puts the compiler's diagnostics on the document as it changes. The
extension host loads an extension's `main` as CommonJS, so this is the one script in the repository
that cannot be an ES module — `require` is the only way to reach the editor's API.

```bash
make build              # puts the server in artifacts/bin/asp-language-server
make vscode-extension   # link this folder into ~/.vscode/extensions, then reload the window
```

Three settings name what is spawned, which is how a machine with the SDK somewhere else works:

| Setting | Default |
| --- | --- |
| `aspLanguageServer.command` | `dotnet` |
| `aspLanguageServer.path` | `${workspaceFolder}/artifacts/bin/asp-language-server/debug/asp-language-server.dll` |
| `aspLanguageServer.seeds` | `${workspaceFolder}/src/uk.osric.asp.Web/Pages/Seeds` |

The server compiles against the seed files because an editor has no page store, so a page that names
`/_lib/document` is checked against the file in the repository, and an error inside that library is
underlined in the library rather than in the page that reached it.

## Checking a page

`make check-pages` compiles every seed through the real compiler and prints
`file:line:col: severity message`, which the `check pages` task in `.vscode/tasks.json` turns into
Problems entries. See `tools/check-pages`.
