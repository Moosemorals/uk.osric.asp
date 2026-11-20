// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace AspLanguageServer;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>The repository's seed files, as a compile's libraries and as files a diagnostic can name.</summary>
/// <remarks>
/// An editor has no page store, so the closure comes from the seeds the repository ships: a page that
/// names <c>/_lib/html</c> is compiled against that file, and a diagnostic in it is published against
/// the file rather than the page that reached it. The key a diagnostic carries is the key the seed
/// would be stored under, which is how the two are matched.
/// </remarks>
internal sealed class SeedSources {

    private readonly Dictionary<string, string> _files;

    private SeedSources(Dictionary<string, string> files, IReadOnlyList<LibrarySource> libraries) {
        _files = files;
        Libraries = libraries;
    }

    /// <summary>No files and no libraries, for a server given no seed folder.</summary>
    public static SeedSources Empty { get; } = new([], []);

    /// <summary>The library candidates a page's <c>using</c> directives choose from, in key order.</summary>
    public IReadOnlyList<LibrarySource> Libraries { get; }

    /// <summary>Reads a folder of seed files the way the seeder reads it.</summary>
    /// <param name="folder">The folder to read, or a path that does not exist.</param>
    /// <param name="compiler">The compiler that tells a library from a worker.</param>
    /// <returns>The files by key and the libraries among them; both are empty when the folder is absent.</returns>
    public static SeedSources Load(string folder, IPageCompiler compiler) {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(compiler);

        Dictionary<string, string> files = [];
        List<LibrarySource> libraries = [];
        if (!Directory.Exists(folder)) {
            // An editor opened over a checkout with no build yet still gets its own document checked;
            // it simply has no libraries to reach.
            return new SeedSources(files, libraries);
        }

        string[] found = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        Array.Sort(found, StringComparer.Ordinal);

        foreach (string file in found) {
            string key = "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');
            files[key] = file;

            string template;
            try {
                template = File.ReadAllText(file);
            } catch (IOException) {
                // A file being written by another process is not a reason to refuse to start.
                continue;
            }

            TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);
            if (generated.Kind is not PageKind.Library || generated.Source is null) {
                continue;
            }

            GeneratedSource library = TemplateSourceGenerator.GenerateLibrary(template).Source!;

            // A declaration-only source is a library or a worker, and only the compiler can say which.
            // A worker is a program a page starts, never something a page compiles against.
            if (compiler.CompileWorker(library, []).Kind is not PageKind.Worker) {
                libraries.Add(new LibrarySource(key, library));
            }
        }

        return new SeedSources(files, libraries);
    }

    /// <summary>The file URI a page key names, or <see langword="null"/> when the folder does not hold it.</summary>
    /// <param name="key">A page key, as a diagnostic's source names it.</param>
    public string? UriOf(string key) {
        ArgumentNullException.ThrowIfNull(key);

        return _files.TryGetValue(key, out string? file)
            ? new Uri(Path.GetFullPath(file)).AbsoluteUri
            : null;
    }
}
