// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

// Compiles page templates with the product's own compiler and prints one line per diagnostic in the
// file:line:column shape the `check pages` task's problem matcher reads. A template with no code is
// skipped, because the host never compiles one either.
if (args.Length == 0) {
    Console.Error.WriteLine("usage: check-pages <file-or-directory>...");
    return 2;
}

PageCompiler compiler = new();
List<(string File, PageKind Kind, GeneratedSource? Source, IReadOnlyList<PageDiagnostic> Errors)> templates = [];
List<(string Key, string File)> files = [];
List<LibrarySource> libraries = [];
foreach ((string file, string key) in Files(args)) {
    string template = File.ReadAllText(file);
    TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);

    // A library's generated source is not a page's: it has no GeneratedPage scaffold and may declare
    // a namespace.
    GeneratedSource? source = generated.Kind is PageKind.Library
        ? TemplateSourceGenerator.GenerateLibrary(template).Source
        : generated.Source;

    templates.Add((file, generated.Kind, source, generated.Errors));
    files.Add((key, file));

    // Every page is compiled with every library in the same run, exactly as the host offers the
    // libraries one installation holds.
    if (generated.Kind is PageKind.Library && source is not null) {
        libraries.Add(new LibrarySource(key, source));
    }
}

bool failed = false;
// A library is compiled into every page that reaches it, so the same diagnostic can arrive once per
// dependent; the file it names is printed once.
HashSet<string> printed = [];
foreach ((string file, PageKind kind, GeneratedSource? source, IReadOnlyList<PageDiagnostic> errors) in templates) {
    if (kind is PageKind.Text) {
        continue;
    }

    IReadOnlyList<PageDiagnostic> diagnostics = source is null
        ? errors
        : kind is PageKind.Library
            ? compiler.CompileLibrary(source).Diagnostics
            : compiler.Compile(source, libraries).Diagnostics;
    foreach (PageDiagnostic diagnostic in PageDiagnostic.SortedForDisplay(diagnostics)) {
        string severity = diagnostic.Severity == PageDiagnosticSeverity.Error ? "error" : "warning";
        TemplatePosition position = diagnostic.Position ?? new TemplatePosition(1, 1);

        // A diagnostic from a library names the library's file rather than the page that reached it.
        string reported = diagnostic.Source is { } libraryKey
            ? files.FirstOrDefault(item => item.Key == libraryKey).File ?? file
            : file;
        string line = $"{reported}:{position.Line}:{position.Column}: {severity} {diagnostic.Message}";
        if (printed.Add(line)) {
            Console.WriteLine(line);
        }

        failed |= diagnostic.Severity == PageDiagnosticSeverity.Error;
    }
}

return failed ? 1 : 0;

// Every file under a directory, the way PageAssetSeeder reads a seed folder, with the page key it
// would be stored under.
static IEnumerable<(string File, string Key)> Files(IEnumerable<string> paths) {
    foreach (string path in paths) {
        if (!Directory.Exists(path)) {
            yield return (path, "/" + Path.GetFileName(path));
            continue;
        }

        string[] found = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        Array.Sort(found, StringComparer.Ordinal);
        foreach (string file in found) {
            yield return (file, "/" + Path.GetRelativePath(path, file).Replace('\\', '/'));
        }
    }
}
