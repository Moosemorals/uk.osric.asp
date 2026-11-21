// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Testing;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

/// <summary>Reuses the real compiler's answer for source and libraries it has already seen.</summary>
/// <remarks>
/// A fixture that renders a page is about the render, not about Roslyn: the same seeded page is
/// compiled once per process rather than once per test. Every call still goes through the compiler
/// this wraps, so a counting compiler outside it records what the test asked for, and the image a hit
/// returns is the immutable bytes the first compile produced.
/// </remarks>
internal sealed class CachingCompiler(IPageCompiler inner) : IPageCompiler {

    // Shared by every fixture in the process: the whole point is that the second fixture's test of
    // the same seeded page costs a lookup rather than a compile. The value is lazy so two fixtures
    // that reach the same page at the same moment wait for one compile instead of racing two: the
    // first compile in a process is Roslyn's warm-up and the one thing here worth not repeating.
    private static readonly ConcurrentDictionary<string, Lazy<CompileResult>> s_results = new(StringComparer.Ordinal);

    public string RuntimeId => inner.RuntimeId;

    public CompileResult Compile(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(libraries);

        return Cached("page", source, libraries, inner.Compile, cancellationToken);
    }

    public CompileResult CompileWorker(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(libraries);

        return Cached("worker", source, libraries, inner.CompileWorker, cancellationToken);
    }

    public LoadedPage Load(byte[] image, byte[]? symbols = null) {
        return inner.Load(image, symbols);
    }

    public LoadedWorker LoadWorker(byte[] image, byte[]? symbols = null) {
        return inner.LoadWorker(image, symbols);
    }

    private CompileResult Cached(
        string kind,
        GeneratedSource source,
        IReadOnlyList<LibrarySource> libraries,
        Func<GeneratedSource, IReadOnlyList<LibrarySource>, CancellationToken, CompileResult> compile,
        CancellationToken cancellationToken) {
        string key = Key(kind, source, libraries);
        Lazy<CompileResult> lazy = s_results.GetOrAdd(key,
            _ => new Lazy<CompileResult>(() => compile(source, libraries, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return Answer(key, lazy);
    }

    private static CompileResult Answer(string key, Lazy<CompileResult> lazy) {
        try {
            return lazy.Value;
        } catch {
            // Only an answer counts as one: a compile that threw (a deadline, a cancellation) is the
            // caller's to see and is not what the same source compiles to, so it is not remembered.
            s_results.TryRemove(new KeyValuePair<string, Lazy<CompileResult>>(key, lazy));
            throw;
        }
    }

    // A compile is a function of its generated source, the libraries in scope and the compiler's
    // own runtime, so the fingerprint names all three. The runtime id is in every key because a
    // fixture may pass a compiler whose contract differs from the one an earlier fixture used.
    private string Key(string kind, GeneratedSource source, IReadOnlyList<LibrarySource> libraries) {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, kind);
        Append(hash, RuntimeId);
        Append(hash, source.Text);
        foreach (LibrarySource library in libraries) {
            Append(hash, library.Key);
            Append(hash, library.Source.Text);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text) {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData([0]);
    }
}
