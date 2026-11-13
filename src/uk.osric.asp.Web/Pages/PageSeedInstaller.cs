// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;

/// <summary>The rule for comparing a bundled seed with the live head, and for installing it.</summary>
/// <remarks>
/// The emergency restore and the editor page's install action ask the same two questions — does this
/// seed differ, and what happened when it was written — so they ask them here. Every write goes
/// through <see cref="PageEditor.SaveAsync"/>, the one compile-then-store path.
/// </remarks>
internal static class PageSeedInstaller {

    /// <summary>What the live head holds relative to the bundled source.</summary>
    public enum SeedState {
        Current,
        Changed,
        Missing,
        Deleted,
    }

    /// <summary>What one seed's install did.</summary>
    public enum SeedOutcome {
        Unchanged,
        Restored,
        SavedWithErrors,
        Failed,
    }

    /// <summary>One seed, its bundled source and what the store holds now.</summary>
    public sealed record SeedPlan(PagePath Path, string Source, SeedState State);

    /// <summary>One seed and the outcome of trying to install it.</summary>
    /// <param name="Path">The page's key.</param>
    /// <param name="Was">What the store held before.</param>
    /// <param name="Outcome">What this run did.</param>
    /// <param name="Hash">The revision it stored, or the current head when nothing was written.</param>
    public sealed record SeedResult(PagePath Path, SeedState Was, SeedOutcome Outcome, string? Hash = null);

    /// <summary>The pages this release ships with.</summary>
    public static IReadOnlyList<PagePath> SeedPaths() => PageAssetSeeder.SeedPaths();

    /// <summary>Reads every seed and compares it with the live head.</summary>
    /// <param name="pages">The page store.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<IReadOnlyList<SeedPlan>> PlanAsync(PageEditor pages, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);

        List<SeedPlan> plan = [];

        foreach (PagePath path in SeedPaths()) {
            string? source = await PageAssetSeeder.ReadSeedTemplateAsync(path, cancellationToken);
            if (source is null) {
                continue;
            }

            PageHeadSummary? head = await pages.GetHeadAsync(path, cancellationToken);
            if (head is null) {
                plan.Add(new SeedPlan(path, source, SeedState.Missing));
                continue;
            }

            if (head.IsDeleted) {
                plan.Add(new SeedPlan(path, source, SeedState.Deleted));
                continue;
            }

            PageTemplate? live = await pages.GetAsync(path, cancellationToken);
            SeedState state = PageSourceChange.IsNoOp(live?.Template, source)
                ? SeedState.Current
                : SeedState.Changed;
            plan.Add(new SeedPlan(path, source, state));
        }

        return plan;
    }

    /// <summary>Installs one seed through the ordinary saver.</summary>
    /// <param name="pages">The page store.</param>
    /// <param name="seed">The plan entry to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>A save with errors is still stored, so the outcome says so rather than failing; an exception is the caller's to catch.</remarks>
    public static async Task<SeedResult> InstallAsync(PageEditor pages, SeedPlan seed, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seed);

        SavedRevision saved = await pages.SaveAsync(seed.Path, seed.Source, cancellationToken);
        SeedOutcome outcome = HasErrors(saved.Diagnostics) ? SeedOutcome.SavedWithErrors : SeedOutcome.Restored;

        return new SeedResult(seed.Path, seed.State, outcome, saved.Revision.Hash);
    }

    private static bool HasErrors(IReadOnlyList<PageDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);
}
