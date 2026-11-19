// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>Turns the store's answer to a move into the answer a page, a tool or the shell receives.</summary>
/// <remarks>
/// One mapping, so the write capability, the assistant's <c>move_page</c> and the shell's <c>mv</c>
/// cannot word the same outcome differently. A library is the one store answer with no value of its
/// own: no argument makes that move legal in this release, so it throws.
/// </remarks>
internal static class PageMoveAnswer {

    /// <summary>The page's answer to a move the store attempted.</summary>
    /// <param name="moved">What the store did.</param>
    /// <param name="from">The key the page had, for the library refusal.</param>
    /// <exception cref="InvalidOperationException">The page is a library.</exception>
    public static PageMoveResult From(PageMoveStoreResult moved, PagePath from) {
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(from);

        if (moved.Outcome == PageMoveStoreOutcome.IsLibrary) {
            throw new InvalidOperationException(
                $"{from.Key} is a library: its key is in every dependent's closure, so moving it would leave those pages naming an empty key. Move the pages that name it first.");
        }

        return moved.Outcome switch {
            PageMoveStoreOutcome.Moved => new PageMoveResult(PageMoveOutcome.Moved, moved.Hash, []),
            PageMoveStoreOutcome.Stale => new PageMoveResult(PageMoveOutcome.Stale, null, []),
            PageMoveStoreOutcome.SourceNotFound => new PageMoveResult(PageMoveOutcome.SourceNotFound, null, []),
            PageMoveStoreOutcome.TargetExists => new PageMoveResult(PageMoveOutcome.TargetExists, null, []),
            _ => new PageMoveResult(PageMoveOutcome.FolderNotEmpty, null, moved.Below ?? []),
        };
    }
}
