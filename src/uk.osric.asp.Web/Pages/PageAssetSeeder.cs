// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>Seeds the pages the shell ships with, and reads them back for a restore.</summary>
internal static class PageAssetSeeder {

    /// <summary>Installs the seeds a store is missing, and replaces one written against a type the page contract no longer has.</summary>
    /// <param name="pages">The store to seed.</param>
    /// <param name="cancellationToken">Bounds the reads and the writes.</param>
    /// <returns>The seeds it replaced, so the host can say which of them it rewrote.</returns>
    public static async Task<IReadOnlyList<PagePath>> SeedAsync(IPageStore pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        List<PagePath> replaced = [];
        foreach (string seed in SeedFiles()) {
            PagePath key = KeyOf(seed);
            string template = await File.ReadAllTextAsync(seed, cancellationToken);

            if (await pages.GetHeadAsync(key, cancellationToken) is null) {
                await pages.SavePageAsync(Stored(key, template), cancellationToken);
                continue;
            }

            // The document builder left the page contract for the shell, so a stored layout or editor
            // seed that names it can no longer compile: a broken layout leaves every page unwrapped
            // and a broken editor leaves nothing to edit with. The shipped version replaces it once,
            // and the revision it replaced stays in the page's history.
            if (await pages.GetPageAsync(key, cancellationToken) is { } stored
                && NamesRemovedType(stored.Template)
                && !string.Equals(stored.Template, template, StringComparison.Ordinal)) {
                await pages.SavePageAsync(Stored(key, template), cancellationToken);
                replaced.Add(key);
            }
        }

        return replaced;
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

    // The builder is `Web/Html/ShellDocument.cs` now; nothing a page can see is called `PageDocument`.
    private static bool NamesRemovedType(string template) =>
        template.Contains("PageDocument", StringComparison.Ordinal);

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
