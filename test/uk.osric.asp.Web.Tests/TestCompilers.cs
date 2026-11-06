// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>A compiler that always fails the way a broken generator would.</summary>
internal sealed class FaultingCompiler : IPageCompiler {

    public string RuntimeId => "faulting";

    public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) {
        throw new InvalidOperationException("The generated page source did not compile.");
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        throw new NotSupportedException("A faulting compiler never produces an image.");
    }
}

/// <summary>A compile that outlasts a one-second render budget, the way a cold Roslyn does.</summary>
/// <remarks>
/// It finishes, so its image must be kept even though the answer was given up on.
/// </remarks>
internal sealed class SlowCompiler(IPageCompiler inner) : IPageCompiler {

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) {
        Thread.Sleep(TimeSpan.FromMilliseconds(1300));

        return inner.Compile(source, cancellationToken);
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        return inner.Load(image, symbols);
    }
}

/// <summary>A compile that waits out its deadline and cancels, the way Roslyn does.</summary>
/// <remarks>
/// Roslyn honours the token between operations rather than inside one, so a compile that outruns its
/// budget reports by cancelling. This one waits for the deadline Roslyn would have observed.
/// </remarks>
internal sealed class StallingCompiler : IPageCompiler {

    public string RuntimeId => "stalling";

    public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) {
        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        throw new OperationCanceledException(cancellationToken);
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        throw new NotSupportedException("A stalling compiler never produces an image.");
    }
}
