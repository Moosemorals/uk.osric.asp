// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;

public sealed class SqliteStorage(StorageDbContext database) : IStorage {
    public async Task<IEnumerable<string>> ListFilesAsync(CancellationToken cancellationToken = default) {
        return await database.Set<StoredFile>().AsNoTracking()
            .Select(file => file.Path).ToListAsync(cancellationToken);
    }

    public Task<string?> GetFileContentsAsync(string path, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return database.Set<StoredFile>().AsNoTracking()
            .Where(file => file.Path == path).Select(file => file.Content)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetFileContentsAsync(string path, string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        StoredFile? file = await database.Set<StoredFile>()
            .SingleOrDefaultAsync(file => file.Path == path, cancellationToken);
        if (file is null) {
            database.Add(new StoredFile { Path = path, Content = content });
        } else {
            file.Content = content;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
