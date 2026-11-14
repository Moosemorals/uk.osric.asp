// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>How serious a <see cref="PageDiagnostic"/> is.</summary>
public enum PageDiagnosticSeverity {
    /// <summary>The page runs; the author is told and the image is kept.</summary>
    Warning,

    /// <summary>The page cannot run; no image is emitted.</summary>
    Error,
}
