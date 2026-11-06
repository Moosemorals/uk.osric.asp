// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>Page storage the render can stop.</summary>
/// <remarks>
/// Two things end a storage call early: the render's deadline, and the render being given up on. Both
/// come from the shell, never from the page, because the SDK has no abandonment member (DEC-123).
/// </remarks>
internal interface IRenderBoundPageStorage : IPageStorage {

    /// <summary>Gives the storage the render's deadline.</summary>
    /// <param name="deadline">The deadline's token; it outlives this use of it.</param>
    void AttachDeadline(CancellationToken deadline);

    /// <summary>Stops accepting work, because the render was given up on.</summary>
    void Abandon();
}
