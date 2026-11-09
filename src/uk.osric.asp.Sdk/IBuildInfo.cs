// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The build that is answering the request.</summary>
/// <remarks>
/// This is a capability because a page may not read the assembly or the environment: the page API
/// denies both. A build made outside a checkout records no commit, so <see cref="Commit"/> is
/// <see langword="null"/> rather than a placeholder a page might mistake for a hash.
/// </remarks>
public interface IBuildInfo {

    /// <summary>The full git commit hash, or <see langword="null"/> when the build has none.</summary>
    public string? Commit { get; }
}
