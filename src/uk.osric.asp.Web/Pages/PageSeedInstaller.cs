// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;

/// <summary>The rule for comparing a bundled seed with the live head, and for installing it.</summary>
/// <remarks>
/// The seed page asks the same questions of the same pack — what does the store hold, and what would
/// writing the seed do — so it asks them here. Every write goes through
/// <see cref="PageEditor.SaveSeedAsync"/>, which records the seed hash as the page's anchor.
/// </remarks>
internal static class PageSeedInstaller {

    /// <summary>What one seed's install did.</summary>
    internal enum SeedOutcome {
        Unchanged,
        Written,
        WrittenWithErrors,
        Adopted,
        NotTicked,
        Stale,
        Failed,
    }

    /// <summary>One seed, its bundled source, and what the store holds now.</summary>
    /// <param name="Path">The page's key.</param>
    /// <param name="Source">The bundled source.</param>
    /// <param name="SeedHash">The hash of the bundled source.</param>
    /// <param name="Summary">The verdict and the actions it permits.</param>
    /// <param name="HoldsSeed">Whether the live content already equals the bundled source.</param>
    /// <param name="Head">The head the plan compared, or null when the store has no page at the key.</param>
    internal sealed record SeedPlan(
        PagePath Path,
        string Source,
        string SeedHash,
        PageSeedSummary Summary,
        bool HoldsSeed,
        PageHeadSummary? Head);

    /// <summary>One seed and the outcome of trying to install it.</summary>
    /// <param name="Path">The page's key.</param>
    /// <param name="Was">What the store held before.</param>
    /// <param name="Outcome">What this run did.</param>
    /// <param name="Hash">The revision it stored, or the current head when nothing was written.</param>
    internal sealed record SeedResult(PagePath Path, PageSeedSummary Was, SeedOutcome Outcome, string? Hash = null);

    /// <summary>Reads every seed and compares it with the live head.</summary>
    /// <param name="pages">The page store.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<IReadOnlyList<SeedPlan>> PlanAsync(
        PageEditor pages,
        PageSeeds seeds,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);

        List<SeedPlan> plan = [];

        foreach (SeedFile seed in seeds.Files) {
            PageHeadSummary? head = await pages.GetHeadAsync(seed.Path, cancellationToken);

            if (head is null) {
                plan.Add(new SeedPlan(seed.Path, seed.Source, seed.Hash, SeedVerdictWords.New(), HoldsSeed: false, Head: null));
                continue;
            }

            if (head.IsDeleted) {
                plan.Add(new SeedPlan(seed.Path, seed.Source, seed.Hash, SeedVerdictWords.Deleted(), HoldsSeed: false, Head: head));
                continue;
            }

            SeedComparison comparison = SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash);
            plan.Add(new SeedPlan(
                seed.Path,
                seed.Source,
                seed.Hash,
                comparison.Describe(seed.Hash),
                HoldsSeed: string.Equals(head.ContentHash, seed.Hash, StringComparison.Ordinal),
                Head: head));
        }

        return plan;
    }

    /// <summary>Installs one seed through the ordinary saver, recording the seed it installed.</summary>
    /// <param name="pages">The page store.</param>
    /// <param name="seed">The plan entry to write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>A save with errors is still stored, so the outcome says so rather than failing; an exception is the caller's to catch.</remarks>
    public static async Task<SeedResult> InstallAsync(PageEditor pages, SeedPlan seed, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seed);

        SavedRevision saved = await pages.SaveSeedAsync(seed.Path, seed.Source, seed.SeedHash, cancellationToken);
        SeedOutcome outcome = HasErrors(saved.Diagnostics) ? SeedOutcome.WrittenWithErrors : SeedOutcome.Written;

        return new SeedResult(seed.Path, seed.Summary, outcome, saved.Revision.Hash);
    }

    private static bool HasErrors(IReadOnlyList<Core.Compilation.PageDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Severity == Core.Compilation.PageDiagnosticSeverity.Error);
}
