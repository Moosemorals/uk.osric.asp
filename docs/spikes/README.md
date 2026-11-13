<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spikes

A spike answers one question with evidence instead of argument: read the upstream source, measure the
bytes, try the thing, then write down what was found and what it rules out. A spike is neither a plan
nor a decision. It ends with a recommendation and, at most, the decision that adopting it would need.

Unlike a [job](../jobs/README.md), a spike is not work waiting to be done; unlike a
[plan](../plans/), it does not describe how to build something. It exists so the next person does not
have to repeat the reading.

| Spike | Question | Verdict |
| --- | --- | --- |
| [assistant-memory](assistant-memory.md) | Should the page assistant remember anything between conversations, and what is the smallest design that remembers well on this site? | Yes, in SQLite: one owner-scoped table and its edit history, an FTS5 index with porter stemming, BM25 recall mixed with pinned and already-used memories into a 4,000-character budget, and writes only through explicit tools. No embeddings, no extraction pass, no graph |
| [deepseek-token-counting](deepseek-token-counting.md) | Should the assistant budget the context in tokens rather than characters, and what is the cheapest exact way for a C# process to count DeepSeek tokens? | Exact counting is cheap: `Tokenizers.DotNet` reads `tokenizer.json` unchanged, a managed counter is ~300 lines, and the file reduces to a 1 MB merge table. `Microsoft.ML.Tokenizers` cannot do it, and the character budget is wrong by up to 4× |
| [aot-page-binary-spike](aot-page-binary-spike.md) | Is a NativeAOT binary per page a cheaper way to run a page than rendering in-process? | Render half passes; publishing fails 5.6–7.5× and the per-render sandbox is blocked by the deployed hardening (re-checked on the CAX21: the box is no longer the blocker) |
| [trimmed-host](trimmed-host.md) | Would publishing trimmed — short of NativeAOT — cut the image or the memory enough to be worth it? | No. It saves 92 MB of RSS but doubles the released layer, needs EF, the migrations, corelib and reflection JSON kept, and still leaves the page compiler compiling against a reference set that shrinks with the trim |
| [ace-editor](ace-editor.md) | Can Ace replace the editor's textarea, given no npm, vim keybindings, C# inside HTML and a useful no-JavaScript fallback? | Yes as a progressively-enhanced editor; needs a custom mode and a CSP decision, and does not format C# |
| [vscode-template-language](vscode-template-language.md) | What does it take for VS Code to highlight a page template and show errors against the author's line, without npm? | Highlighting is a declarative extension with no JavaScript, but C# must be embedded as a flat fragment — `source.cs` wholesale leaves `%>` unreachable after `<% if (x) { %>`. Real error positions need no language server: the compiler already maps them and a problem matcher makes them clickable |
