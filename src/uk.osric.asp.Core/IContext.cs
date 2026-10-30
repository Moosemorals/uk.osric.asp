// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using Microsoft.CodeAnalysis.Emit;

public interface IContext {

    public void Log(string message);

    public EmitResult? BuildResult { get; set; }
}