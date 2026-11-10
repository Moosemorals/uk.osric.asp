// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>Page storage the render can stop.</summary>
/// <remarks>
/// The handle a page holds is only <see cref="IPageStorage"/>; this is the shell's view of the same
/// object, with the deadline and abandonment the SDK deliberately does not expose (DEC-123).
/// </remarks>
internal interface IRenderBoundPageStorage : IPageStorage, IRenderBoundCapability;
