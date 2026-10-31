// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core;

public sealed class SqliteStorage(StorageDbContext database) : IStorage {

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        StoredFile? file = await database.Set<StoredFile>().AsNoTracking()
            .SingleOrDefaultAsync(file => file.Path == path.Path, cancellationToken);

        return file is null ? null : ToPage(file);
    }

    public async Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);

        StoredFile? file = await database.Set<StoredFile>()
            .SingleOrDefaultAsync(file => file.Path == page.Path.Path, cancellationToken);

        if (file is null) {
            file = new StoredFile { Path = page.Path.Path, Content = page.Content };
            database.Add(file);
        } else {
            file.Content = page.Content;
        }

        Apply(file, page.Compilation);

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TrySetCompilationAsync(PagePath path, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(compilation);

        // The content predicate is the point: a page saved while its previous version was compiling
        // must keep the newer source instead of being overwritten by a result for the older one.
        int updated = await database.Set<StoredFile>()
            .Where(file => file.Path == path.Path && file.Content == expectedContent)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(file => file.ContentHash, compilation.ContentHash)
                .SetProperty(file => file.RuntimeId, compilation.RuntimeId)
                .SetProperty(file => file.Image, compilation.Image)
                .SetProperty(file => file.Diagnostics, Serialize(compilation)), cancellationToken);

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

    private static void Apply(StoredFile file, PageCompilation? compilation) {
        file.ContentHash = compilation?.ContentHash;
        file.RuntimeId = compilation?.RuntimeId;
        file.Image = compilation?.Image;
        file.Diagnostics = compilation is null ? null : Serialize(compilation);
    }

    private static string Serialize(PageCompilation compilation) {
        return JsonSerializer.Serialize(compilation.Diagnostics, s_jsonOptions);
    }
}
