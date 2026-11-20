// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

// The editor's half of the one check: a Language Server Protocol server on stdio, so VS Code shows a
// template's diagnostics while it is typed. The compiler, the generator and the policy are the
// product's own; the only thing this project adds is the protocol.

using AspLanguageServer;
using uk.osric.asp.Runtime;

// usage: asp-language-server [--seeds <folder>]
// The seeds are the repository's, because an editor has no page store: the client passes its
// workspace's folder, and the default is where a checkout keeps them.
string seedsFolder = Path.Combine(
    Directory.GetCurrentDirectory(), "src", "uk.osric.asp.Web", "Pages", "Seeds");

for (int index = 0; index < args.Length; index++) {
    if (string.Equals(args[index], "--seeds", StringComparison.Ordinal) && index + 1 < args.Length) {
        seedsFolder = args[index + 1];
        index++;
    }
}

PageCompiler compiler = new();
SeedSources seeds = SeedSources.Load(seedsFolder, compiler);
using Stream input = Console.OpenStandardInput();
using Stream output = Console.OpenStandardOutput();
LanguageServer server = new(
    new StreamTransport(input, output),
    new CompilerPageChecker(compiler, seeds.Libraries),
    seeds);

using CancellationTokenSource stopping = new();
Console.CancelKeyPress += (_, eventArgs) => {
    eventArgs.Cancel = true;
    stopping.Cancel();
};

return await server.RunAsync(stopping.Token);
