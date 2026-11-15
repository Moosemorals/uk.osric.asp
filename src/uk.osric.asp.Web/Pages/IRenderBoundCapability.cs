// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>A capability the render can stop.</summary>
/// <remarks>
/// Two things end a capability call early: the render's deadline, and the render being given up on.
/// Both come from the shell, never from the page, because the SDK has no abandonment member
/// (DEC-123).
/// </remarks>
internal interface IRenderBoundCapability {

    /// <summary>Gives the capability the render's deadline.</summary>
    /// <param name="deadline">The deadline's token; it outlives this use of it.</param>
    void AttachDeadline(CancellationToken deadline);

    /// <summary>Stops accepting work, because the render was given up on.</summary>
    void Abandon();
}

/// <summary>A capability that owns resources until its render lifetime ends.</summary>
internal interface IRenderLifetimeAware {

    /// <summary>Attaches the render lifetime and its asynchronous cleanup boundary.</summary>
    /// <param name="lifetime">The render lifetime that owns this capability.</param>
    void AttachLifetime(RenderLifetime lifetime);
}
