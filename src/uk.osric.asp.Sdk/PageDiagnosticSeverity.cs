// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>How serious a <see cref="PageDiagnostic"/> is.</summary>
public enum PageDiagnosticSeverity {

    /// <summary>The page compiles and is served; the warning is unfinished work.</summary>
    Warning,

    /// <summary>The page is stored but produces no image, so it cannot be served until it is fixed.</summary>
    Error,
}
