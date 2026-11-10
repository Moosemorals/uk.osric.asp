// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The capabilities this render offers.</summary>
/// <remarks>Deprecated: every member is inherited from <see cref="IPageCapabilities"/>.</remarks>
[Obsolete(PageCapabilityDeprecation.Message, DiagnosticId = "PAGE005")]
public interface IPageFeatures : IPageCapabilities;
