// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using uk.osric.asp.Sdk;

/// <summary>A compiled page, loaded into its own collectible assembly load context.</summary>
/// <remarks>
/// One instance is cheap to create from stored bytes and safe to invoke repeatedly; each invocation
/// still gets a fresh handler instance so page fields cannot leak between requests.
/// <para>
/// Nothing here suppresses inlining. <c>MethodInfo.Invoke</c> throws its wrapper whether or not the
/// caller was inlined, and <see cref="ExceptionDispatchInfo"/> restores the original stack, so the
/// attribute that used to sit on these members protected nothing. If unloading a collectible context
/// ever appears to hang, that attribute is the knob to revisit.
/// </para>
/// </remarks>
public sealed class LoadedPage : IDisposable {

    internal const string HandlerTypeName = "GeneratedPage";
    internal const string HandlerMethodName = "HandleAsync";

    private readonly AssemblyLoadContext _context;
    private readonly Type _handler;
    private readonly MethodInfo _handle;

    internal LoadedPage(byte[] image, byte[]? symbols) {
        _context = new PageLoadContext();

        try {
            using MemoryStream stream = new(image, writable: false);
            Assembly assembly;
            if (symbols is null) {
                assembly = _context.LoadFromStream(stream);
            } else {
                using MemoryStream symbolStream = new(symbols, writable: false);
                assembly = _context.LoadFromStream(stream, symbolStream);
            }

            // By name, not "the first type that implements IHandler": a page may declare a type of
            // its own that implements the interface, and the generator always emits its handler.
            _handler = assembly.GetType(HandlerTypeName)
                ?? throw new InvalidOperationException("No handler found in the assembly.");

            _handle = _handler.GetMethod(HandlerMethodName)
                ?? throw new InvalidOperationException("No handle method found in the handler.");
        } catch {
            _context.Unload();
            throw;
        }
    }

    /// <summary>Runs the page against a request context.</summary>
    /// <param name="context">The context the page writes to.</param>
    /// <returns>A task that completes when the page has finished.</returns>
    /// <exception cref="System.Exception">Whatever the page threw, unwrapped.</exception>
    public async Task InvokeAsync(IContext context) {
        ArgumentNullException.ThrowIfNull(context);

        object? result;
        try {
            result = _handle.Invoke(Activator.CreateInstance(_handler), [context]);
        } catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            // The runtime owns the reflection, so it owns the reflection's wrapper: callers get the
            // exception the page threw, with the page's own stack rather than a nested one. An async
            // method hands its exception to the task instead, so this is now nearly unreachable - it
            // stays as the guard that keeps the wrapper out of the shell's catch blocks.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (result is not Task page) {
            throw new InvalidOperationException(
                "The page handler did not return a task. This is a compiler fault, not a page error.");
        }

        // Awaiting the page's own task is what rethrows its exception with its own stack, stitched
        // from the portable PDB: no ExceptionDispatchInfo, no AggregateException, no nested frame.
        await page;
    }

    /// <summary>Marks the load context for unloading.</summary>
    public void Dispose() {
        _context.Unload();
    }
}
