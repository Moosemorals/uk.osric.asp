// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a delete answered with.</summary>
/// <param name="Deleted">Whether this call tombstoned the page; a missing page answers false rather than failing.</param>
/// <param name="WasLive">Whether the page was live before the call.</param>
public sealed record PageDeleteResult(bool Deleted, bool WasLive);
