// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The method an outbound fetch uses.</summary>
public enum PageFetchMethod {
    /// <summary>A request with no body.</summary>
    Get,

    /// <summary>A request that may carry a text body.</summary>
    Post,
}
