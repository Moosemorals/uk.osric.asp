// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>Keeps the host's log entries so a fixture can assert what the journal would have said.</summary>
/// <remarks>
/// Added beside the JSON console provider, not instead of it, so the assertions are made against the
/// entries the real pipeline produced. Scopes are read the way the console formatter reads them: when
/// the entry is written, from the provider's own scope stack.
/// </remarks>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope {

    private readonly ConcurrentQueue<Entry> _entries = [];

    /// <summary>Everything the host has logged since it started, in order.</summary>
    public IReadOnlyList<Entry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => Scopes = scopeProvider;

    public void Dispose() { }

    private IExternalScopeProvider Scopes { get; set; } = new LoggerExternalScopeProvider();

    /// <summary>One captured entry, with the scope fields that were open when it was written.</summary>
    /// <param name="Category">The category the entry was filed under.</param>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception logged with it, if any.</param>
    /// <param name="Scope">The scope fields, innermost winning.</param>
    public sealed record Entry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Scope);

    private sealed class CapturingLogger(string category, ConcurrentQueue<Entry> entries, LogCapture capture) : ILogger {

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            ArgumentNullException.ThrowIfNull(state);

            return capture.Scopes.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            ArgumentNullException.ThrowIfNull(formatter);

            Dictionary<string, object?> scope = [with(StringComparer.OrdinalIgnoreCase)];
            capture.Scopes.ForEachScope((open, collected) => {
                if (open is IEnumerable<KeyValuePair<string, object?>> values) {
                    foreach (KeyValuePair<string, object?> value in values) {
                        collected[value.Key] = value.Value;
                    }
                }
            }, scope);

            entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception, scope));
        }
    }
}
