// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Reflection;
using uk.osric.asp.Sdk;

/// <summary>The commit this build was made from.</summary>
internal sealed class BuildInfo : IBuildInfo {

    private const string MetadataKey = "GitCommit";

    private BuildInfo(string? commit) {
        Commit = string.IsNullOrWhiteSpace(commit) ? null : commit;
    }

    /// <summary>The one build serving this process.</summary>
    public static BuildInfo Current { get; } = new(ReadCommit());

    /// <inheritdoc/>
    public string? Commit { get; }

    private static string? ReadCommit() {
        return typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))
            ?.Value;
    }
}
