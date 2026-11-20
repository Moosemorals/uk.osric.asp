// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Web.Editor;

/// <summary>Applies the seeds a release moved, so a deploy finishes without the owner opening a page.</summary>
/// <remarks>
/// <para>
/// The one automatic write the store already permits is the row whose page still holds the seed it was
/// installed from and whose shipped seed has moved — *release newer*, the one verdict the seed page's
/// gate marks <c>CanApply</c>. Everything else needs a tick on that page, so only that row is a
/// candidate here.
/// </para>
/// <para>
/// This runs on every start and remembers nothing. After a write the row is in sync, so a second start
/// finds nothing to do; a marker would only add a way for the step to be skipped. A page the owner
/// edited is never a candidate, which is what makes that the way to hold a source the release moved on
/// from.
/// </para>
/// </remarks>
internal static class PageSeedApplier {

    /// <summary>Writes every seed whose page still holds the seed it was installed from.</summary>
    /// <param name="pages">The store, through the editor's save.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="logger">Where each written row is reported.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns>What each candidate's write did, in the pack's order.</returns>
    /// <remarks>
    /// A row that fails is reported and the rest still run: one broken seed is not a reason to leave
    /// every other one behind.
    /// </remarks>
    public static async Task<IReadOnlyList<PageSeedInstaller.SeedResult>> ApplyAsync(
        PageEditor pages,
        PageSeeds seeds,
        ILogger logger,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(logger);

        IReadOnlyList<PageSeedInstaller.SeedPlan> plan = await PageSeedInstaller.PlanAsync(pages, seeds, cancellationToken);
        List<PageSeedInstaller.SeedResult> results = [];

        foreach (PageSeedInstaller.SeedPlan seed in plan.Where(IsMovedSeed)) {
            try {
                PageSeedInstaller.SeedResult result = await PageSeedInstaller.InstallAsync(pages, seed, cancellationToken);
                results.Add(result);
                if (result.Outcome == PageSeedInstaller.SeedOutcome.WrittenWithErrors) {
                    logger.LogWarning(
                        "Wrote the release's seed of {Path} as revision {Hash}, which has compiler errors",
                        seed.Path.Key,
                        result.Hash);
                } else {
                    logger.LogInformation(
                        "Wrote the release's seed of {Path} as revision {Hash}", seed.Path.Key, result.Hash);
                }
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogError(exception, "Writing the release's seed of {Path} failed", seed.Path.Key);
                results.Add(new PageSeedInstaller.SeedResult(seed.Path, seed.Summary, PageSeedInstaller.SeedOutcome.Failed));
            }
        }

        return results;
    }

    // A key with no head is the create-seeder's, and `PlanAsync` offers it as applicable because
    // installing would create it; this step only ever replaces a page that is already there.
    private static bool IsMovedSeed(PageSeedInstaller.SeedPlan seed) =>
        seed.Summary.CanApply && seed.Head is not null;
}
