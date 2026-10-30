// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using Microsoft.CodeAnalysis.Emit;

public class SimpleContext : IContext {
    public EmitResult? BuildResult { get; set; }

    public void Log(string message) {
        Console.WriteLine(message);
    }
}