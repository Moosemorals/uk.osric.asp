// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ChatRequestJsonTests {

    [Test]
    public void TheBodyCarriesEveryFieldTheProviderNeeds() {
        ChatRequest request = Request(
            [new("system", "prompt"), new("tool", "result", ToolCallId: "call-1", ToolName: "read_page")],
            [Tool]);
        request = request with {
            Messages = [.. request.Messages, new("assistant", "", """[{"id":"call-1","type":"function","function":{"name":"read_page","arguments":"{}"}}]""")],
        };

        using JsonDocument document = JsonDocument.Parse(ChatRequestJson.Serialize(request));
        JsonElement root = document.RootElement;

        Assert.Multiple(() => {
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("test-model"));
            Assert.That(root.GetProperty("stream").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("tool_choice").GetString(), Is.EqualTo("auto"));
            Assert.That(root.GetProperty("temperature").GetDouble(), Is.EqualTo(0.2));
            Assert.That(root.GetProperty("max_tokens").GetInt32(), Is.EqualTo(20));
            Assert.That(root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("tools")[0].GetProperty("type").GetString(), Is.EqualTo("function"));
            Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("read_page"));

            JsonElement messages = root.GetProperty("messages");
            Assert.That(messages.GetArrayLength(), Is.EqualTo(3));
            Assert.That(messages[0].GetProperty("role").GetString(), Is.EqualTo("system"));
            Assert.That(messages[1].GetProperty("tool_call_id").GetString(), Is.EqualTo("call-1"));
            Assert.That(messages[1].GetProperty("name").GetString(), Is.EqualTo("read_page"));
            Assert.That(messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString(), Is.EqualTo("read_page"));
        });
    }

    [Test]
    public void TheBodyOmitsUsageWhenItIsNotRequested() {
        using JsonDocument document = JsonDocument.Parse(ChatRequestJson.Serialize(Request([], []) with { IncludeUsage = false }));

        Assert.That(document.RootElement.TryGetProperty("stream_options", out _), Is.False);
    }

    [Test]
    public void MeasuringCountsUtf8BytesNotCharacters() {
        ChatRequest request = Request([new("user", "🙂")], []);

        long measured = ChatRequestJson.MeasureBytes(request);

        Assert.Multiple(() => {
            Assert.That(measured, Is.EqualTo(Encoding.UTF8.GetByteCount(ChatRequestJson.Serialize(request))));
            Assert.That(measured, Is.GreaterThan(Encoding.UTF8.GetByteCount("🙂")),
                "the escaped body is larger than the message's own text");
        });
    }

    [Test]
    public void TheToolDefinitionsAreInTheBodyAndTheMeasure() {
        ChatRequest withoutTools = Request([new("user", "hello")], []);
        ChatRequest withTools = withoutTools with { Tools = [Tool] };

        Assert.Multiple(() => {
            Assert.That(ChatRequestJson.Serialize(withTools), Does.Contain("read_page"));
            Assert.That(ChatRequestJson.MeasureBytes(withTools), Is.GreaterThan(ChatRequestJson.MeasureBytes(withoutTools)));
        });
    }

    [Test]
    public void TheEnvelopeIsInTheMeasure() {
        ChatRequest request = Request([new("user", "x")], []);

        Assert.That(ChatRequestJson.MeasureBytes(request), Is.GreaterThan(1),
            "the model, messages array and stream fields are part of the request");
    }

    [Test]
    public void TheSameRequestSerialisesToTheSameBytes() {
        ChatRequest request = Request(
            [new("assistant", "", """[{"id":"call-1","type":"function","function":{"name":"read_page","arguments":"{\"path\":\"/blog\"}"}}]""")],
            [Tool]);

        string first = ChatRequestJson.Serialize(request);

        Assert.That(ChatRequestJson.Serialize(request), Is.EqualTo(first));
    }

    [Test]
    public void ANullRequestIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => ChatRequestJson.Serialize(null!), Throws.ArgumentNullException);
            Assert.That(() => ChatRequestJson.MeasureBytes(null!), Throws.ArgumentNullException);
        });
    }

    private static ToolDefinition Tool { get; } = new(
        "read_page",
        "Reads a page.",
        false,
        """{"type":"object","properties":{"path":{"type":"string"}}}""");

    private static ChatRequest Request(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools) => new(
        "test-model",
        messages,
        tools,
        0.2,
        20,
        "reasoning_content",
        IncludeUsage: true);
}
