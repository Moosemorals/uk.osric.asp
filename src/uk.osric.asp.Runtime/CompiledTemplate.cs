// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using uk.osric.asp.Core;

/// <summary>A compiled page, loaded into its own collectible assembly load context.</summary>
/// <remarks>
/// One instance is cheap to create from stored bytes and safe to invoke repeatedly; each invocation
/// still gets a fresh handler instance so page fields cannot leak between requests.
/// </remarks>
public sealed class CompiledTemplate : IDisposable {

    private readonly AssemblyLoadContext _context;
    private readonly Type _handler;
    private readonly MethodInfo _handle;

    internal CompiledTemplate(byte[] image) {
        _context = new AssemblyLoadContext(null, isCollectible: true);

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
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Invoke(IContext context) {
        ArgumentNullException.ThrowIfNull(context);

        _handle.Invoke(Activator.CreateInstance(_handler), [context]);
    }

    /// <summary>Marks the load context for unloading.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose() {
        _context.Unload();
    }
}
