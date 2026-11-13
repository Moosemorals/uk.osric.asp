<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# uk.osric.asp template for VS Code

A declarative extension: a language, two grammars and a language configuration. There is no `main`,
no JavaScript and nothing to install from npm — the built-in `html` extension has the same shape.

```bash
make vscode-extension   # link this folder into ~/.vscode/extensions, then reload the window
```

`code --install-extension` takes an extension id or a `.vsix`, not a folder, and there is no build
step here to make one.

The seeded pages have no extension, because a seed is served under its file name. The workspace
associates them by path in `.vscode/settings.json`; the four asset extensions are listed there too,
because a `files.associations` glob beats an extension association.

## Why the C# is a fragment

`source.cs` cannot be included wholesale inside a block. Its `block` rule opens on `{` and closes on
`}`, and a page opens a brace in one block and closes it in another — `<% if (x) { %> … <% } %>` — so
that rule never closes, `%>` is never reached, and the rest of the file is coloured as C#. The
grammar here gives every C# rule a `match` and no rule a `begin`/`end`, so `%>` always wins; the leaf
rules are still the built-in grammar's, reached through `source.cs#…` includes. Comments, keywords
and identifiers are ours, because `<% // note %>` must stop at the delimiter and the C# grammar's
keyword rules are spread across its statement and expression rules.

`asp-injection.html` applies the same block rules inside HTML attribute strings, so
`href="<%= x %>"` highlights too. `injectTo` keeps it out of every other HTML file.

The fragment reuses VS Code's built-in `source.cs` grammar for strings, numbers, built-in types and
the expression operators. If that grammar is unavailable — the built-in `csharp` extension disabled —
the delimiters, keywords, identifiers and punctuation still highlight; only the reused leaves lose
their colour.

## Checking a page

`make check-pages` compiles every seed through the real compiler and prints
`file:line:col: severity message`, which the `check pages` task in `.vscode/tasks.json` turns into
Problems entries. See `tools/check-pages`.
