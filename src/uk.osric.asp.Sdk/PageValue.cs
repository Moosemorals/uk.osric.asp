// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Whether a stored key was found, and the value read when it was.</summary>
/// <remarks>
/// An <c>out</c> parameter cannot be used on the asynchronous storage read, so presence and value
/// travel together instead. A stored JSON <c>null</c> is found with a null value; an absent key is
/// not found with the default.
/// </remarks>
/// <typeparam name="T">The type the value was deserialised as.</typeparam>
/// <param name="Found">Whether the key exists.</param>
/// <param name="Value">The stored value, or the default when the key is absent.</param>
public readonly record struct PageValue<T>(bool Found, T? Value);
