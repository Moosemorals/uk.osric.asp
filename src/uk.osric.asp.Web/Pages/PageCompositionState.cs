// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One page that will wrap the page that declared it.</summary>
/// <param name="Key">The wrapper's canonical key.</param>
/// <param name="Request">The request the wrapper runs against.</param>
/// <param name="IsDefault">
/// Whether the page asked for the site's layout rather than naming one, which is what decides
/// whether a wrapper that cannot be served is skipped or is an error.
/// </param>
internal readonly record struct PageWrap(PagePath Key, PageRequest Request, bool IsDefault);

/// <summary>The wrappers one layer declared, innermost first.</summary>
/// <remarks>
/// A declaration, not a handover: the host runs the chain after the page returns, so an early
/// <c>return;</c> from a guard is still wrapped. Each call adds a layer outside the last.
/// </remarks>
internal sealed class PageCompositionState {

    private readonly List<PageWrap> _wraps = [];

    /// <summary>The wrappers, in the order the page declared them.</summary>
    public IReadOnlyList<PageWrap> Wraps => _wraps;

    /// <summary>Whether the page declared no wrapper, so it is served exactly as it wrote itself.</summary>
    public bool IsEmpty => _wraps.Count == 0;

    /// <summary>Records one wrapper outside every layer already declared.</summary>
    /// <param name="key">The wrapper's canonical key.</param>
    /// <param name="request">The request the wrapper runs against.</param>
    /// <param name="isDefault">Whether this is the site's layout rather than a named page.</param>
    public void Wrap(PagePath key, PageRequest request, bool isDefault) {
        ArgumentNullException.ThrowIfNull(request);

        _wraps.Add(new(key, request, isDefault));
    }
}
