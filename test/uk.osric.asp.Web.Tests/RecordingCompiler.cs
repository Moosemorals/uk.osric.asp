// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>Counts compilations while delegating to the real compiler.</summary>
internal sealed class RecordingCompiler(IPageCompiler inner) : IPageCompiler {

    public int CompileCount { get; private set; }

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) {
        CompileCount++;
        return inner.Compile(source, cancellationToken);
    }

    public CompileResult CompileLibrary(GeneratedSource source, CancellationToken cancellationToken = default) {
        CompileCount++;
        return inner.CompileLibrary(source, cancellationToken);
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        return inner.Load(image, symbols);
    }
}
