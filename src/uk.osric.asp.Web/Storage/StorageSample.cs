// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

/// <summary>What one sample finds: the bytes each shape holds and how many tables a source declared.</summary>
/// <remarks>
/// The bytes count the text and blobs a row stores, not the indexes and not SQLite's own overhead, so
/// the figure answers "what would this cost somewhere else" rather than "how big is the file". A
/// declared table's rows live in a table whose name the sample cannot enumerate, so the declared
/// figure is the catalogue's count rather than the rows' bytes.
/// </remarks>
/// <param name="Revisions">The stored source, compiled image and symbols a revision holds.</param>
/// <param name="Values">The subject, keys, JSON values and stamps of <c>StoreValues</c>.</param>
/// <param name="Messages">The addresses, body and stamps of <c>StoreMessages</c>.</param>
/// <param name="Objects">The digest, content type, BLOB and stamps of <c>StoreObjects</c>.</param>
/// <param name="Secrets">The name, text and stamp of <c>StoreSecrets</c>.</param>
/// <param name="DeclaredTables">How many tables the page store's sources declare.</param>
internal readonly record struct StorageSample(
    long Revisions,
    long Values,
    long Messages,
    long Objects,
    long Secrets,
    long DeclaredTables) {

    /// <summary>The logical bytes every shape holds, which is what the payload figure reports.</summary>
    public long Payload => Revisions + Values + Messages + Objects + Secrets;
}
