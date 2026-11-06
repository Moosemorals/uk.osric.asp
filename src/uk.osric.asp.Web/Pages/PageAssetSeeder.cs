// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Seeds the pages the shell ships with, and reads them back for a restore.</summary>
internal static class PageAssetSeeder {

    public static async Task SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        foreach (string seed in SeedFiles()) {
            PagePath key = PagePath.Normalize("/" + Path.GetFileName(seed));
            if (await pages.GetHeadAsync(key, cancellationToken) is not null) {
                continue;
            }

            string content = await File.ReadAllTextAsync(seed, cancellationToken);
            await pages.SavePageAsync(new StoredPage(key, content), cancellationToken);
        }
    }

    public static async Task<string?> ReadSeedContentAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        string? seed = FindSeedFile(path);
        return seed is null ? null : await File.ReadAllTextAsync(seed, cancellationToken);
    }

    private static string[] SeedFiles() {
        string[] seeds = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));
        Array.Sort(seeds, StringComparer.Ordinal);
        return seeds;
    }

    private static string? FindSeedFile(PagePath path) {
        if (path.Key.Length <= 1 || path.Key.AsSpan(1).Contains('/')) {
            return null;
        }

        foreach (string seed in SeedFiles()) {
            if (string.Equals(
                PagePath.Normalize("/" + Path.GetFileName(seed)).Key,
                path.Key,
                StringComparison.Ordinal)) {
                return seed;
            }
        }

        return null;
    }
}
