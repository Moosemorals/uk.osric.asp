// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

internal sealed record AgentPathResult(PagePath? Path, string? Error) {
    public bool Success => Error is null;
}

internal sealed class AgentPaths {
    private readonly ReservedPaths _reserved;
    private readonly HashSet<string> _protectedPaths;
    private readonly PagePath? _writablePrefix;

    public AgentPaths(ReservedPaths reserved, AgentOptions options) {
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(options);
        _reserved = reserved;
        _protectedPaths = options.ProtectedPaths
            .Select(PagePath.Normalize)
            .Where(path => path.IsAddressable)
            .Select(path => path.Key)
            .ToHashSet(StringComparer.Ordinal);
        _writablePrefix = string.IsNullOrWhiteSpace(options.WritablePathPrefix)
            ? null
            : PagePath.Normalize(options.WritablePathPrefix);
    }

    public AgentPathResult Validate(string? pathText, bool write) {
        if (string.IsNullOrWhiteSpace(pathText)) {
            return Invalid("A page path is required.");
        }

        PagePath path = PagePath.Normalize(pathText);
        if (!path.IsAddressable) {
            return Invalid("The page path is not addressable.");
        }
        if (IsProtected(path)) {
            return Invalid($"The path {path.Key} is reserved or protected by the site.");
        }
        if (write && _writablePrefix is { } prefix && !IsWithin(path, prefix)) {
            return Invalid("The path is outside Agent:WritablePathPrefix.");
        }

        return new AgentPathResult(path, null);
    }

    public bool IsProtected(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return _reserved.IsReserved(path) || _protectedPaths.Contains(path.Key);
    }

    private static bool IsWithin(PagePath path, PagePath prefix) =>
        string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
        || prefix.Key == "/"
        || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);

    private static AgentPathResult Invalid(string error) => new(null, error);
}
