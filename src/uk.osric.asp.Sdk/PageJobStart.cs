// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The answer to a job that was asked to start.</summary>
/// <param name="Id">The new job's id, or <see langword="null"/> when it was refused.</param>
/// <param name="Refusal">Why it was refused, or <see cref="PageJobRefusal.None"/>.</param>
public readonly record struct PageJobStart(string? Id, PageJobRefusal Refusal) {

    /// <summary>Whether the job started.</summary>
    public bool Started => Refusal is PageJobRefusal.None;
}
