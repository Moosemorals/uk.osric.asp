// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>The pages this release ships, read from the seed folder once per process.</summary>
/// <remarks>
/// <para>
/// The pack is a folder beside the assembly, because that is where the build copies
/// <c>Pages/Seeds</c>. Reading it once here removes the directory walk every
/// <c>SeedSourceAsync</c> lookup used to do, and gives the seeder, the page list, the shell's
/// restore and the <c>seeds</c> command one account of what this release carries.
/// </para>
/// <para>
/// A key is the file's path under the folder, so a seed in a subtree is seeded at its subtree's key:
/// <c>_lib/document</c> is <c>/_lib/document</c>.
/// </para>
/// </remarks>
internal sealed class PageSeeds {

    private readonly Dictionary<string, SeedFile> _byKey;

    /// <summary>Reads every seed in a folder.</summary>
    /// <param name="directory">The folder the build copies the seed files into.</param>
    /// <exception cref="ArgumentException">The folder is blank.</exception>
    public PageSeeds(string directory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        List<SeedFile> seeds = [];
        _byKey = [];
        foreach (string file in files) {
            string name = Path.GetRelativePath(directory, file).Replace('\\', '/');
            PagePath key = PagePath.Normalize("/" + name);
            string source = File.ReadAllText(file);
            SeedFile seed = new(key, name, source, TemplateFingerprint.Of(source));
            seeds.Add(seed);
            _byKey[key.Key] = seed;
        }

        Files = seeds;
        Paths = [.. seeds.Select(seed => seed.Path)];
    }

    /// <summary>Every seed, in ordinal key order.</summary>
    public IReadOnlyList<SeedFile> Files { get; }

    /// <summary>Every seed's key, in ordinal order.</summary>
    public IReadOnlyList<PagePath> Paths { get; }

    /// <summary>Whether this release ships a seed for a key.</summary>
    /// <param name="path">The canonical key.</param>
    public bool Contains(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        return _byKey.ContainsKey(path.Key);
    }

    /// <summary>One seed, or null when this release does not ship it.</summary>
    /// <param name="path">The canonical key.</param>
    public SeedFile? Find(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        return _byKey.TryGetValue(path.Key, out SeedFile? seed) ? seed : null;
    }

    /// <summary>The source this release ships for a key, or null when it ships none.</summary>
    /// <param name="path">The canonical key.</param>
    public string? Source(PagePath path) => Find(path)?.Source;
}

/// <summary>One seed: its key, its path under the seed folder, its exact source and its hash.</summary>
/// <param name="Path">The canonical page key the seed installs.</param>
/// <param name="Name">The file's path under the seed folder, with <c>/</c> separators.</param>
/// <param name="Source">The file's text, exactly as it will be stored.</param>
/// <param name="Hash">The content hash of <paramref name="Source"/>, which is what a seed write records.</param>
internal sealed record SeedFile(PagePath Path, string Name, string Source, string Hash);
