// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>Counts the compilations the running host performs, then delegates to the real compiler.</summary>
internal sealed class RecordingCompiler(IPageCompiler inner) : IPageCompiler {

    private int _compileCount;

    public int CompileCount => Volatile.Read(ref _compileCount);

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref _compileCount);
        return inner.Compile(source, libraries, cancellationToken);
    }

    public CompileResult CompileLibrary(GeneratedSource source, CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref _compileCount);
        return inner.CompileLibrary(source, cancellationToken);
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        return inner.Load(image, symbols);
    }
}
