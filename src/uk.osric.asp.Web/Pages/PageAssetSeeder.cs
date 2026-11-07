// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Seeds the pages the shell ships with, and reads them back for a restore.</summary>
internal static class PageAssetSeeder {

    public static async Task SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        foreach (string seed in SeedFiles()) {
            PagePath key = KeyOf(seed);
            if (await pages.GetHeadAsync(key, cancellationToken) is not null) {
                continue;
            }

            string template = await File.ReadAllTextAsync(seed, cancellationToken);
            await pages.SavePageAsync(new PageTemplate(key, template), cancellationToken);
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

    private static string[] SeedFiles() {
        string[] seeds = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));
        Array.Sort(seeds, StringComparer.Ordinal);
        return seeds;
    }

    // A seed is served under its file name at the site root.
    private static PagePath KeyOf(string seedFile) =>
        PagePath.Normalize("/" + Path.GetFileName(seedFile));

    private static string? FindSeedFile(PagePath path) {
        if (path.Key.Length <= 1 || path.Key.AsSpan(1).Contains('/')) {
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
