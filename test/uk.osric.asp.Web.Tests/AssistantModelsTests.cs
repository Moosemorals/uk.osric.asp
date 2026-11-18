// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Configuration;

[TestFixture]
public sealed class AssistantModelsTests {

    // The provider's own documented example, which is the two numbers this used to ask an operator
    // to keep in step by hand.
    private const string Documented = """
        {
            "object": "list",
            "data": [
                { "id": "deepseek-v4.1-flash", "object": "model", "context_window": 524288 },
                { "id": "deepseek-flash", "object": "model", "context_window": 1048576, "max_output_tokens": 393216 }
            ]
        }
        """;

    [Test]
    public void TheDocumentedEntryParsesToItsTwoFigures() {
        ModelWindowRead read = ProviderModelsClient.Parse(Documented, "deepseek-flash");

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.Ok));
            Assert.That(read.ContextWindow, Is.EqualTo(1_048_576));
            Assert.That(read.MaximumOutputTokens, Is.EqualTo(393_216));
            Assert.That(read.IsAvailable, Is.True);
        });
    }

    [Test]
    public void AnEntryThatOmitsTheWindowIsNoWindowAtAll() {
        string body = """{ "data": [ { "id": "deepseek-flash", "max_output_tokens": 393216 } ] }""";

        ModelWindowRead read = ProviderModelsClient.Parse(body, "deepseek-flash");

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.Incomplete));
            Assert.That(read.ContextWindow, Is.Null, "a guessed window is what the budget's decision refuses");
            Assert.That(read.IsAvailable, Is.False);
        });
    }

    [Test]
    public void AModelTheListDoesNotCarryIsAMissingModel() {
        ModelWindowRead read = ProviderModelsClient.Parse(Documented, "some-other-model");

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.ModelMissing));
            Assert.That(read.IsAvailable, Is.False);
        });
    }

    [Test]
    public void ABodyThatIsNotAModelListIsInvalid() {
        ModelWindowRead read = ProviderModelsClient.Parse("""{"error":{"message":"nope"}}""", "deepseek-flash");

        Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.Invalid));
    }

    [Test]
    public async Task AMissingModelStopsTheSiteNamingTheKeyAsync() {
        using ServiceProvider services = Services(ModelWindowRead.Failed(ModelWindowReason.ModelMissing));
        ModelWindowSampler sampler = services.GetRequiredService<ModelWindowSampler>();

        ConfigurationException error = await RefusalAsync(sampler);

        Assert.That(error.Message, Does.Contain("Assistant:Model"));
    }

    [Test]
    public async Task AProviderThatDidNotAnswerLeavesTheAssistantWithoutABudgetAsync() {
        using ServiceProvider services = Services(ModelWindowRead.Failed(ModelWindowReason.Unreachable));
        ModelWindowSampler sampler = services.GetRequiredService<ModelWindowSampler>();

        ModelWindowRead read = await sampler.RefreshAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.Unreachable));
            Assert.That(sampler.Budget, Is.Null, "every page still serves; only the assistant cannot run");
        });
    }

    [Test]
    public async Task AWindowThatLeavesTooLittleForThePromptStopsTheSiteNamingTheFloorAsync() {
        using ServiceProvider services = Services(ModelWindowRead.Ok(140_000, 393_216));
        ModelWindowSampler sampler = services.GetRequiredService<ModelWindowSampler>();

        ConfigurationException error = await RefusalAsync(sampler);

        Assert.That(error.Message, Does.Contain("Assistant:MinOutputTokens"));
    }

    [Test]
    public async Task AnAnsweredWindowIsTheBudgetTheRunIsGivenAsync() {
        using ServiceProvider services = Services(ModelWindowRead.Ok(1_048_576, 393_216));
        ModelWindowSampler sampler = services.GetRequiredService<ModelWindowSampler>();

        await sampler.RefreshAsync(CancellationToken.None);

        ContextBudget? budget = sampler.Budget;
        Assert.Multiple(() => {
            Assert.That(budget, Is.Not.Null);
            Assert.That(budget!.PromptBudget, Is.EqualTo(917_504));
            Assert.That(budget.MaximumOutputTokens, Is.EqualTo(393_216));
        });
    }

    [Test]
    public async Task TheReadAsksTheProvidersModelListWithTheAssistantsKeyAsync() {
        string? path = null;
        string? authorization = null;
        using HttpClient client = Client(request => {
            path = request.RequestUri!.AbsolutePath;
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Response(Documented));
        });
        ProviderModelsClient models = new(client, TestOptions.Assistant with { RequireApiKey = true, ApiKey = "configured" });

        ModelWindowRead read = await models.GetAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read.Reason, Is.EqualTo(ModelWindowReason.Ok));
            Assert.That(path, Is.EqualTo("/models"));
            Assert.That(authorization, Is.EqualTo("Bearer configured"));
        });
    }

    private static async Task<ConfigurationException> RefusalAsync(ModelWindowSampler sampler) {
        try {
            await sampler.RefreshAsync(CancellationToken.None);
        } catch (ConfigurationException exception) {
            return exception;
        }
        Assert.Fail("the site should have refused to start");
        throw new InvalidOperationException("unreachable");
    }

    private static ServiceProvider Services(ModelWindowRead answer) {
        ServiceCollection services = new();
        services.AddSingleton(TestOptions.Assistant);
        services.AddSingleton<IProviderModelsClient>(new FixedModelsClient(answer));
        services.AddSingleton<ModelWindowSource>();
        services.AddSingleton(services => new ModelWindowSampler(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<ModelWindowSource>(),
            services.GetRequiredService<AssistantOptions>(),
            NullLogger<ModelWindowSampler>.Instance));
        return services.BuildServiceProvider();
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class FixedModelsClient(ModelWindowRead answer) : IProviderModelsClient {
        public Task<ModelWindowRead> GetAsync(CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
