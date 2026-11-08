// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>One layer's <see cref="IPageLayout"/>, composed with the layers inside it.</summary>
/// <remarks>
/// <para>
/// A chain renders inside out — the content first, then its layout, then the layout's parent — so
/// each state points at the layer it wraps. The head and the sheets are therefore read
/// outermost-first, which is the order they appear on the wire, even though the layers rendered in
/// the opposite order.
/// </para>
/// <para>
/// The title flows the other way: a new state is seeded with the title so far, so a layout reads
/// what the page called itself and may compose its own.
/// </para>
/// </remarks>
/// <param name="defaultLayout">The key the host wraps with when nothing says otherwise.</param>
/// <param name="inner">The layer this one wraps, when this is a layout.</param>
internal sealed class PageLayoutState(string? defaultLayout, PageLayoutState? inner = null) : IPageLayout {

    private readonly List<string> _head = [];
    private readonly PageLayoutState? _inner = inner;
    private readonly List<string> _stylesheets = [];
    private string? _title = inner?.TitleOrNull;

    /// <inheritdoc/>
    public string? Layout { get; set; } = defaultLayout;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">No title has been set in this or any level.</exception>
    public string Title {
        get => _title ?? throw new InvalidOperationException(
            "This page is wrapped by a layout but set no title. Set it with ctx.Features.GetRequired<IPageLayout>().Title.");
        set {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _title = value;
        }
    }

    /// <inheritdoc/>
    public string Head => string.Concat(_head) + (_inner?.Head ?? string.Empty);

    /// <inheritdoc/>
    public IReadOnlyList<string> Stylesheets => _inner is null ? _stylesheets : [.. _stylesheets, .. _inner.Stylesheets];

    /// <inheritdoc/>
    public string Body { get; init; } = string.Empty;

    /// <summary>The title set so far, or <see langword="null"/> when no layer has set one.</summary>
    public string? TitleOrNull => _title;

    /// <inheritdoc/>
    public void AddHead(string markup) {
        ArgumentNullException.ThrowIfNull(markup);

        _head.Add(markup);
    }

    /// <inheritdoc/>
    public void AddStylesheet(string href) {
        if (!PageDocument.IsSiteRelative(href)) {
            throw new ArgumentException($"'{href}' is not a site-relative URL.", nameof(href));
        }

        _stylesheets.Add(href);
    }
}
