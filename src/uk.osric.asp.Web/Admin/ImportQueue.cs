// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Web.Work;

/// <summary>The bounded queue of imports waiting for the worker.</summary>
/// <remarks>
/// The queue refuses an import that does not fit rather than growing without bound. With one editor
/// this is a backstop, not a feature — the worker runs one job at a time because there is one
/// compile slot behind it.
/// </remarks>
/// <param name="options">The archive options, whose staged-import count bounds the queue.</param>
internal sealed class ImportQueue(ArchiveOptions options) : BoundedWorkQueue<string>(Capacity(options)) {

    private static int Capacity(ArchiveOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return Math.Max(1, options.MaxStagedImports);
    }
}
