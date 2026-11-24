// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

/// <summary>The question and answer shapes, bounded as the tool, the route and the page read them.</summary>
[TestFixture]
public sealed class OwnerQuestionsTests {

    private const int MaxQuestions = 4;
    private const int MaxOptions = 5;
    private const int MaxChars = 500;

    [Test]
    public void AQuestionSetOverTheQuestionLimitIsRefusedAndNamesTheLimit() {
        OwnerQuestionSet set = Set(
            Question("One"), Question("Two"), Question("Three"), Question("Four"), Question("Five"));

        string? error = OwnerQuestions.Validate(set, MaxQuestions, MaxOptions, MaxChars);

        Assert.Multiple(() => {
            Assert.That(error, Is.Not.Null);
            Assert.That(error, Does.Contain("at most 4"));
            Assert.That(error, Does.Contain("had 5"));
        });
    }

    [Test]
    public void AnOptionListOverTheLimitIsRefusedAndNamesTheLimit() {
        OwnerQuestionSet set = Set(Question(
            "Which?",
            Option("one"), Option("two"), Option("three"), Option("four"), Option("five"), Option("six")));

        string? error = OwnerQuestions.Validate(set, MaxQuestions, MaxOptions, MaxChars);

        Assert.Multiple(() => {
            Assert.That(error, Does.Contain("offers 6 options"));
            Assert.That(error, Does.Contain("limit is 5"));
        });
    }

    [Test]
    public void AQuestionOverTheByteLimitIsRefusedRatherThanCut() {
        OwnerQuestionSet set = Set(Question(new string('q', MaxChars + 1)));

        string? error = OwnerQuestions.Validate(set, MaxQuestions, MaxOptions, MaxChars);

        Assert.Multiple(() => {
            Assert.That(error, Does.Contain($"{MaxChars + 1} bytes"));
            Assert.That(error, Does.Contain($"limit is {MaxChars}"));
        });
    }

    [Test]
    public void AnOptionWithNoLabelIsRefused() {
        OwnerQuestionSet set = Set(Question("Which?", Option(""), Option("two")));

        Assert.That(OwnerQuestions.Validate(set, MaxQuestions, MaxOptions, MaxChars), Does.Contain("no label"));
    }

    [Test]
    public void TwoOptionsOfOneQuestionWithTheSameLabelAreRefused() {
        OwnerQuestionSet set = Set(Question("Which?", Option("same"), Option("same")));

        Assert.That(OwnerQuestions.Validate(set, MaxQuestions, MaxOptions, MaxChars), Does.Contain("twice"));
    }

