// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

// Compiles page templates with the product's own compiler and prints one line per diagnostic in the
// file:line:column shape the `check pages` task's problem matcher reads. A template with no code is
// skipped, because the host never compiles one either.
if (args.Length == 0) {
    Console.Error.WriteLine("usage: check-pages <file-or-directory>...");
    return 2;
}

PageCompiler compiler = new();
bool failed = false;
foreach (string file in Files(args)) {
    string template = File.ReadAllText(file);
    TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);
    if (!generated.HasCode) {
        continue;
    }

    IReadOnlyList<PageDiagnostic> diagnostics = generated.Source is { } source
        ? compiler.Compile(source).Diagnostics
        : generated.Errors;
    foreach (PageDiagnostic diagnostic in PageDiagnostic.SortedForDisplay(diagnostics)) {
        string severity = diagnostic.Severity == PageDiagnosticSeverity.Error ? "error" : "warning";
        TemplatePosition position = diagnostic.Position ?? new TemplatePosition(1, 1);
        Console.WriteLine($"{file}:{position.Line}:{position.Column}: {severity} {diagnostic.Message}");
        failed |= diagnostic.Severity == PageDiagnosticSeverity.Error;
    }
}

return failed ? 1 : 0;

// A directory contributes the files directly inside it, the way PageAssetSeeder reads a seed folder.
static IEnumerable<string> Files(IEnumerable<string> paths) {
    foreach (string path in paths) {
        if (!Directory.Exists(path)) {
            yield return path;
            continue;
        }

        string[] files = Directory.GetFiles(path);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files) {
            yield return file;
        }
    }
}
