// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>A provider failure that carries a message safe to show and a stable code for metrics.</summary>
internal sealed class ProviderException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException) {
    /// <summary>The provider rejected the request because the prompt and the reply exceed the window.</summary>
    /// <remarks>
    /// It is its own code because it is the one rejection that says the cached window is stale, so the
    /// run reads the model list again and sends the step once more rather than failing the run.
    /// </remarks>
    public const string ContextWindowMismatch = "context_window_mismatch";

    public string Code { get; } = code;
}