    [Test]
    public void TheAnswersAreReadBackInTheOrderTheyWereAsked() {
        OwnerQuestionSet set = Set(Question("First?"), Question("Second?", Option("two"), Option("three")));

        OwnerAnswersResult result = OwnerQuestions.Answer(
            set, [new OwnerAnswerInput([], "one"), new OwnerAnswerInput(["two"], "")], MaxChars);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Answers!.Answers.Select(answer => answer.Question), Is.EqualTo(["First?", "Second?"]));
            Assert.That(result.Answers.Answers[1].Selected, Is.EqualTo(["two"]));
        });
    }

    [Test]
    public void AQuestionLeftBlankIsCarriedAsAnEmptyAnswer() {
        OwnerQuestionSet set = Set(Question("Which?", Option("one"), Option("two")));

        OwnerAnswersResult result = OwnerQuestions.Answer(
            set, [new OwnerAnswerInput([], "")], MaxChars);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Answers!.Answers, Has.Count.EqualTo(1));
            Assert.That(result.Answers.Answers[0].Selected, Is.Empty);
            Assert.That(result.Answers.Answers[0].Text, Is.Empty);
        });
    }

    [Test]
    public void AnAnswerSetInWhichEveryQuestionWasSkippedIsCarried() {
        OwnerQuestionSet set = Set(Question("One"), Question("Two"));

        OwnerAnswersResult result = OwnerQuestions.Answer(
            set, [new OwnerAnswerInput([], ""), new OwnerAnswerInput([], "")], MaxChars);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AChoiceThatWasNotOfferedIsRefused() {
        OwnerQuestionSet set = Set(Question("Which?", Option("one"), Option("two")));

        OwnerAnswersResult result = OwnerQuestions.Answer(
            set, [new OwnerAnswerInput(["three"], "")], MaxChars);

        Assert.That(result.Error, Does.Contain("does not offer \"three\""));
    }

    [Test]
    public void AnAnswerOverTheCharacterLimitIsRefusedWithTheLimit() {
        OwnerQuestionSet set = Set(Question("Anything?"));

        OwnerAnswersResult result = OwnerQuestions.Answer(
            set, [new OwnerAnswerInput([], new string('a', 11))], 10);

        Assert.That(result.Error, Does.Contain("limit is 10"));
    }

    [Test]
    public void TheWireShapeRoundTripsThroughItsOwnParser() {
        OwnerQuestionSet set = Set(Question("Which page?", null, false, Option("/about", "The about page"), Option("/home")));

        string json = OwnerQuestions.Write(set);
        OwnerQuestionSet? read = OwnerQuestions.Read(json);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"questions\""));
            Assert.That(json, Does.Contain("\"header\":null"));
            Assert.That(read, Is.Not.Null);
            Assert.That(OwnerQuestions.Write(read!), Is.EqualTo(json));
            Assert.That(read!.Questions[0].Options[0].Description, Is.EqualTo("The about page"));
            Assert.That(OwnerQuestions.Write(Set(Question("Which page?", "Page", false))), Does.Contain("\"header\":\"Page\""));
        });
    }

    [Test]
    public void TheAnswerWireShapeRoundTripsThroughItsOwnParser() {
        OwnerAnswersResult result = OwnerQuestions.Answer(
            Set(Question("Which?", Option("one"), Option("two"))),
            [new OwnerAnswerInput(["two"], "and more")], MaxChars);

        string json = OwnerQuestions.WriteAnswers(result.Answers!);
        OwnerAnswerSet? read = OwnerQuestions.ReadAnswers(json);

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("\"selected\":[\"two\"]"));
            Assert.That(json, Does.Contain("\"text\":\"and more\""));
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Answers[0].Selected, Is.EqualTo(["two"]));
            Assert.That(read.Answers[0].Text, Is.EqualTo("and more"));
        });
    }

    [Test]
    public void AStoredShapeThisBuildCannotReadIsAbsentRatherThanAnError() {
        Assert.Multiple(() => {
            Assert.That(OwnerQuestions.Read("not json"), Is.Null);
            Assert.That(OwnerQuestions.Read(null), Is.Null);
            Assert.That(OwnerQuestions.ReadAnswers("{}"), Is.Null);
        });
    }

    [Test]
    public void AQuestionKeepsItsTextAndMarkupUnescapedInTheStoredForm() {
        string json = OwnerQuestions.Write(Set(Question("Use <b>bold</b> for café?")));

        Assert.Multiple(() => {
            Assert.That(json, Does.Contain("<b>bold</b> for café?"));
            Assert.That(json, Does.Not.Contain("\\u003C"));
            Assert.That(json, Does.Not.Contain("\\u00E9"));
        });
    }

    private static OwnerQuestionSet Set(params OwnerQuestion[] questions) => new(questions);

    private static OwnerQuestion Question(string question) => Question(question, null, false);

    private static OwnerQuestion Question(string question, params OwnerQuestionOption[] options) =>
        Question(question, null, false, options);

    private static OwnerQuestion Question(
        string question,
        string? header,
        bool multiSelect,
        params OwnerQuestionOption[] options) => new(question, header, options, multiSelect);

    private static OwnerQuestionOption Option(string label) => new(label, null);

    private static OwnerQuestionOption Option(string label, string description) => new(label, description);
}
