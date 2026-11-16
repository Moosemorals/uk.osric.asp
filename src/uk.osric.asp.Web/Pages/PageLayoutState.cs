// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Html;

/// <summary>One layer's <see cref="IPageLayout"/>: its own slots, and the layer inside it.</summary>
/// <remarks>
/// The host no longer composes the layer inside into these slots: <see cref="Content"/> is handed
/// over and the wrapper folds what it wants. That is what makes a wrapper and an embed the same
/// relation from opposite ends.
/// </remarks>
internal sealed class PageLayoutState : IPageLayout {

    private readonly List<string> _head = [];
    private readonly List<string> _stylesheets = [];
    private string? _title;

    /// <inheritdoc/>
    public PageCapture? Content { get; init; }

    /// <inheritdoc/>
    public string? Title {
        get => _title;
        set {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            _title = value;
        }
    }

    /// <inheritdoc/>
    public string Head => string.Concat(_head);

    /// <inheritdoc/>
    /// <remarks>A sheet this layer asked for twice is linked once.</remarks>
    public IReadOnlyList<string> Stylesheets {
        get {
            List<string> sheets = [];

            foreach (string href in _stylesheets) {
                if (!sheets.Contains(href, StringComparer.Ordinal)) {
                    sheets.Add(href);
                }
            }

            return sheets;
        }
    }

    /// <inheritdoc/>
    public void AddHead(string markup) {
        ArgumentNullException.ThrowIfNull(markup);

        _head.Add(markup);
    }

    /// <inheritdoc/>
    public void AddStylesheet(string href) {
        if (!ShellDocument.IsSiteRelative(href)) {
            throw new ArgumentException($"'{href}' is not a site-relative URL.", nameof(href));
        }

        _stylesheets.Add(href);
    }
}
