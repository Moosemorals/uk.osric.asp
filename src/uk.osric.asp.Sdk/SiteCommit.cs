// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One line of the changelist a build carries.</summary>
/// <param name="Hash">
/// The full 40-character commit hash, or <see langword="null"/> for a line that is not a commit. A
/// build written by the Makefile carries only commits; a null means a hand-edited or malformed file,
/// and a page shows that line unlinked rather than dropping it.
/// </param>
/// <param name="Subject">The commit's subject, or the line as it was written.</param>
public sealed record SiteCommit(string? Hash, string Subject);
