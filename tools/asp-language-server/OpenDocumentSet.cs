// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

/// <summary>A document the editor has open, at the version the editor last sent.</summary>
/// <param name="Uri">The document's URI, which is its identity to the protocol.</param>
/// <param name="Text">The template as it stands.</param>
/// <param name="Version">The editor's version of that text.</param>
internal sealed record OpenDocument(string Uri, string Text, int Version);

/// <summary>The documents the editor has open, by URI, with the version each one is at.</summary>
/// <remarks>
/// The version is what keeps answers in order: a check publishes for the version it compiled, so a
/// slow check of an older text can never replace the squiggles of a newer one.
/// </remarks>
internal sealed class OpenDocumentSet {

    // A string key compares ordinally by default, which is every URI the protocol names.
    private readonly Dictionary<string, OpenDocument> _byUri = [];

    /// <summary>Finds an open document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <returns>The document, or <see langword="null"/> when it is not open.</returns>
    public OpenDocument? Find(string uri) => _byUri.GetValueOrDefault(uri);

    /// <summary>Records a document the editor opened.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="text">Its text.</param>
    /// <param name="version">The editor's version, when it sent one.</param>
    /// <returns>The document as it was recorded.</returns>
    public OpenDocument Opened(string uri, string text, int? version) {
        OpenDocument document = new(uri, text, version ?? 1);
        _byUri[uri] = document;
        return document;
    }

    /// <summary>Records a change to an open document.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <param name="text">Its new text.</param>
    /// <param name="version">The editor's new version, when it sent one.</param>
    /// <returns>The document as it now stands, or <see langword="null"/> when it is not open.</returns>
    public OpenDocument? Changed(string uri, string text, int? version) {
        if (!_byUri.TryGetValue(uri, out OpenDocument? current)) {
            return null;
        }

        OpenDocument document = new(uri, text, version ?? current.Version + 1);
        _byUri[uri] = document;
        return document;
    }

    /// <summary>Forgets a document the editor closed.</summary>
    /// <param name="uri">The document's URI.</param>
    /// <returns>Whether it was open.</returns>
    public bool Closed(string uri) => _byUri.Remove(uri);
}
