// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

internal sealed class SqlitePageStore(StorageDbContext database) : IPageStore {

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        StoredFile? file = await database.Set<StoredFile>().AsNoTracking()
            .SingleOrDefaultAsync(file => file.Path == path.Key, cancellationToken);

        return file is null ? null : ToPage(file);
    }

    public async Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);

        PageCompilation? compilation = page.Compilation;
        string? diagnostics = compilation is null ? null : SerializeDiagnostics(compilation.Diagnostics);

        // One statement, so a save that lands while another is in flight updates the row instead of
        // colliding with it: a read followed by an insert had a window where both saw no row, and
        // the second insert failed the primary key. The row is written whole, so the image always
        // belongs to the content beside it.
        await database.Database.ExecuteSqlAsync($"""
            INSERT INTO "Files" ("Path", "Content", "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
            VALUES ({page.Path.Key}, {page.Content}, {compilation?.ContentHash}, {compilation?.RuntimeId}, {compilation?.Image}, {compilation?.Symbols}, {diagnostics})
            ON CONFLICT ("Path") DO UPDATE SET
                "Content" = excluded."Content",
                "ContentHash" = excluded."ContentHash",
                "RuntimeId" = excluded."RuntimeId",
                "Image" = excluded."Image",
                "Symbols" = excluded."Symbols",
                "Diagnostics" = excluded."Diagnostics"
            """, cancellationToken);
    }

    public async Task<bool> TrySetCompilationAsync(PagePath path, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(compilation);

        // The content predicate is the point: a page saved while its previous version was compiling
        // must keep the newer source instead of being overwritten by a result for the older one.
        int updated = await database.Set<StoredFile>()
            .Where(file => file.Path == path.Key && file.Content == expectedContent)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(file => file.ContentHash, compilation.ContentHash)
                .SetProperty(file => file.RuntimeId, compilation.RuntimeId)
                .SetProperty(file => file.Image, compilation.Image)
                .SetProperty(file => file.Symbols, compilation.Symbols)
                .SetProperty(file => file.Diagnostics, SerializeDiagnostics(compilation.Diagnostics)), cancellationToken);

        return updated > 0;
    }

    private static StoredPage ToPage(StoredFile file) {
        return new StoredPage(PagePath.Normalize(file.Path), file.Content, ToCompilation(file));
    }

    private static PageCompilation? ToCompilation(StoredFile file) {
        if (file.RuntimeId is null) {
            return null;
        }

        return new PageCompilation(
            file.ContentHash ?? string.Empty,
            file.RuntimeId,
            file.Image,
            file.Symbols,
            Deserialize(file.Diagnostics, file.Image is not null));
    }

    private static IReadOnlyList<PageDiagnostic> Deserialize(string? json, bool hasImage) {
        if (json is null) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<PageDiagnostic[]>(json, s_jsonOptions) ?? [];
        } catch (JsonException) {
            // Rows written before diagnostics carried a severity stored a plain string array. The
            // image is the only clue to what they meant: no image means they were errors.
            string[]? legacy = JsonSerializer.Deserialize<string[]>(json);
            if (legacy is null) {
                return [];
            }

            PageDiagnosticSeverity severity = hasImage
                ? PageDiagnosticSeverity.Warning
                : PageDiagnosticSeverity.Error;
            return [.. legacy.Select(message => new PageDiagnostic(severity, null, message))];
        }
    }

    private static string SerializeDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) {
        return JsonSerializer.Serialize(diagnostics, s_jsonOptions);
    }
}
