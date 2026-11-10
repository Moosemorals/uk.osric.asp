// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics.CodeAnalysis;
using uk.osric.asp.Sdk;

// The stored pages that still say ctx.Features reach this object as IPageFeatures, so the registry
// implements the deprecated alias until IDEA-026 removes it (DEC-228).
#pragma warning disable PAGE005

/// <summary>The registry a page reads its optional capabilities from.</summary>
/// <remarks>
/// Built while a layer is being constructed and handed to the page as the read-only
/// <see cref="IPageCapabilities"/>, so nothing page code can name can add or replace a capability the
/// host decided on.
/// </remarks>
internal sealed class PageCapabilities : IPageCapabilities, IPageFeatures {
#pragma warning restore PAGE005

    private readonly Dictionary<Type, object> _capabilities = [];

    /// <summary>Registers a capability under the interface the page asks for.</summary>
    /// <typeparam name="T">The capability interface.</typeparam>
    /// <param name="capability">The implementation; it may not be <see langword="null"/>.</param>
    /// <returns>This registry, so the layer can register its capabilities in one expression.</returns>
    /// <exception cref="InvalidOperationException">The capability is already registered.</exception>
    public PageCapabilities Add<T>(T capability) where T : class {
        ArgumentNullException.ThrowIfNull(capability);

        foreach (object existing in _capabilities.Values) {
            if (existing is T) {
                throw new InvalidOperationException($"{typeof(T).Name} is already registered.");
            }
        }

        _capabilities.Add(typeof(T), capability);
        return this;
    }

    /// <inheritdoc/>
    public bool TryGet<T>([NotNullWhen(true)] out T? capability) where T : class {
        if (_capabilities.TryGetValue(typeof(T), out object? registered) && registered is T exact) {
            capability = exact;
            return true;
        }

        // The host normally registers under the capability interface, but a registration by its
        // implementation is still the same capability, so a lookup by the interface finds it.
        foreach (object candidate in _capabilities.Values) {
            if (candidate is T match) {
                capability = match;
                return true;
            }
        }

        capability = null;
        return false;
    }

    /// <summary>Gives every bound capability the render's deadline.</summary>
    /// <param name="deadline">The deadline's token.</param>
    public void AttachDeadline(CancellationToken deadline) {
        foreach (object capability in _capabilities.Values) {
            if (capability is IRenderBoundCapability bound) {
                bound.AttachDeadline(deadline);
            }
        }
    }

    /// <summary>Stops every bound capability, because the render was given up on.</summary>
    public void Abandon() {
        foreach (object capability in _capabilities.Values) {
            if (capability is IRenderBoundCapability bound) {
                bound.Abandon();
            }
        }
    }
}
