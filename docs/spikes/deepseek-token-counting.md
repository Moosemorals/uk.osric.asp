<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Spike — counting DeepSeek tokens in C#

| | |
| --- | --- |
| Question | Should the assistant budget the context in tokens rather than characters, and what is the cheapest exact way for a C# process to count DeepSeek tokens? |
| Answered | 2026-11-09 |
| Version measured | DeepSeek `tokenizer.json` (128,000-token byte-level BPE, 127,741 merges, 1,283 added tokens, format `1.0`), `Microsoft.ML.Tokenizers` 2.0.1, `Tokenizers.DotNet` 1.4.1, HuggingFace `tokenizers` 0.21.4 as the reference |
| Verdict | Exact counting is cheap and needs no conversion: `Tokenizers.DotNet` reads `tokenizer.json` unchanged, a managed counter is about 300 lines, and the file reduces to a 1 MB merge table. `Microsoft.ML.Tokenizers` cannot do it. The character budget is wrong by up to 4× either way |
| Recorded | Nothing decided. A token budget changes `ContextTrimmer`, `BudgetSnapshot`, `AssistantOptions`, a paragraph of [assistant](../assistant.md) and a live test, so it needs a `DEC-` entry |

## The answer

Nothing here is hard to implement; the mistake is the unit. `ContextTrimmer` fits whole turns into
`MaxContextChars` (120,000) and the page draws that figure as characters, but the provider's limit
is tokens, and characters per token in this tokenizer range from 4.5 (C#, Markdown) down to 1.1
(emoji). The same 120,000-character budget is about 26,800 tokens of C# and about 105,000 tokens of
emoji: the four-fold spread in
[why characters are the wrong unit](#why-characters-are-the-wrong-unit) is the whole argument.

Counting exactly costs one file read and a merge table:

1. **If a native asset is acceptable**, reference `Tokenizers.DotNet` (and a runtime package per RID)
   and call `new Tokenizer("tokenizer.json")`. It loads in ~120 ms, counts 31 KB of code in 6 ms,
   and was identical to HuggingFace on all 567 test inputs. Nothing is converted, nothing is
   reimplemented.
2. **If it is not** — the deployed image is Alpine/musl, and the native runtime packages are built
   per RID, so that has to be tried before it is relied on — a managed counter is about 300 lines
   and was equally exact. It reads `tokenizer.json` once (~140 ms) or a 1 MB merge table derived
   from it (~20 ms).
3. **`Microsoft.ML.Tokenizers` should not be used for this vocabulary.** It has no `tokenizer.json`
   loader, and when fed the vocab, merges and regex chain by hand it agreed on 196 of 361
   hand-written cases, agreed on 6 of 206 fuzz cases, and threw `ArgumentOutOfRangeException` on 65
   of them. See [what was rejected](#what-was-rejected).

## What the file is

`tokenizer.json` is a HuggingFace `tokenizers` file, not a flat list of recognised tokens. The
vocabulary is a byte-level BPE alphabet: `Ġ` is a space, `Ċ` a newline, and every token is a merge
of those.

| Section | Contents |
| --- | --- |
| `model.vocab` | 128,000 tokens, ids 0–127,999 |
| `model.merges` | 127,741 rank-ordered pairs; the lowest rank wins |
| `added_tokens` | 1,283 whole tokens: the three specials, `<｜User｜>`, `<think>`, the tool markers, 1,217 placeholders |
| `normalizer` | empty |
| `pre_tokenizer` | `Split` on `\p{N}{1,3}`, then `Split` on CJK runs, then `Split` on the GPT-4 pattern, then `ByteLevel` |
| `post_processor`, `decoder` | `ByteLevel`; neither affects a count |

Counting is therefore: match added tokens, apply the three splits to what is left, byte-encode each
piece, merge by rank. A greedy longest-match over `model.vocab` — the obvious reading of "a list of
tokens the API recognises" — gives a different and usually higher number, because it may cross the
boundaries the pre-tokenizer enforces. `"  leading"` is `Ġ` + `Ġleading`, not `ĠĠ` + `leading`.

## Why characters are the wrong unit

Measured with the reference tokenizer on this repo and on synthetic text of the same kind:

| Content | UTF-16 chars | Tokens | Chars/token | 120,000 chars is |
| --- | ---: | ---: | ---: | ---: |
| C# source, 15 files from `src/` | 33,708 | 7,525 | 4.48 | ~26,800 tokens |
| `docs/*.md` | 187,526 | 45,913 | 4.08 | ~29,400 tokens |
| English prose (`README.md`) | 8,817 | 2,316 | 3.81 | ~31,500 tokens |
| Chinese | 17,400 | 8,400 | 2.07 | ~58,000 tokens |
| JSON (tool definitions, vocabulary-shaped) | 200,000 | 124,552 | 1.61 | ~74,500 tokens |
| base64 | 77,400 | 50,400 | 1.54 | ~78,000 tokens |
| Japanese | 14,800 | 10,400 | 1.42 | ~84,500 tokens |
| Emoji (`🙂👍🏽🎉`, 2,000 groups) | 16,000 | 14,000 | 1.14 | ~105,000 tokens |

Two things in that table matter beyond the spread. Tool definitions are sent as JSON on every
request — the second-worst case above — and they are not in `ChatMessage` at all:
`OpenAiChatCompletionClient` serialises `request.Tools` beside the messages. And `MessageLength`
counts `Content + ToolCallsJson` in UTF-16 `char`s, which is not a unit any tokenizer has: `🙂`
is 2 chars and 2 tokens, `👍🏽` is 4 chars and 3 tokens, and the four-person family emoji is 11
and 11.

## Method

A throwaway workspace, since deleted, with three candidates measured against one reference.

The reference is HuggingFace `tokenizers` 0.21.4 — the same library the `tokenizer.json` came from.
This box has no `pip`, so its `cp39-abi3-manylinux` wheel was unpacked into a directory and imported
with `PYTHONPATH`; `Tokenizer.from_file(...)` then encoded every input with
`add_special_tokens=False`.

The corpus is 567 inputs:

- 361 hand-written cases: runs of spaces, tabs and newlines, CRLF, digits in groups of one to ten,
  URLs, punctuation runs, code, Markdown, CJK, Korean, Greek, Cyrillic, Arabic, combining marks,
  zero-width space, ideographic space, emoji, astral letters, chat markers (`<｜User｜>`,
  `<｜tool▁call▁begin｜>`, `</think>`), and runs of 300 identical characters
- 206 generated cases: 200 random strings of 200–2,000 characters over ASCII, code punctuation,
  CJK, Korean, emoji, astral letters and zero-width characters, plus six real `.cs` files from
  `src/` (up to 20,000 characters each)

Each candidate was compared on token ids, not counts, so a shared mistake in the merge order would
have shown up:

| Approach | Exact | Load | 31 KB of code | 107 KB of code |
| --- | --- | ---: | ---: | ---: |
| `Tokenizers.DotNet` 1.4.1, `tokenizer.json` unchanged | 567/567 | 115–122 ms | 6 ms | 7 ms |
| Managed counter, from `tokenizer.json` | 567/567 | ~140 ms | ~70 ms | ~200 ms |
| Managed counter, from the 1 MB merge table | 567/567 | ~20 ms | ~70 ms | ~200 ms |
| `Microsoft.ML.Tokenizers` 2.0.1, wired by hand | 196/361, 6/206 | ~380 ms | 17 ms | 17 ms |

The managed counter is a `Rune` scanner for the three patterns, the GPT-2 byte map, and a merge loop
that merges the lowest-ranked adjacent pair until none is left. Worst cases measured: a 5,000
character word in 29 ms, 5 KB of base64 in 7 ms, 5,000 newlines in 36 ms — the loop is quadratic in
the length of a single pre-token, and a pre-token is normally short. At ~195,000 tokens/s it is not
the slow part of building a request.

## The compact encoding

The vocabulary in the file is redundant, which is what makes "encode the data for C#" interesting at
all. Checked against the file:

- ids 0–2 are `<｜begin▁of▁sentence｜>`, `<｜end▁of▁sentence｜>`, `<｜▁pad▁｜>`
- ids 3–258 are the 256 single-byte tokens, in GPT-2's byte-alphabet order
- id `259 + rank` is the token produced by merge `rank` — true for all 127,741 merges, no exceptions
- ids 128,000 and up are exactly `added_tokens`

So a counter needs no token strings: the merges key by id, and the id of a merged pair is implied by
its rank. The whole 4.2 MB reduces to 127,741 `(left, right)` pairs of ids — 1,021,932 bytes as two
`uint32`s per rank, 766,446 at three bytes each, less with varints. That loads in ~20 ms against
~140 ms for the JSON, and it is a quarter of the bytes. Only the 1,283 `added_tokens` still need
their text, and that is 45 KB of JSON.

The saving is start-up time, nothing else; a counter is built once and lives for the process. It is
worth doing if the table is generated at build time and embedded, not worth a bespoke build step on
its own.

## Where exactness comes from

The data format was never the hard part. These are the things that a straight port gets wrong, and
each one was caught by the corpus above:

- **Categories are code-point properties.** `\p{L}`, `\p{M}`, `\p{P}`, `\p{S}` and `\p{N}` are
  Unicode categories of characters, and a .NET `Regex` matches UTF-16 code units. An emoji outside
  the BMP is two `char`s that match none of them, so a regex port splits text differently from
  HuggingFace and can even throw. Scan `Rune`s.
- **`\s+(?!\S)` backtracks by one character.** Greedy `\s+` takes the whole run, the lookahead fails
  on the following non-space, and the engine gives one back. That is why `"  leading"` is `Ġ` +
  `Ġleading` and `"word    multiple"` is `ĠĠĠ` + `Ġmultiple`: the last space of a run belongs to the
  next piece.
- **`\s*[\r\n]+` backtracks to the last CR/LF run**, leaving any trailing spaces to `\s+`, so
  `"):\n    return"` is `):Ċ` + `ĠĠĠ` + `Ġreturn`.
- **`added_tokens` are matched first, leftmost-longest**, before any splitting, so `<｜User｜>` is one
  token and `<think>` is one token rather than `<` + `think` + `>`.
- **There is no normalizer, no `unk_token` and `byte_fallback` is false**, so every byte has a token
  and nothing is ever dropped.

## What was rejected

`Microsoft.ML.Tokenizers` 2.0.1 is the obvious managed candidate and the wrong one here. It has no
`tokenizer.json` loader: `BpeTokenizer.Create` takes a vocabulary and a merge list, and the
pre-tokenizer has to be rebuilt from the three regexes plus `ByteLevel`. Wired that way it agreed on
196 of 361 hand-written cases and 6 of 206 generated ones, and threw `ArgumentOutOfRangeException`
on 65. The failures cluster exactly where the .NET regex diverges from code-point matching — emoji,
astral letters, combining marks — and plain prose and code tokenized correctly. It is also the
slowest to build (~380 ms), and the easiest to misconfigure: passing the vocabulary as special
tokens instead of only `added_tokens` silently encodes every character as its own byte token.

## What adopting would change

- `ContextTrimmer.Trim` takes a budget in characters and `MessageLength` measures
  `Content + ToolCallsJson`; both become token counts, and each message is tokenized instead of
  measured. The rules that keep whole turns are unaffected.
- `BudgetSnapshot.ContextChars` becomes a token figure, and with it the context row's unit:
  `AssistantEndpoint` already writes the output row in `tokens` and the context row in `chars`
  through the same `WriteBudgetAsync`. The paragraph in [assistant](../assistant.md) that explains
  why the row counts the untrimmed transcript stays true; only the unit changes.
- `AssistantOptions.MaxContextChars` becomes `MaxContextTokens`, and its validator floor moves from
  characters to tokens. 120,000 characters is 26,800 tokens of C# or 105,000 of emoji, so the number
  cannot simply be renamed.
- The request is bigger than the transcript: `tools`, `tool_choice` and the per-message chat markers
  are JSON the client writes, and JSON is the 1.61 chars/token case. Counting the transcript alone
  would repeat the mistake in a new unit.
- Nothing currently checks prompt plus output against the model's window: `MaxContextChars` bounds
  the prompt and `MaxOutputTokens` (32,768) bounds the reply, and the two are never added up. A
  token count is what would let [RunProcessor](../../src/uk.osric.asp.Web/Assistant/RunProcessor.cs)
  refuse a request the provider would reject.
- `AssistantLiveTruncationTests` runs against the live provider; a token budget gives it an exact
  expected figure to assert on instead of a character proxy.

## Re-check triggers

- `Tokenizers.DotNet` gains a runtime package that loads on Alpine/musl, or the image stops being
  Alpine. Either reopens the cheapest option, which is the one with the least code.
- DeepSeek ships a new `tokenizer.json` (a version bump changes the vocabulary and the ranks
  together; the derived table must be regenerated with it, and the `259 + rank` identity re-checked).
- `Microsoft.ML.Tokenizers` gains a `tokenizer.json` loader or code-point pre-tokenization.
- The counter starts being built per request instead of once per process, where a 1 MB table and
  140 ms would be worth avoiding.

## Reproducing

The reference half, on a box with `tokenizers` installed:

```python
from tokenizers import Tokenizer
tok = Tokenizer.from_file("tokenizer.json")
print(tok.encode("Hello!", add_special_tokens=False).ids)   # [19923, 3]
```

The compact table, from the file's own vocabulary (no other input):

```python
import json, struct
model = json.load(open("tokenizer.json"))["model"]
vocab, merges = model["vocab"], model["merges"]
out = bytearray(struct.pack("<I", len(merges)))
for rank, pair in enumerate(merges):
    left, right = pair.split(" ")
    assert vocab[left + right] == 259 + rank
    out += struct.pack("<II", vocab[left], vocab[right])
open("merges.bin", "wb").write(out)                          # 1,021,932 bytes
```

The candidates, in a throwaway console project:

```bash
dotnet add package Tokenizers.DotNet
dotnet add package Tokenizers.DotNet.runtime.linux-x64
# new Tokenizer("/path/to/tokenizer.json").Encode(text) -> uint[] ids
```

```csharp
// Microsoft.ML.Tokenizers: no tokenizer.json loader, so the pipeline is rebuilt by hand.
var options = new BpeOptions(vocabulary) {          // Dictionary<string,int> from model.vocab
    Merges = mergeLines,                            // "left right" per rank
    PreTokenizer = new CompositePreTokenizer(
        [new RegexPreTokenizer(digits, specials),
         new RegexPreTokenizer(cjk, specials),
         new RegexPreTokenizer(gpt4, specials)], specials),
    SpecialTokens = specials,                       // added_tokens only, never the vocabulary
    ByteLevel = true,
};
BpeTokenizer.Create(options).EncodeToIds(text);
```

## Sources

- The measured snapshot: `tokenizer.json`, `tokenizer_config.json` and the chat template of the
  DeepSeek tokenizer under `/home/osric/tmp/deepseek_v4_tokenizer` — format `1.0`, `LlamaTokenizerFast`,
  128,000 tokens; the numbers here are that file's, and a new snapshot needs its own row
- [`tokenizers` format `1.0`](https://github.com/huggingface/tokenizers/tree/main/tokenizers/src) —
  `pre_tokenizers/split.rs` (the `Split` with `Isolated` behaviour), `pre_tokenizers/byte_level.rs`,
  and `tokenizer/mod.rs` for the order added tokens are matched in
- [GPT-2 `encoder.py`](https://github.com/openai/gpt-2/blob/master/src/encoder.py) — the byte-to-character
  alphabet that ids 3–258 follow
- [The GPT-4 pre-tokenizer pattern](https://github.com/openai/tiktoken/blob/main/tiktoken_ext/openai_public.py) —
  where the third `Split` regex comes from
- [`Microsoft.ML.Tokenizers`](https://github.com/dotnet/machinelearning/tree/main/src/Microsoft.ML.Tokenizers) —
  `BpeTokenizer.Create`, and the absence of a `tokenizer.json` entry point
- [`Tokenizers.DotNet`](https://github.com/sappho192/Tokenizers.DotNet) — the native wrapper used above
