// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using uk.osric.asp.Core;

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
public sealed class CompiledTemplate : IDisposable {

    private readonly AssemblyLoadContext _context;
    private readonly Type _handler;
    private readonly MethodInfo _handle;

    internal CompiledTemplate(byte[] image) {
        _context = new PageLoadContext();

        try {
            using MemoryStream stream = new(image, writable: false);
            Assembly assembly = _context.LoadFromStream(stream);

            _handler = assembly.GetTypes().FirstOrDefault(type => type.IsAssignableTo(typeof(IHandler)))
                ?? throw new InvalidOperationException("No handler found in the assembly.");

            _handle = _handler.GetMethod("Handle")
                ?? throw new InvalidOperationException("No handle method found in the handler.");
        } catch {
            _context.Unload();
            throw;
        }
    }

    /// <summary>Runs the page against a request context.</summary>
    /// <param name="context">The context the page writes to.</param>
    /// <exception cref="System.Exception">Whatever the page threw, unwrapped.</exception>
    public void Invoke(IContext context) {
        ArgumentNullException.ThrowIfNull(context);

        try {
            _handle.Invoke(Activator.CreateInstance(_handler), [context]);
        } catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            // The runtime owns the reflection, so it owns the reflection's wrapper: callers get the
            // exception the page threw, with the page's own stack rather than a nested one.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    /// <summary>Marks the load context for unloading.</summary>
    public void Dispose() {
        _context.Unload();
    }
}
