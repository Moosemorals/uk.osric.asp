// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Core;

public interface IStorage {
    Task<string?> GetFileContentsAsync(PagePath path, CancellationToken cancellationToken = default);

    Task SetFileContentsAsync(PagePath path, string content, CancellationToken cancellationToken = default);
}
