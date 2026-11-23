// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Narrows a listing to one lifetime, and bounds how many entries it may answer.</summary>
/// <param name="Lifetime">The lifetime to select, or <see langword="null"/> for every entry.</param>
/// <param name="Limit">
/// The most entries to answer, or <see langword="null"/> for the subject's own limit. A limit beyond that
/// limit is treated as the limit.
/// </param>
public sealed record StoreFilter(StoreLifetimeKind? Lifetime = null, int? Limit = null);
