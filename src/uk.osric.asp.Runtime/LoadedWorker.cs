// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Runtime.Loader;
using uk.osric.asp.Sdk;

/// <summary>A compiled worker, loaded into its own collectible assembly load context.</summary>
/// <remarks>
/// The image was checked when it was saved, so a missing or doubled entry point here is a stored
/// image that no longer matches the running build rather than an author's mistake; it is still an
/// exception, because there is nothing sensible to run.
/// </remarks>
public sealed class LoadedWorker : IDisposable {

    private readonly AssemblyLoadContext _context;
    private readonly Type _entryPoint;

    /// <summary>Loads a worker image and finds the one entry point it declares.</summary>
    /// <param name="image">The bytes an earlier <see cref="IPageCompiler.CompileWorker"/> emitted.</param>
    /// <param name="symbols">The portable PDB bytes emitted with the image, if present.</param>
    /// <exception cref="InvalidOperationException">
    /// The assembly does not declare exactly one worker entry point.
    /// </exception>
    public LoadedWorker(byte[] image, byte[]? symbols = null) {
        ArgumentNullException.ThrowIfNull(image);

        _context = new PageLoadContext();

        try {
            using MemoryStream stream = new(image, writable: false);
            System.Reflection.Assembly assembly;
            if (symbols is null) {
                assembly = _context.LoadFromStream(stream);
            } else {
                using MemoryStream symbolStream = new(symbols, writable: false);
                assembly = _context.LoadFromStream(stream, symbolStream);
            }

            Type[] entryPoints = [.. assembly.GetTypes().Where(type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(IWorker).IsAssignableFrom(type))];

            _entryPoint = entryPoints.Length switch {
                1 => entryPoints[0],
                0 => throw new InvalidOperationException("No worker entry point was found in the assembly."),
                _ => throw new InvalidOperationException(
                    $"The assembly declares {entryPoints.Length} worker entry points; a worker declares exactly one."),
            };
        } catch {
            _context.Unload();
            throw;
        }
    }

    /// <summary>Creates an instance of the worker's entry point.</summary>
    /// <returns>A fresh worker, so nothing a finished job left behind reaches the next one.</returns>
    public IWorker Create() => (IWorker)Activator.CreateInstance(_entryPoint)!;

    /// <summary>Marks the load context for unloading.</summary>
    public void Dispose() => _context.Unload();
}
