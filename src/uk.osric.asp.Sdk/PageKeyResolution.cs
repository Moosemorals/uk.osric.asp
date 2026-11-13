// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a key names, as <see cref="IPageEditor.Resolve"/> reports it.</summary>
/// <remarks>
/// A page's edited key arrives as a query value, so a key that names nothing is an answer the page
/// renders rather than an exception it catches. <see cref="IsReserved"/> is the host's own routing
/// rule reported, so the page can explain why a shell file is not editable without owning the list.
/// </remarks>
/// <param name="Key">The canonical key, or <see langword="null"/> when the text names no page.</param>
/// <param name="IsReserved">Whether one of the site's own routes answers for the key.</param>
/// <param name="Error">Why the text names no page, or <see langword="null"/> when it does.</param>
public sealed record PageKeyResolution(string? Key, bool IsReserved, string? Error) {

    /// <summary>Whether the text names a page.</summary>
    public bool IsSuccess => Error is null;
}
