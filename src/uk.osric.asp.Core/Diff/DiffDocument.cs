// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Diff;

/// <summary>The line changes between two documents.</summary>
/// <param name="Hunks">The changed regions, in document order; empty when the documents have the same lines.</param>
/// <param name="AddedLines">How many lines the later document adds.</param>
/// <param name="RemovedLines">How many lines the later document removes.</param>
/// <param name="IsCoarse">
/// Whether the documents were too different to align line by line, so the changed middle is one
/// removed block followed by one added block. Every line is still present; the alignment is the part
/// that was given up.
/// </param>
public sealed record DiffDocument(
    IReadOnlyList<DiffHunk> Hunks,
    int AddedLines,
    int RemovedLines,
    bool IsCoarse) {

    /// <summary>Whether the two documents have the same lines.</summary>
    public bool IsEmpty => Hunks.Count == 0;
}
