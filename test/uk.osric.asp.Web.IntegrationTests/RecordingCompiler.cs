// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using uk.osric.asp.Runtime;

/// <summary>Counts the compilations the running host performs, then delegates to the real compiler.</summary>
internal sealed class RecordingCompiler(ITemplateCompiler inner) : ITemplateCompiler {

    private int _compileCount;

    public int CompileCount => Volatile.Read(ref _compileCount);

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(string source, CancellationToken cancellationToken = default) {
        Interlocked.Increment(ref _compileCount);
        return inner.Compile(source, cancellationToken);
    }

    public CompiledTemplate Load(byte[] image) {
        return inner.Load(image);
    }
}
