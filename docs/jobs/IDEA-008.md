<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-008 — Writing pages by hand is the only way to write pages

| | |
| --- | --- |
| Size | Large |
| Area | Web, product, security |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, `src/uk.osric.asp.Web/Pages/TemplateCompiler.cs` |

## Status

Implemented as the multi-turn authenticated page-authoring agent described in
[the implementation plan](../plans/ai-page-agent.md). The earlier one-shot draft sketch is
superseded by that plan; it is retained in history, not as a second implementation contract.

## Superseded sketch

The original idea was a one-shot draft surface with an Apply button. It is superseded because page
authoring benefits from inspecting the existing site, making localized edits, reading compile
diagnostics and iterating in one conversation. The implemented agent is disabled by default, uses a
configured OpenAI-compatible streaming provider, and has only bounded page-store tools. Its tool
approval defaults and provider data flow are recorded in DEC-111.
