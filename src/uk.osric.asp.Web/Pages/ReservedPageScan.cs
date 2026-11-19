// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Finds stored paths at keys the shell answers, which no page can ever serve.</summary>
internal static class ReservedPageScan {

    /// <summary>The stored paths that are reserved keys.</summary>
    /// <param name="pages">The page store.</param>
    /// <param name="reserved">The shell's own key set.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    public static async Task<IReadOnlyList<PagePath>> FindAsync(
        IPageStore pages,
        ReservedPaths reserved,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(reserved);

        IReadOnlyList<PagePath> stored = await pages.ListStoredPathsAsync(cancellationToken);
        return [.. stored.Where(reserved.IsReserved)];
    }
}
