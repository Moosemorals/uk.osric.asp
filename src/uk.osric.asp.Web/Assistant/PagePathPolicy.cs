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

/// <summary>Which pages the assistant may read, and which it may write.</summary>
/// <remarks>
/// Two kinds of key are read-only. A <em>shell file</em> is a key the site's own routes answer
/// (<c>Pages:ReservedPaths</c>): it is readable, because the tools describe what the route does, and
/// it is never a stored page. A <em>protected</em> path (<c>Assistant:ProtectedPaths</c>) is an
/// ordinary page the operator has locked against the assistant. Everything else is a page the
/// assistant may list, read and write, subject to <c>Assistant:WritablePathPrefix</c>.
/// </remarks>
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

    /// <summary>The shell files: every key the site's own routes answer.</summary>
    public IReadOnlyList<PagePath> Reserved => _reserved.Keys;

    public PathValidationResult Validate(string? pathText, bool write) {
        if (string.IsNullOrWhiteSpace(pathText)) {
            return Invalid("A page path is required.");
        }

        PagePath path = PagePath.Normalize(pathText);
        if (!path.IsAddressable) {
            return Invalid("The page path is not addressable.");
        }
        if (write && IsReserved(path)) {
            PagePath owner = ShadowedBy(path) ?? path;
            return Invalid($"Not permitted: {MessageText.Sanitize(path.Key)} is a shell file. The site "
                + $"answers {MessageText.Sanitize(owner.Key)} with its own code, so no page is stored there.");
        }
        if (write && IsProtected(path)) {
            return Invalid($"Not permitted: {MessageText.Sanitize(path.Key)} is locked read-only by "
                + "Assistant:ProtectedPaths. The owner changes it in the editor.");
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

    /// <summary>Whether the site's own routes answer for this key.</summary>
    public bool IsReserved(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return _reserved.IsReserved(path);
    }

    /// <summary>Whether the operator has locked this page against the assistant.</summary>
    public bool IsProtected(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return _protectedPaths.Contains(path.Key);
    }

    /// <summary>Whether the assistant may read but never write this key: a route, or a locked page.</summary>
    public bool IsReadOnly(PagePath path) => IsReserved(path) || IsProtected(path);

    /// <summary>Whether a save at this key would be accepted, which is what the permission column says.</summary>
    public bool CanWrite(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return Validate(path.Key, write: true).IsSuccess;
    }

    /// <summary>The shell file that shadows this key, when the key itself is not the reserved one.</summary>
    public PagePath? ShadowedBy(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);
        return _reserved.MatchingPrefix(path);
    }

    private static bool IsWithin(PagePath path, PagePath prefix) =>
        string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
        || prefix.Key == "/"
        || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);

    private static PathValidationResult Invalid(string error) => new(null, error);
}
