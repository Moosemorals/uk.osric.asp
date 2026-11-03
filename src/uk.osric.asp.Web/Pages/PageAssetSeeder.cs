// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

internal static class PageAssetSeeder {

    public static async Task SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        string[] seeds = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));
        Array.Sort(seeds, StringComparer.Ordinal);

        foreach (string seed in seeds) {
            PagePath key = PagePath.Normalize("/" + Path.GetFileName(seed));
            if (await pages.GetHeadAsync(key, cancellationToken) is not null) {
                continue;
            }

            string content = await File.ReadAllTextAsync(seed, cancellationToken);
            await pages.SavePageAsync(new StoredPage(key, content), cancellationToken);
        }
    }
}
