// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;

/// <summary>Captures log entries so a test can assert the category, the level and the message.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory {

    private readonly List<Entry> _entries = [];

    /// <summary>Everything written through a logger this factory created, in order.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

        return new RecordingLogger(categoryName, _entries);
    }

    public void AddProvider(ILoggerProvider provider) {
        throw new NotSupportedException("The recording factory is the only provider a test needs.");
    }

    public void Dispose() { }

    /// <summary>One captured log entry.</summary>
    /// <param name="Category">The category the entry was filed under.</param>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The formatted message.</param>
    internal sealed record Entry(string Category, LogLevel Level, string Message);

    private sealed class RecordingLogger(string category, List<Entry> entries) : ILogger {

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) {
            ArgumentNullException.ThrowIfNull(formatter);

            entries.Add(new Entry(category, logLevel, formatter(state, exception)));
        }
    }
}
