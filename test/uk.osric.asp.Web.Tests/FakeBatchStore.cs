// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>A batch store that records what an import asked it to save.</summary>
internal sealed class FakeBatchStore : IPageBatchStore {

    /// <summary>The batches that committed, in call order.</summary>
    public List<PageBatchSave> Saved { get; } = [];

    /// <summary>Pages to refuse, so a fixture can prove a cap refusal writes nothing.</summary>
    public IReadOnlyList<PagePath> Refuse { get; set; } = [];

    /// <summary>An exception to throw instead of writing.</summary>
    public Exception? Failure { get; set; }

    public int CallCount { get; private set; }

    public Task<PageBatchResult> SaveBatchAsync(IReadOnlyList<PageBatchSave> pages, CancellationToken cancellationToken = default) {
        CallCount++;

        if (Failure is not null) {
            throw Failure;
        }

        if (Refuse.Count > 0) {
            return Task.FromResult(new PageBatchResult(Refuse));
        }

        Saved.AddRange(pages);
        return Task.FromResult(PageBatchResult.Committed);
    }
}
