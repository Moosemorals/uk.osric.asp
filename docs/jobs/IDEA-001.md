<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-001 — The editor is a plain textarea

| | |
| --- | --- |
| Size | Large |
| Area | Web, developer experience |
| Location | `src/uk.osric.asp.Web/Endpoints/EditorEndpoint.cs` |

## Idea

The editor is a form around a ten-row `<textarea>` with a Save button. There is no syntax
highlighting, no line numbers, no way to insert the `<% %>` delimiters, and the page's diagnostics
are printed in a block above the form rather than against the line that produced them. Writing a
page of any size in it is unpleasant, and it is the thing most likely to stop the site being used.

Either improve ours or adopt a third-party editor component. The second is a real dependency
decision for a project that has otherwise avoided them, so it needs the justification
`Directory.Packages.props` asks for.

## Sketch

1. Write down what the editor has to do before choosing anything: highlight text and code blocks
   separately, keep the caret and selection across a save, show each diagnostic on its line, and
   survive a save that does not compile.
2. Ours: keep the textarea, add a server-rendered line gutter and anchor each diagnostic to a line
   with a link; the whole thing stays HTML and the save path does not change. Cheap, and enough if
   the pages stay small.
3. Third party: CodeMirror 6 is the smallest of the real options; Monaco is heavier than the editor
   is worth. Either way the asset is vendored under `wwwroot` and served like `site.css`, with no
   build step — which means a pinned version and a note on how to update it.
4. There is no `Content-Security-Policy` today, so adding script at all is the moment to add one
   (self plus the site's own nonce) rather than afterwards.
5. Keep the save path exactly as it is: an antiforgery-checked `POST`, then `303` back to the
   editor with a flash message. The editor is a view; it must not grow a second way to save.
6. Test the parts a browser test would otherwise have to: diagnostics mapped to lines, and a save
   that keeps the submitted text on failure. Anything about caret behaviour is a browser test.
