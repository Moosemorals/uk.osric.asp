// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>The live run journals, by run id and by conversation.</summary>
internal sealed class RunRegistry(AssistantOptions options, TimeProvider clock) {
    private readonly object _gate = new();
    private readonly Dictionary<string, RunStream> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _activeByConversation = [];

    public bool TryStart(string runId, long conversationId, out RunStream? stream) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (_gate) {
            if (_activeByConversation.ContainsKey(conversationId) || _runs.ContainsKey(runId)) {
                stream = null;
                return false;
            }

            stream = new RunStream(runId, options, clock);
            _runs.Add(runId, stream);
            _activeByConversation.Add(conversationId, runId);
            return true;
        }
    }

    public RunStream? Find(string runId) {
        lock (_gate) {
            if (!_runs.TryGetValue(runId, out RunStream? stream)) {
                return null;
            }
            if (stream.IsExpired(clock.GetUtcNow())) {
                _runs.Remove(runId);
                return null;
            }
            return stream;
        }
    }

    public string? ActiveRun(long conversationId) {
        lock (_gate) {
            return _activeByConversation.GetValueOrDefault(conversationId);
        }
    }

    public bool Stop(string runId) => Find(runId)?.Stop() ?? false;

    public void Complete(string runId, long conversationId) {
        lock (_gate) {
            if (_runs.TryGetValue(runId, out RunStream? stream)) {
                stream.Complete();
            }
            if (_activeByConversation.GetValueOrDefault(conversationId) == runId) {
                _activeByConversation.Remove(conversationId);
            }
        }
    }

    public void RemoveExpired() {
        DateTimeOffset now = clock.GetUtcNow();
        lock (_gate) {
            foreach (string runId in _runs.Where(pair => pair.Value.IsExpired(now)).Select(pair => pair.Key).ToArray()) {
                _runs.Remove(runId);
            }
        }
    }
}
