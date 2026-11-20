// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Why a job call was refused, as a value a page can branch on.</summary>
public enum PageJobRefusal {

    /// <summary>Nothing was refused.</summary>
    None,

    /// <summary>The render is not signed in, and a job belongs to an account.</summary>
    Anonymous,

    /// <summary>The render is a <c>HEAD</c>, which reads and never writes.</summary>
    ReadOnly,

    /// <summary>The key is not an addressable page key.</summary>
    InvalidKey,

    /// <summary>The site's own routes answer the key, so no worker is stored there.</summary>
    OwnedBySite,

    /// <summary>No page is stored at the key.</summary>
    UnknownWorker,

    /// <summary>The key holds a page, a text asset or a library rather than a worker.</summary>
    NotAWorker,

    /// <summary>Every slot in the job queue is taken.</summary>
    QueueFull,

    /// <summary>No job with that id belongs to this visitor.</summary>
    UnknownJob,

    /// <summary>The job has already finished, so it cannot receive.</summary>
    Ended,

    /// <summary>The job's pending messages are at their limit.</summary>
    TooManyMessages,

    /// <summary>The message is larger than <c>Pages:Jobs:MaxMessageBytes</c>.</summary>
    MessageTooLarge,

    /// <summary>The credential that would start the job is no longer live, so nothing was started.</summary>
    Revoked,
}
