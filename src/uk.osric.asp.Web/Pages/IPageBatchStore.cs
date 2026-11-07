// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One page of a batch: the revision to store and the state to merge with what it holds.</summary>
/// <param name="Template">The revision, already built with a fresh id, timestamp and hash.</param>
/// <param name="State">The archive's state values; keys the page already has that these do not name are kept.</param>
internal sealed record PageBatchSave(PageTemplate Template, IReadOnlyList<PageStateValue> State);

/// <summary>What a batch did.</summary>
/// <param name="Refused">The pages whose merged state would not fit; empty when the batch committed.</param>
internal sealed record PageBatchResult(IReadOnlyList<PagePath> Refused) {

    /// <summary>A result for a batch that stored every page.</summary>
    public static PageBatchResult Committed { get; } = new([]);

    /// <summary>Whether every page in the batch is stored.</summary>
    public bool IsCommitted => Refused.Count == 0;
}

/// <summary>Writes a whole import in one transaction: all of it, or none of it.</summary>
internal interface IPageBatchStore {

    /// <summary>Stores every page in one transaction.</summary>
    /// <param name="pages">The pages to store, in key order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see cref="PageBatchResult.Committed"/>, or the pages whose merged state would overflow a cap.
    /// A refusal writes nothing.
    /// </returns>
    Task<PageBatchResult> SaveBatchAsync(IReadOnlyList<PageBatchSave> pages, CancellationToken cancellationToken = default);
}
