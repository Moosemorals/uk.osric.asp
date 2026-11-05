// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal sealed class AgentRunRegistry(AgentOptions options, TimeProvider clock) {
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentRunStream> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _activeByConversation = [];

    public bool TryStart(string runId, long conversationId, out AgentRunStream? stream) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (_gate) {
            if (_activeByConversation.ContainsKey(conversationId) || _runs.ContainsKey(runId)) {
                stream = null;
                return false;
            }

            stream = new AgentRunStream(runId, options, clock);
            _runs.Add(runId, stream);
            _activeByConversation.Add(conversationId, runId);
            return true;
        }
    }

    public AgentRunStream? Find(string runId) {
        lock (_gate) {
            if (!_runs.TryGetValue(runId, out AgentRunStream? stream)) {
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
            if (_runs.TryGetValue(runId, out AgentRunStream? stream)) {
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

internal sealed class AgentRunStream(string runId, AgentOptions options, TimeProvider clock) {
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly Queue<AgentEvent> _journal = new();
    private readonly HashSet<Channel<AgentEvent>> _subscribers = [];
    private readonly AgentOptions _options = options;
    private readonly TimeProvider _clock = clock;
    private readonly CancellationTokenSource _stop = new();
    private long _sequence;
    private long _journalBytes;
    private DateTimeOffset? _expiresAt;
    private bool _completed;

    public string RunId { get; } = runId;
    public CancellationToken StopToken => _stop.Token;
    public long LastSequence { get { lock (_gate) { return _sequence; } } }
    public bool CanReplayFrom(long after) {
        lock (_gate) {
            long oldest = _journal.Count == 0 ? _sequence + 1 : _journal.Peek().Sequence;
            return after >= oldest - 1 && after <= _sequence;
        }
    }
    public bool IsExpired(DateTimeOffset now) { lock (_gate) { return _expiresAt is { } expiry && expiry <= now; } }

    public AgentEvent Publish(string type, object data) {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(data);
        string json = JsonSerializer.Serialize(data, s_jsonOptions);
        lock (_gate) {
            if (_completed) {
                throw new InvalidOperationException("The run stream is complete.");
            }

            AgentEvent @event = new(RunId, ++_sequence, type, json);
            int bytes = Encoding.UTF8.GetByteCount(json) + Encoding.UTF8.GetByteCount(type) + RunId.Length + 24;
            _journal.Enqueue(@event);
            _journalBytes += bytes;
            while (_journal.Count > _options.JournalMaxEvents || _journalBytes > _options.JournalMaxBytes) {
                AgentEvent removed = _journal.Dequeue();
                _journalBytes -= EventBytes(removed);
            }

            foreach (Channel<AgentEvent> subscriber in _subscribers.ToArray()) {
                if (!subscriber.Writer.TryWrite(@event)) {
                    ResetSlowSubscriber(subscriber);
                }
            }

            return @event;
        }
    }

    public bool Stop() {
        if (_completed || _stop.IsCancellationRequested) {
            return false;
        }
        _stop.Cancel();
        return true;
    }

    public void Complete() {
        lock (_gate) {
            if (_completed) {
                return;
            }
            _completed = true;
            _expiresAt = _clock.GetUtcNow().AddMinutes(_options.JournalTtlMinutes);
            foreach (Channel<AgentEvent> subscriber in _subscribers) {
                subscriber.Writer.TryComplete();
            }
            _subscribers.Clear();
        }
    }

    public async IAsyncEnumerable<AgentEvent> ReadAfterAsync(
        long after,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        Channel<AgentEvent> subscriber = Channel.CreateBounded<AgentEvent>(new BoundedChannelOptions(_options.JournalMaxEvents) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        lock (_gate) {
            long oldest = _journal.Count == 0 ? _sequence + 1 : _journal.Peek().Sequence;
            bool cannotReplay = after < oldest - 1 || after > _sequence;
            if (cannotReplay) {
                subscriber.Writer.TryWrite(ResetEvent("requested sequence is not retained"));
                subscriber.Writer.TryComplete();
            } else {
                foreach (AgentEvent @event in _journal.Where(item => item.Sequence > after)) {
                    subscriber.Writer.TryWrite(@event);
                }
                if (_completed) {
                    subscriber.Writer.TryComplete();
                } else {
                    _subscribers.Add(subscriber);
                }
            }
        }

        try {
            await foreach (AgentEvent @event in subscriber.Reader.ReadAllAsync(cancellationToken)) {
                yield return @event;
            }
        } finally {
            lock (_gate) {
                _subscribers.Remove(subscriber);
            }
        }
    }

    private void ResetSlowSubscriber(Channel<AgentEvent> subscriber) {
        _subscribers.Remove(subscriber);
        while (subscriber.Reader.TryRead(out _)) { }
        subscriber.Writer.TryWrite(ResetEvent("subscriber could not keep up"));
        subscriber.Writer.TryComplete();
    }

    private AgentEvent ResetEvent(string reason) => new(
        RunId,
        _sequence,
        "stream.reset",
        JsonSerializer.Serialize(new { reason }, s_jsonOptions));

    private static int EventBytes(AgentEvent @event) =>
        Encoding.UTF8.GetByteCount(@event.DataJson) + Encoding.UTF8.GetByteCount(@event.Type) + @event.RunId.Length + 24;
}
