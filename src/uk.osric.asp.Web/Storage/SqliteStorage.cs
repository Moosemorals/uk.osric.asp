// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core;

public sealed class SqliteStorage(StorageDbContext database) : IStorage {
    public Task<string?> GetFileContentsAsync(PagePath path, CancellationToken cancellationToken = default) {

        return database.Set<StoredFile>().AsNoTracking()
            .Where(file => file.Path == path.Path).Select(file => file.Content)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetFileContentsAsync(PagePath path, string content, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(content);

        StoredFile? file = await database.Set<StoredFile>()
            .SingleOrDefaultAsync(file => file.Path == path.Path, cancellationToken);
        if (file is null) {
            database.Add(new StoredFile { Path = path.Path, Content = content });
        } else {
            file.Content = content;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
