// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

public sealed class StoredFile {
    public required string Path { get; init; }

    public required string Content { get; set; }
}
