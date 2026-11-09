// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>What a chart says about itself: its title, its value axis and its words for no data.</summary>
/// <remarks>
/// The frame is data, not chrome: every renderer draws the same title band and legend from it, so a
/// chart served on its own has everything it needs and needs no stylesheet to say what it shows.
/// </remarks>
/// <param name="Id">The fragment prefix for the series anchors; it is unique within one document.</param>
/// <param name="Title">The drawing's title, for its accessible name and its title band.</param>
/// <param name="YTitle">What the value axis measures.</param>
/// <param name="EmptyText">What to draw when there is nothing to draw.</param>
/// <param name="Zone">The zone the time labels and the hover text are written in.</param>
public sealed record ChartFrame(
    string Id,
    string Title,
    string YTitle,
    string EmptyText,
    TimeZoneInfo Zone) {

    /// <summary>Checks the frame before a renderer draws it.</summary>
    /// <exception cref="ArgumentException">The id or the title is blank.</exception>
    internal void Validate() {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
    }
}
