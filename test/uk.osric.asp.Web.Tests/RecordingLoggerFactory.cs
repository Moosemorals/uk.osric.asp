// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;

/// <summary>Captures log entries so a test can assert the category, the level, the message and the scope.</summary>
/// <remarks>
/// Scopes are held the way the real factory holds them - on an <see cref="LoggerExternalScopeProvider"/>
/// shared by every logger - rather than per logger, so a test can open a scope with one category and
/// see it from an entry written under another. That is what a request scope does.
/// </remarks>
internal sealed class RecordingLoggerFactory : ILoggerFactory {

    private readonly List<Entry> _entries = [];
    private readonly List<string> _categories = [];
    private readonly LoggerExternalScopeProvider _scopes = new();

    /// <summary>Everything written through a logger this factory created, in order.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Every distinct category a logger was asked for, in order, including repeats.</summary>
    public IReadOnlyList<string> Categories => _categories;

    public ILogger CreateLogger(string categoryName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        _categories.Add(categoryName);

        return new RecordingLogger(categoryName, _entries, _scopes);
    }

    public void AddProvider(ILoggerProvider provider) {
        throw new NotSupportedException("The recording factory is the only provider a test needs.");
    }

    public void Dispose() { }

    /// <summary>One captured log entry.</summary>
    /// <param name="Category">The category the entry was filed under.</param>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception logged with it, if any.</param>
    /// <param name="Scope">The scope properties open when the entry was written, innermost winning.</param>
    internal sealed record Entry(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Scope);

    private sealed class RecordingLogger(string category, List<Entry> entries, LoggerExternalScopeProvider scopes) : ILogger {

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            ArgumentNullException.ThrowIfNull(state);

            return scopes.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            ArgumentNullException.ThrowIfNull(formatter);

            Dictionary<string, object?> scope = new(StringComparer.OrdinalIgnoreCase);
            scopes.ForEachScope((open, collected) => {
                if (open is IEnumerable<KeyValuePair<string, object?>> values) {
                    foreach (KeyValuePair<string, object?> value in values) {
                        collected[value.Key] = value.Value;
                    }
                }
            }, scope);

            entries.Add(new Entry(category, logLevel, formatter(state, exception), exception, scope));
        }
    }
}
