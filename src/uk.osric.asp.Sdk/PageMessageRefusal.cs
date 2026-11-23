// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Why a message was not queued, or why a take or peek answered nothing.</summary>
/// <remarks>
/// A refused destination and a full queue depend on the page store rather than on the page's code, so
/// they are values a page branches on. A body over its limit and a lifetime the subject cannot carry
/// are page bugs and throw instead.
/// </remarks>
public enum PageMessageRefusal {

    /// <summary>Nothing was refused: the message was queued, or one was waiting.</summary>
    None,

    /// <summary>The queue held no message for this reader.</summary>
    Empty,

    /// <summary>The key cannot name a page.</summary>
    NotAddressable,

    /// <summary>The site's own routes answer the key, so no page is stored there.</summary>
    OwnedBySite,

    /// <summary>No live page is stored at the key.</summary>
    UnknownPage,

    /// <summary>The key holds a library or a content-only page, which can never take a message.</summary>
    NotASink,

    /// <summary>The reader is at their message count or byte limit, so nothing was queued.</summary>
    Full,
}
