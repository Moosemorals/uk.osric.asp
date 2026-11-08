// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Pages;

/// <summary>A validated page path, or why the assistant may not use the one it asked for.</summary>
internal sealed record PathValidationResult(PagePath? Path, string? Error) {
    public bool IsSuccess => Error is null;
}

/// <summary>The rule for which pages the assistant may read and write.</summary>
internal sealed class PagePathPolicy {
    private readonly ReservedPaths _reserved;
    private readonly HashSet<string> _protectedPaths;
    private readonly PagePath? _writablePrefix;

    public PagePathPolicy(ReservedPaths reserved, AssistantOptions options) {
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

    public PathValidationResult Validate(string? pathText, bool write) {
        if (string.IsNullOrWhiteSpace(pathText)) {
            return Invalid("A page path is required.");
        }

        PagePath path = PagePath.Normalize(pathText);
        if (!path.IsAddressable) {
            return Invalid("The page path is not addressable.");
        }
        if (IsProtected(path)) {
            return Invalid($"The path {MessageText.Sanitize(path.Key)} is reserved or protected by the site.");
        }
        if (write && _writablePrefix is { } prefix && !IsWithin(path, prefix)) {
            return Invalid("The path is outside Assistant:WritablePathPrefix.");
        }

        return new PathValidationResult(path, null);
    }

    /// <summary>Validates an optional prefix, which only has to be an addressable path.</summary>
    /// <param name="prefixText">The prefix the model asked for, or null for none.</param>
    public static PathValidationResult ValidatePrefix(string? prefixText) {
        if (string.IsNullOrWhiteSpace(prefixText)) {
            return new PathValidationResult(null, null);
        }

        PagePath prefix = PagePath.Normalize(prefixText);

        return prefix.IsAddressable
            ? new PathValidationResult(prefix, null)
            : Invalid("The prefix is not an addressable page path.");
    }

    public bool IsProtected(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return _reserved.IsReserved(path) || _protectedPaths.Contains(path.Key);
    }

    private static bool IsWithin(PagePath path, PagePath prefix) =>
        string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
        || prefix.Key == "/"
        || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);

    private static PathValidationResult Invalid(string error) => new(null, error);
}
