// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>A 1-based line and column in the page template, as the author typed it.</summary>
/// <param name="Line">The 1-based line in the template.</param>
/// <param name="Column">The 1-based column in the template.</param>
/// <remarks>
/// The only coordinate system that is stored or displayed. Generated-source coordinates stay inside
/// Roslyn; an unknown position is <see langword="null"/>, never (0, 0), so it cannot be mistaken for
/// a real one.
/// </remarks>
public readonly record struct TemplatePosition(int Line, int Column) {

    /// <inheritdoc/>
    public override string ToString() {
        return $"line {Line}, column {Column}";
    }
}
