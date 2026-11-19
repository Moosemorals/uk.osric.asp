// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Tools;
using uk.osric.asp.Web.Pages;

/// <summary>The tool's job is the refusal and the result; the wait belongs to the run behind the seam.</summary>
[TestFixture]
public sealed class AskUserToolTests {

    private static readonly ToolContext Context = new("owner", ConversationId: 1, RunId: "run-1");

    [Test]
    public async Task TheAnswerIsTheToolResult() {
        FakeQuestions questions = new(Answers("Alpha"));
        AskUserTool tool = new();

        ToolExecutionResult result = await tool.RunAsync(
            Values("""{"questions":[{"question":"Which?","options":[{"label":"Alpha"},{"label":"Beta"}]}]}"""),
            Services(questions),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(result.Content, Does.Contain("\"ok\":true"));
            Assert.That(result.Content, Does.Contain("\"question\":\"Which?\""));
            Assert.That(result.Content, Does.Contain("\"selected\":[\"Alpha\"]"));
        });
    }

    [Test]
    public async Task AQuestionSetWithNoQuestionsIsRefused() {
        FakeQuestions questions = new(Answers("Alpha"));

        ToolExecutionResult result = await new AskUserTool().RunAsync(
            Values("""{"questions":[]}"""), Services(questions), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("at least one question"));
            Assert.That(questions.Calls, Is.Zero, "a refused set never reaches the owner");
        });
    }

    [Test]
    public async Task TheQuestionsTheToolAsksAreTheOnesItValidated() {
        FakeQuestions questions = new(Answers("Alpha"));

        await new AskUserTool().RunAsync(
            Values("""
                {"questions":[{"question":"Which page?","header":"Page","multiSelect":true,
                  "options":[{"label":"Alpha","description":"The first"},{"label":"Beta"}]}]}
                """),
            Services(questions),
            CancellationToken.None);

        OwnerQuestion asked = questions.Asked!.Questions.Single();
        Assert.Multiple(() => {
            Assert.That(asked.Question, Is.EqualTo("Which page?"));
            Assert.That(asked.Header, Is.EqualTo("Page"));
            Assert.That(asked.MultiSelect, Is.True);
            Assert.That(asked.Options.Select(option => option.Label), Is.EqualTo(["Alpha", "Beta"]));
            Assert.That(asked.Options[0].Description, Is.EqualTo("The first"));
        });
    }

    [Test]
    public async Task ASkippedQuestionIsAnEmptyEntryInTheResult() {
        FakeQuestions questions = new(new OwnerAnswerSet([new OwnerAnswer("Which?", null, [], "")]));

        ToolExecutionResult result = await new AskUserTool().RunAsync(
            Values("""{"questions":[{"question":"Which?"}]}"""), Services(questions), CancellationToken.None);

        JsonElement data = JsonElement.Parse(result.Content).GetProperty("data").GetProperty("answers")[0];
        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, result.Content);
            Assert.That(data.GetProperty("selected").GetArrayLength(), Is.Zero);
            Assert.That(data.GetProperty("text").GetString(), Is.Empty);
        });
    }

    [Test]
    public async Task WithoutARunThereIsNoOwnerToAsk() {
        ToolExecutionResult result = await new AskUserTool().RunAsync(
            Values("""{"questions":[{"question":"Which?"}]}"""), Services(questions: null), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Content, Does.Contain("no owner to ask"));
        });
    }

    private static OwnerAnswerSet Answers(string selected) =>
        new([new OwnerAnswer("Which?", null, [selected], "")]);

    private static ToolValues Values(string json) {
        ToolArgumentsResult parsed = ToolArguments.Parse(json, new AskUserTool().Spec.Arguments, 8_192);
        Assert.That(parsed.IsSuccess, Is.True, parsed.Error);

        return new ToolValues(parsed.Values);
    }

    private static ToolServices Services(IOwnerQuestions? questions) {
        ServiceCollection services = new();
        services.AddSingleton(TestOptions.Editor);

        return new ToolServices(
            services.BuildServiceProvider(),
            Context,
            new PagePathPolicy(new ReservedPaths(ReservedPaths.Default), TestOptions.Assistant),
            TestOptions.Assistant,
            TestOptions.Budget,
            new AssistantMetrics(),
            new FakeAssistantFetcher(),
            questions);
    }

    private sealed class FakeQuestions(OwnerAnswerSet? answers) : IOwnerQuestions {

        public OwnerQuestionSet? Asked { get; private set; }

        public int Calls { get; private set; }

        public Task<OwnerAnswerSet?> AskAsync(OwnerQuestionSet questions, CancellationToken cancellationToken) {
            Asked = questions;
            Calls++;

            return Task.FromResult(answers);
        }
    }
}
