// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

public interface IStorage {
    Task<IEnumerable<string>> ListFilesAsync(CancellationToken cancellationToken = default);

    Task<string?> GetFileContentsAsync(string path, CancellationToken cancellationToken = default);

    Task SetFileContentsAsync(string path, string content, CancellationToken cancellationToken = default);
}
