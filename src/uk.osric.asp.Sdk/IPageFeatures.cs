// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

using System.Diagnostics.CodeAnalysis;

/// <summary>The optional capabilities this render offers.</summary>
/// <remarks>
/// Read-only: a page asks, and the host has already decided. A capability is either registered with
/// an implementation or absent — never registered as <see langword="null"/>.
/// </remarks>
public interface IPageFeatures {

    /// <summary>Reads a capability that this render may or may not offer.</summary>
    /// <param name="feature">The implementation, when this render offers the capability.</param>
    /// <returns>Whether this render offers the capability.</returns>
    public bool TryGet<T>([NotNullWhen(true)] out T? feature) where T : class;

    /// <summary>Reads a capability, or <see langword="null"/> when this render does not offer it.</summary>
    public T? Get<T>() where T : class => TryGet<T>(out T? feature) ? feature : null;

    /// <summary>Reads a capability this page cannot render without.</summary>
    /// <exception cref="InvalidOperationException">This render does not offer the capability.</exception>
    public T GetRequired<T>() where T : class =>
        TryGet<T>(out T? feature)
            ? feature
            : throw new InvalidOperationException($"This render does not offer {typeof(T).Name}.");
}
