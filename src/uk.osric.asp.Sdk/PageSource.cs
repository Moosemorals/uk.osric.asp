// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A page's template, with the revision it belongs to.</summary>
/// <param name="Key">The page's canonical key.</param>
/// <param name="Hash">The revision's hash.</param>
/// <param name="SavedAt">When the revision was saved.</param>
/// <param name="Source">The template text, exactly as it was stored.</param>
/// <remarks>
/// The source is text, not markup: write it with <see cref="IContext.Write"/> so it is escaped,
/// because a template is exactly the kind of value a source view must not run.
/// </remarks>
public sealed record PageSource(
    string Key,
    string Hash,
    DateTimeOffset SavedAt,
    string Source);
