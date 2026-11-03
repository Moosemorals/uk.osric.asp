<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-009 — Pages cannot be exported or imported as an archive

| | |
| --- | --- |
| Size | Medium |
| Area | Web, product, storage |
| Location | `src/uk.osric.asp.Web/Storage/`, `src/uk.osric.asp.Web/Editor/` |

## Idea

Export the stored pages to a ZIP whose entries mirror their page paths, and import that same
archive shape to restore or move a site's pages. Export is the straightforward half; import needs
to treat the archive as untrusted input and validate it before changing stored pages.

## Sketch

1. Export each page's source under its stored path in the archive. Keep the format simple enough
   that the exported ZIP is also the import format.
2. Read archive entries directly rather than extracting paths to disk. Validate entry names as
   addressable page paths, and reject duplicates, reserved paths, and entries that exceed defined
   size limits.
3. Validate the complete archive before saving any pages, then use the normal save and compile
   path. Decide whether one invalid page rejects the whole import or is reported while valid pages
   are imported; avoid a partially applied archive by accident.
4. Put both operations behind the existing editor authorization and test malformed archives,
   path traversal names, duplicate paths, and a successful export/import round trip.