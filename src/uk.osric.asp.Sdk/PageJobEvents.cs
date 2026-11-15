// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What one read of a job's journal answered.</summary>
/// <param name="Events">The events after the caller's sequence, oldest first; empty when there are none.</param>
/// <param name="Next">
/// The sequence to pass back next: the last event's, or the caller's own when nothing arrived. A reset
/// moves it to the oldest sequence the journal still holds.
/// </param>
/// <param name="Reset">
/// Whether the caller's sequence had already been dropped. The view should start again from
/// <paramref name="Next"/> rather than treat what it missed as delivered.
/// </param>
/// <param name="State">The job's state when the call answered, or <see langword="null"/> when it is gone.</param>
/// <param name="Failure">Why the job failed, or <see langword="null"/> when it did not.</param>
public sealed record PageJobEvents(
    IReadOnlyList<PageJobEvent> Events,
    long Next,
    bool Reset,
    PageJobState? State,
    string? Failure);
