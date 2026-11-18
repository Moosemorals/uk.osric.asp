// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>How a write against a pending interrupt ended.</summary>
internal enum InterruptResolutionResult {

    /// <summary>The record was resolved and the run is going again.</summary>
    Resolved,

    /// <summary>No interrupt with that run and owner exists, or the conversation is not the owner's.</summary>
    NotFound,

    /// <summary>The record or its run has moved on, so the form that arrived is stale.</summary>
    Conflict,
}
