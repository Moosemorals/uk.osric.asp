// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Runtime;

/// <summary>Counts compilations while delegating to the real compiler.</summary>
internal sealed class RecordingCompiler(ITemplateCompiler inner) : ITemplateCompiler {

    public int CompileCount { get; private set; }

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(string source) {
        CompileCount++;
        return inner.Compile(source);
    }

    public CompiledTemplate Load(byte[] image) {
        return inner.Load(image);
    }
}
