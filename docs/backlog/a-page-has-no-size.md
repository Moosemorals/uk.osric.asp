<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page has no size, and the site counts two units

| | |
| --- | --- |
| Difficulty | determined-mountain-goat |
| Utility | high |
| Status | planned |
| Blocked by | — |
| Plan | [counting-bytes](../plans/counting-bytes.md) |
| Area | Core (Assistant), Sdk, Web (pages, assistant, configuration), docs, seeds |
| Location | `src/uk.osric.asp.Sdk/PageEntry.cs`, `src/uk.osric.asp.Web/Pages/SqlitePageReadStore.cs`, `src/uk.osric.asp.Core/Assistant/ContextBudget.cs`, `src/uk.osric.asp.Web/appsettings.json`, `src/uk.osric.asp.Web/Pages/Seeds/help` |
| Spike | — |

## Idea

A listing row says what a page is — key, kind, hash, saved time, state, libraries — and not how large
it is. `PageEntry` carries no length, so `ls -l`, `stat` and a view that lists pages cannot answer
"how large is this page" without reading the whole source, and the assistant's `stat` command has a
size field with nothing to put in it.

The unit is the other half. The shapes built since the storage model already limit themselves in
UTF-8 bytes — objects, messages, secrets, jobs, the journal, declared values — and `ContextBudget`
already scales its share of the window by `BytesPerToken`. The older limits count `string.Length`,
which is UTF-16 code units, and are named `...Chars`: eleven configuration keys, the string
comparisons that guard them and the handful of truncation sites. On ASCII the two readings agree; on
emoji and CJK they do not, which is where a limit is most likely to be reached. The mismatch is not
theoretical: `ContextBudget` states 1.6 bytes per token and the tool result cut compares `json.Length`,
so a result of astral characters is cut later than its own budget allows.

Make UTF-8 bytes the site's one unit, and make a listing row carry the head's stored byte length. The
second is nearly free from the first: the length is `octet_length(Template)` in the join the listing
already makes — answered from the record header, with no column, no backfill and no way to drift from
the source it describes.

## Sketch

1. **One unit.** Every size limit is UTF-8 bytes and its key says `Bytes`; a retired key is refused at
   startup with its replacement named, the way `fetch-is-a-rate` retired the per-render counts.
   `string.Length` stops guarding a limit anywhere.
2. **A boundary-safe clip.** One helper in `Core` clips text to at most *n* UTF-8 bytes without
   splitting a code point, and the truncation sites use it rather than `text[..n]`. Its tests are a
   multi-byte character at the boundary, an astral character, a lone surrogate and text exactly at the
   limit.
3. **The size is a listing fact.** `PageEntry` gains the stored byte length, projected by `ListAsync`
   as `octet_length(Template)`, so a page knows the size of any other page in the tree without reading
   it. A row with no head has no length.
4. **The assistant's budget becomes real.** `ToolResultChars`, `MessageChars` and `MemoryCharsPerRun`
   take their byte names, are measured with `Encoding.UTF8.GetByteCount`, and the cut is the
   boundary-safe clip. `BodyBudget` charges the bytes it is about to write rather than counting a
   string it has already encoded.
5. **The manual and the prompt.** `/help`, `docs/` and the author prompt say bytes wherever they said
   characters, and `stat` reports the size the listing now carries.

## Open questions

1. **Do the human-facing numbers rise with the unit?** `MaxKeyChars`, `MaxNameChars` and
   `MaxQuestionChars` were chosen as a number of characters a person types; the same number of bytes is
   about a third as much room for CJK and a quarter for emoji. Do they multiply (×3 or ×4, say) so an
   author keeps the room they had, or keep their values and let the manual state the smaller floor?
2. **Is the search snippet window a byte window?** `Pages:Search:MaxContextChars` bounds the text shown
   around a match — a display width rather than a stored size. Does it move to bytes with the rest, or
   stay a character window under a name that says so, a second unit chosen on purpose?
3. **What does `stat` print, and does `ls -l` gain a column?** `stat` needs the size; POSIX `ls -l`
   prints one too. Is the size a `stat` field only, or a column in `ls -l` as well — and is its name
   `SourceBytes`, the stored template's UTF-8 bytes?

## Not in scope

- **Grapheme clusters.** A reader's "character" is a cluster; counting them needs a library and
  answers nothing a limit can use.
- **The provider's tokens.** The window is still tokens; this changes the byte estimate that stands in
  for them, not the accounting.
- **Limits that are already bytes.** Objects, messages, secrets and the archive need no change.
