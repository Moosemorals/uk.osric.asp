// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>Installs the pages the shell ships with into a store that is missing them.</summary>
internal static class PageAssetSeeder {

    private static readonly PageSeeds s_pack = new(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));

    /// <summary>Installs the seeds a store is missing.</summary>
    /// <param name="pages">The store to seed.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="cancellationToken">Bounds the reads and the writes.</param>
    /// <remarks>
    /// A key the store already has is left alone, whatever its source says: an edit survives a
    /// restart, and a tombstone stays a tombstone. A create is the one automatic seed write, so it
    /// records the seed's hash as the page's anchor.
    /// </remarks>
    public static async Task SeedAsync(IPageStore pages, PageSeeds seeds, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);

        foreach (SeedFile seed in seeds.Files) {
            if (await pages.GetHeadAsync(seed.Path, cancellationToken) is null) {
                await pages.SavePageAsync(Stored(seed), cancellationToken);
            }
        }
    }

    /// <summary>The page keys of the pack this build carries, in seeding order.</summary>
    public static IReadOnlyList<PagePath> SeedPaths() => s_pack.Paths;

    // The kind is generated here rather than derived from a compile: a seeded library has to be known
    // as one before any page that names it is compiled, and only the tokens say so.
    private static PageTemplate Stored(SeedFile seed) =>
        new(seed.Path, seed.Source) {
            Kind = TemplateSourceGenerator.Generate(seed.Source).Kind,
            SeedHash = seed.Hash,
        };
}
