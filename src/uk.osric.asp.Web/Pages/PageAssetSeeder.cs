// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>Seeds the pages the shell ships with, and reads them back for a restore.</summary>
internal static class PageAssetSeeder {

    /// <summary>Installs the seeds a store is missing.</summary>
    /// <param name="pages">The store to seed.</param>
    /// <param name="cancellationToken">Bounds the reads and the writes.</param>
    /// <remarks>
    /// A key the store already has is left alone, whatever its source says: an edit survives a
    /// restart, and a tombstone stays a tombstone.
    /// </remarks>
    public static async Task SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        foreach (string seed in SeedFiles()) {
            PagePath key = KeyOf(seed);
            string template = await File.ReadAllTextAsync(seed, cancellationToken);

            if (await pages.GetHeadAsync(key, cancellationToken) is null) {
                await pages.SavePageAsync(Stored(key, template), cancellationToken);
            }
        }
    }

    public static async Task<string?> ReadSeedTemplateAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        string? seed = FindSeedFile(path);
        return seed is null ? null : await File.ReadAllTextAsync(seed, cancellationToken);
    }

    /// <summary>The page keys of the assets bundled with this release, in seeding order.</summary>
    public static IReadOnlyList<PagePath> SeedPaths() {
        return [.. SeedFiles().Select(KeyOf)];
    }

    private static string SeedsDirectory => Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds");

    // The kind is generated here rather than derived from a compile: a seeded library has to be known
    // as one before any page that names it is compiled, and only the tokens say so.
    private static PageTemplate Stored(PagePath key, string template) =>
        new(key, template) { Kind = TemplateSourceGenerator.Generate(template).Kind };

    // Every file under the seed folder, so a library can be seeded in a subtree of its own rather
    // than in the site's root key space.
    private static string[] SeedFiles() {
        string[] seeds = Directory.GetFiles(SeedsDirectory, "*", SearchOption.AllDirectories);
        Array.Sort(seeds, StringComparer.Ordinal);
        return seeds;
    }

    // A seed is served at the key its path under the seed folder names: `_layout` is `/_layout`, and
    // `_lib/document` is `/_lib/document`.
    private static PagePath KeyOf(string seedFile) =>
        PagePath.Normalize("/" + Path.GetRelativePath(SeedsDirectory, seedFile).Replace('\\', '/'));

    private static string? FindSeedFile(PagePath path) {
        if (path.Key.Length <= 1) {
            return null;
        }

        foreach (string seed in SeedFiles()) {
            if (string.Equals(KeyOf(seed).Key, path.Key, StringComparison.Ordinal)) {
                return seed;
            }
        }

        return null;
    }
}
