// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics.CodeAnalysis;
using uk.osric.asp.Sdk;

/// <summary>The registry a page reads its optional capabilities from.</summary>
/// <remarks>
/// Built while a layer is being constructed and handed to the page as the read-only
/// <see cref="IPageFeatures"/>, so nothing page code can name can add or replace a capability the
/// host decided on.
/// </remarks>
internal sealed class PageFeatures : IPageFeatures {

    private readonly Dictionary<Type, object> _features = [];

    /// <summary>Registers a capability under the interface the page asks for.</summary>
    /// <typeparam name="T">The capability interface.</typeparam>
    /// <param name="feature">The implementation; it may not be <see langword="null"/>.</param>
    /// <returns>This registry, so the layer can register its capabilities in one expression.</returns>
    /// <exception cref="InvalidOperationException">The capability is already registered.</exception>
    public PageFeatures Add<T>(T feature) where T : class {
        ArgumentNullException.ThrowIfNull(feature);

        foreach (object existing in _features.Values) {
            if (existing is T) {
                throw new InvalidOperationException($"{typeof(T).Name} is already registered.");
            }
        }

        _features.Add(typeof(T), feature);
        return this;
    }

    /// <inheritdoc/>
    public bool TryGet<T>([NotNullWhen(true)] out T? feature) where T : class {
        if (_features.TryGetValue(typeof(T), out object? registered) && registered is T exact) {
            feature = exact;
            return true;
        }

        // The host normally registers under the capability interface, but a registration by its
        // implementation is still the same capability, so a lookup by the interface finds it.
        foreach (object candidate in _features.Values) {
            if (candidate is T match) {
                feature = match;
                return true;
            }
        }

        feature = null;
        return false;
    }

    /// <summary>Gives every bound capability the render's deadline.</summary>
    /// <param name="deadline">The deadline's token.</param>
    public void AttachDeadline(CancellationToken deadline) {
        foreach (object feature in _features.Values) {
            if (feature is IRenderBoundFeature bound) {
                bound.AttachDeadline(deadline);
            }
        }
    }

    /// <summary>Stops every bound capability, because the render was given up on.</summary>
    public void Abandon() {
        foreach (object feature in _features.Values) {
            if (feature is IRenderBoundFeature bound) {
                bound.Abandon();
            }
        }
    }
}
