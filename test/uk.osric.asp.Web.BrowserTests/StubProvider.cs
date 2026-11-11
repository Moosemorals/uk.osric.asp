// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>One chunk of a scripted provider response.</summary>
/// <param name="Content">Answer text to send as a <c>content</c> delta.</param>
/// <param name="Reasoning">Thinking text to send as a <c>reasoning_content</c> delta.</param>
/// <param name="Delay">How long to wait before sending this chunk.</param>
internal sealed record StubChunk(string? Content = null, string? Reasoning = null, TimeSpan Delay = default);

/// <summary>What the scripted provider answers one chat-completions request with.</summary>
internal sealed class StubResponse {

    /// <summary>Chunks to stream, in order.</summary>
    public List<StubChunk> Chunks { get; init; } = [];

    /// <summary>Wait before the response body starts, so a request can outlive a client deadline.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>A tool call sent as one complete delta rather than token by token.</summary>
    public (string Id, string Name, string Arguments)? ToolCall { get; init; }

    /// <summary>The reason the response ends; <c>stop</c> unless a script asks for another.</summary>
    public string FinishReason { get; init; } = "stop";

    /// <summary>Streams <paramref name="text"/> a word at a time, waiting <paramref name="perChunk"/> between words.</summary>
    public static StubResponse Words(string text, TimeSpan perChunk) =>
        new() { Chunks = [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new StubChunk(Content: word + " ", Delay: perChunk))] };

    /// <summary>Streams thinking and then an answer, both a word at a time with the same wait.</summary>
    public static StubResponse Thinking(string reasoning, string answer, TimeSpan perChunk) =>
        new() {
            Chunks = [
                .. reasoning.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new StubChunk(Reasoning: word + " ", Delay: perChunk)),
                .. answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => new StubChunk(Content: word + " ", Delay: perChunk)),
            ],
        };

    /// <summary>Answers with one tool call and no text, so the run has to ask the provider again.</summary>
    public static StubResponse Tool(string id, string name, string arguments) =>
        new() { ToolCall = (id, name, arguments) };

    /// <summary>Truncates an answer at the provider's token limit, which is what a long think does.</summary>
    public static StubResponse Truncated(string text, TimeSpan perChunk) =>
        new() { Chunks = Words(text, perChunk).Chunks, FinishReason = "length" };

    /// <summary>Accepts the request and then answers nothing until the server gives up on it.</summary>
    public static StubResponse Stall(TimeSpan delay) => new() { Delay = delay };
}

/// <summary>
/// A local stand-in for an OpenAI-compatible provider. It streams server-sent events the way a real
/// one does, and can be told to think slowly, answer for longer than a deadline, or stop mid-sentence,
/// so a test can reproduce what a model does on a bad day without waiting for one.
/// </summary>
internal sealed class StubProvider : IAsyncDisposable {

    private readonly HttpListener _listener = new();
    private readonly Queue<StubResponse> _script = new();
    private readonly List<string> _requests = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private readonly Lock _gate = new();

    public StubProvider(int port) {
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = AcceptAsync(_stopping.Token);
    }

    /// <summary>The port the provider listens on.</summary>
    public int Port { get; }

    /// <summary>Where an OpenAI-compatible client should send its requests.</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>The JSON bodies received so far, oldest first.</summary>
    public IReadOnlyList<string> Requests { get { lock (_gate) { return [.. _requests]; } } }

    /// <summary>Answers the next requests with these responses, in order, then with a short default.</summary>
    public void Script(params StubResponse[] responses) {
        lock (_gate) {
            _script.Clear();
            foreach (StubResponse response in responses) {
                _script.Enqueue(response);
            }
        }
    }

    /// <summary>Forgets the bodies received so far.</summary>
    public void ForgetRequests() { lock (_gate) { _requests.Clear(); } }

    public async ValueTask DisposeAsync() {
        await _stopping.CancelAsync();
        _listener.Stop();
        try {
            await _loop;
        } catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException) {
        }
        _listener.Close();
        _stopping.Dispose();
    }

    private async Task AcceptAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            HttpListenerContext context;
            try {
                context = await _listener.GetContextAsync();
            } catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException) {
                return;
            }
            _ = Task.Run(() => AnswerAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context, CancellationToken cancellationToken) {
        try {
            using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync(cancellationToken);
            StubResponse response;
            lock (_gate) {
                _requests.Add(body);
                response = _script.Count > 0 ? _script.Dequeue() : StubResponse.Words("ok", TimeSpan.Zero);
            }

            if (response.Delay > TimeSpan.Zero) {
                await Task.Delay(response.Delay, cancellationToken);
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.SendChunked = true;
            context.Response.Headers["Cache-Control"] = "no-cache";

            Stream output = context.Response.OutputStream;
            foreach (StubChunk chunk in response.Chunks) {
                if (chunk.Delay > TimeSpan.Zero) {
                    await Task.Delay(chunk.Delay, cancellationToken);
                }
                await WriteAsync(output, Delta(chunk), null, cancellationToken);
            }
            if (response.ToolCall is { } call) {
                await WriteAsync(output, ToolCallDelta(call), null, cancellationToken);
            }
            await WriteAsync(output, new Dictionary<string, object?>(), response.FinishReason, cancellationToken, Usage());
            await WriteDataAsync(output, "[DONE]", cancellationToken);
            output.Close();
            context.Response.Close();
        } catch (Exception exception) when (exception is HttpListenerException or IOException or OperationCanceledException or ObjectDisposedException) {
            context.Response.Abort();
        }
    }

    private static Dictionary<string, object?> Delta(StubChunk chunk) {
        Dictionary<string, object?> delta = [];
        if (chunk.Reasoning is not null) {
            delta["reasoning_content"] = chunk.Reasoning;
        }
        if (chunk.Content is not null) {
            delta["content"] = chunk.Content;
        }
        return delta;
    }

    private static object ToolCallDelta((string Id, string Name, string Arguments) call) => new {
        tool_calls = new[] {
            new {
                index = 0,
                id = call.Id,
                type = "function",
                function = new { name = call.Name, arguments = call.Arguments },
            },
        },
    };

    private static object Usage() => new {
        prompt_tokens = 1_000,
        completion_tokens = 20,
        prompt_cache_hit_tokens = 600,
        completion_tokens_details = new { reasoning_tokens = 5 },
    };

    private static async Task WriteAsync(Stream output, object delta, string? finishReason, CancellationToken cancellationToken, object? usage = null) {
        string payload = JsonSerializer.Serialize(new {
            id = "stub",
            @object = "chat.completion.chunk",
            created = 0,
            model = "stub-model",
            choices = new[] { new { index = 0, delta, finish_reason = finishReason } },
            usage,
        });
        await WriteRawAsync(output, payload, cancellationToken);
    }

    private static Task WriteDataAsync(Stream output, string payload, CancellationToken cancellationToken) =>
        WriteRawAsync(output, payload, cancellationToken);

    private static async Task WriteRawAsync(Stream output, string payload, CancellationToken cancellationToken) {
        byte[] bytes = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
        await output.WriteAsync(bytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
