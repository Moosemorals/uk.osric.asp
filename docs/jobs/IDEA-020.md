<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-020 — Import only adds and overwrites, so an archive cannot reset a site

| | |
| --- | --- |
| Size | Medium |
| Area | Web, product, storage, operations |
| Location | `src/uk.osric.asp.Web/Admin/`, `src/uk.osric.asp.Web/Pages/` |

## Idea

The import in [IDEA-009](IDEA-009.md) is additive on purpose: every page in the archive is saved,
every page absent from it is untouched, and nothing is deleted. That is the safe default, but it
means an archive cannot put a site into a known position — pages the archive does not mention
survive, so "this is now exactly what the archive says" is not something the import can promise.

The case that prompted this is small: a dev instance carrying three files — a tiny modal editor in a
textarea — across to the real site. That is exactly what selective import already does: export those
pages, untick anything else, import. A wipe would add risk to it and remove nothing.

The other case is restoring after a crash or a compromise, and the archive is not a backup. It
carries page source and page state, not revision history, users, auth tickets, conversations or
configuration. A "nuke" would delete the things the archive cannot put back, and on a site after a
couple of years the blast radius is a week-old archive silently deleting every page created since,
together with all of its history.

So this is recorded, not planned. It belongs here because "reset the site to this archive" is a
reasonable thing to want, and because the answer should be written down before somebody adds a
checkbox to the import form.

## Sketch

If it is ever built, these decisions come first.

1. **Scope.** Page heads, revisions and page state only, or the whole `storage.db`? The file also
   holds `Users`, `AuthTickets`, `AssistantConversations`, messages and approvals; the data
   protection keys are on a separate volume. Wiping the file signs everyone out and leaves a site
   with no account until the CLI recreates one. That is an operator action, not a web form, and a
   page-scope reset is the only version with a defensible product story.
2. **A separate action, not a checkbox.** A destructive "replace the site's pages" verb with its own
   confirmation, never a modifier on an import that is otherwise safe. The delete and scrub
   confirmations are the precedent for the wording; this one earns more than a button.
3. **Show the loss first.** Before confirming: how many pages the archive will add, overwrite and
   delete, and that the deleted pages lose their retained history. Scrub already knows how to remove
   revisions; the preview has to name them.
4. **Typed confirmation.** Typing the site's host name or a phrase, not a checkbox. The blast radius
   is the reason.
5. **A pre-nuke export.** Offer a download of the current site before the wipe, so the destructive
   step has a way back that is not "restore the volume". It is the same writer the export view uses.
6. **One transaction.** `SqlitePageBatchStore` can delete every revision, head and state row and
   insert the archive in one transaction, so a crash rolls back and leaves no half-wiped site. That
   is the easy part; the decision is whether the batch store should grow a replace mode at all, and
   whether "delete everything, then import" is one operation or two.
7. **History is destroyed.** A reset means scrub, not tombstone: a tombstone retains the history the
   reset exists to remove. That is irreversible, and the existing warning that scrub is best-effort
   rather than forensic erasure applies here too.
8. **Not a security repair.** Restoring pages into a database that may have been compromised does
   not remove the compromise, and the page archive cannot vouch for users, sessions or
   configuration. The answer to a compromise stays: restore the file backup, rotate secrets, then
   investigate.
9. **Tests.** A wipe and import either commits wholly or not at all; a crash during the batch rolls
   back; the preview counts match what is actually deleted; the typed confirmation cannot be
   skipped; the pre-nuke download is produced before the batch starts.

## Relationship to other work

[The import and export plan](../plans/archive/page-import-export.md) keeps import additive on purpose. The
deployment guide's backup story is the volume, not the archive: see
[deploy/README.md](../../deploy/README.md#backups). If this ever ships, it should point at that story
rather than compete with it.
