// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One item in a navigation landmark.</summary>
/// <param name="Href">The URL, unencoded: <see cref="PageDocument.Nav"/> encodes it.</param>
/// <param name="Text">The visible text, unencoded: <see cref="PageDocument.Nav"/> encodes it.</param>
/// <param name="IsCurrent">
/// Whether this is the location being shown. A current item is text rather than a link to itself,
/// marked so assistive technology says which one it is.
/// </param>
public readonly record struct NavLink(string Href, string Text, bool IsCurrent = false);
