<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot give bytes out

| | |
| --- | --- |
| Difficulty | — |
| Utility | — |
| Status | unshaped |
| Blocked by | — |
| Area | — |
| Location | — |
| Spike | — |

## Idea

A page cannot give bytes out: `IPageResponse` writes strings and every body is buffered against
`Pages:Render:MaxBodyChars`, so an archive, an image or a generated file can only leave as base64
inside HTML — the other half of [keel](../spikes/keel.md)'s archive blocker. Give a page a binary body
that can **only** be streamed: the bytes are written after `FlushAsync` has committed the response, so
the render slot becomes a stream slot and the existing clocks bound it (`Pages:Stream:SilenceSeconds`,
`MaxSeconds` and `Slots`) rather than a byte limit, while a buffered text body keeps the limit it has.
`Content-Disposition` joins the header allow-list, or a `Download(fileName)` member owns both it and
the content type, with the file name escaped and never taken from the request.
