// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One job as its owner sees it.</summary>
/// <param name="Id">The job's id.</param>
/// <param name="Worker">The canonical key of the worker that is running.</param>
/// <param name="Path">The canonical key of the page that started it.</param>
/// <param name="State">What the job is doing, or how it ended.</param>
/// <param name="StartedAt">When it was accepted.</param>
/// <param name="EndedAt">When it finished, or <see langword="null"/> while it is still running.</param>
/// <param name="Failure">
/// Why it failed, in the host's words, or <see langword="null"/> when it did not. A job starts with a
/// <see langword="null"/> and only a failure sets it.
/// </param>
public sealed record PageJobInfo(
    string Id,
    string Worker,
    string Path,
    PageJobState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Failure);
