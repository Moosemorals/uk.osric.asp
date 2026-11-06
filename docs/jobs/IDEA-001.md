<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-001 — The editor is a plain textarea

| | |
| --- | --- |
| Size | Large |
| Area | Web, developer experience |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` |
| Spike | [Ace evaluated as the editor](../spikes/ace-editor.md) |

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
   the pages stay small. [Done](../plans/archive/editor-line-gutter.md) — with one correction: a
   server-rendered gutter is only true until the first keystroke, so the page carries a small
   native module that keeps the numbers in step with what is typed.
3. Third party: [Ace](../spikes/ace-editor.md) is the option that needs no build step — it ships a
   prebuilt bundle vendored under `wwwroot` and served like `site.css` — and it is the only one
   that brings vim keybindings, at the cost of a small custom mode for `<% %>`. CodeMirror 6 is
   smaller but needs a bundler, so it is out while the project has no npm; Monaco is heavier than
   the editor is worth. Either way the asset is pinned and the spike records how to update it.
4. There is no `Content-Security-Policy` today, so adding script at all is the moment to add one
   (self, and a nonce only for any inline script we add — the spike found Ace needs none) rather
   than afterwards. [Done](../plans/archive/editor-line-gutter.md) with the module as an external file,
   which needs no nonce at all.
5. Keep the save path exactly as it is: an antiforgery-checked `POST`, then `303` back to the
   editor with a flash message. The editor is a view; it must not grow a second way to save.
6. Test the parts a browser test would otherwise have to: diagnostics mapped to lines, and a save
   that keeps the submitted text on failure. Anything about caret behaviour is a browser test.
