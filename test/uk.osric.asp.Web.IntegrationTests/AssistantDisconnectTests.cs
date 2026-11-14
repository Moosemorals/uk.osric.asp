// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.Extensions.Logging;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

/// <summary>The failed-request line a browser reload used to leave behind (BUG-17, ARCH-13).</summary>
/// <remarks>
/// The proof that a write after a disconnect is handled is the unit test over the guard. This fixture
/// drives the real host's server-sent-event path and asserts the journal gains no unhandled exception;
/// a test host may end the enumerable before a write is attempted, so no log line is required here.
/// </remarks>
[TestFixture]
public sealed class AssistantDisconnectTests : IntegrationTest {

    private readonly StalledClient _provider = new();

    internal override AspApplication CreateApplication() => new(
        assistantEnabled: true,
        chatCompletionClient: _provider,
        settings: new Dictionary<string, string?> {
            ["Assistant:HeartbeatSeconds"] = "1",
        });

    [Test]
    public async Task AStreamClosedByTheClientLeavesNoUnhandledExceptionAsync() {
        try {
            using HttpClient client = await CreateSignedInClientAsync();
            string conversation = await CreateConversationAsync(client);
            string token = await GetAntiforgeryTokenAsync(client, conversation);
            using HttpResponseMessage submitted = await client.PostAsync(conversation + "/messages", Form(
                ("__RequestVerificationToken", token),
                ("content", "Say hello")));
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            using JsonDocument run = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync());
            string runId = run.RootElement.GetProperty("runId").GetString()!;

            using (HttpResponseMessage events = await client.GetAsync(
                $"{conversation}/events?run={runId}&after=0", HttpCompletionOption.ResponseHeadersRead)) {
                Assert.That(events.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                await using Stream stream = await events.Content.ReadAsStreamAsync();
                using StreamReader reader = new(stream);
                _ = await ReadUntilEventAsync(reader, "content.delta");
            }

            // Long enough for a heartbeat to try to write to a response nobody is reading.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.That(Logs.Entries.Any(entry =>
                entry.Level == LogLevel.Error
                && entry.Scope.GetValueOrDefault("Path") is string path
                && path.Contains("/events", StringComparison.Ordinal)
                && entry.Message.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase)), Is.False);
        } finally {
            _provider.Release();
        }
    }

    private sealed class StalledClient : IChatCompletionClient {

        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken) {
            yield return new ContentDelta("working");

            await Task.Yield();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }
}
