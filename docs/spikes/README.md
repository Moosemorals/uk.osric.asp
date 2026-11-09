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
| [deepseek-token-counting](deepseek-token-counting.md) | Should the assistant budget the context in tokens rather than characters, and what is the cheapest exact way for a C# process to count DeepSeek tokens? | Exact counting is cheap: `Tokenizers.DotNet` reads `tokenizer.json` unchanged, a managed counter is ~300 lines, and the file reduces to a 1 MB merge table. `Microsoft.ML.Tokenizers` cannot do it, and the character budget is wrong by up to 4× |
| [aot-page-binary-spike](aot-page-binary-spike.md) | Is a NativeAOT binary per page a cheaper way to run a page than rendering in-process? | Render half passes; publishing fails 5.6–7.5× and the per-render sandbox is blocked by the deployed hardening (re-checked on the CAX21: the box is no longer the blocker) |
| [ace-editor](ace-editor.md) | Can Ace replace the editor's textarea, given no npm, vim keybindings, C# inside HTML and a useful no-JavaScript fallback? | Yes as a progressively-enhanced editor; needs a custom mode and a CSP decision, and does not format C# |
